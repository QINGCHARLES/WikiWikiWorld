using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// One-off maintenance helper that migrates legacy, non-revisioned image files
/// (<c>images/{CanonicalFileId}{ext}</c>) into the revision-keyed layout
/// (<c>images/{CanonicalFileId}/{RevisionId}{ext}</c>).
/// </summary>
/// <remarks>
/// This is a temporary tool intended to be run once by an administrator. No database
/// writes are required: the served URL is derived from the existing
/// <see cref="FileRevision.Id"/> and <see cref="FileRevision.Filename"/>, so migrating
/// only moves files on disk. The scan also surfaces canonical files that have multiple
/// revisions in the database but for which the older revisions' bytes were overwritten
/// (and therefore permanently lost) under the legacy layout.
/// </remarks>
/// <param name="Context">The database context.</param>
/// <param name="FileStorageOptions">The file storage options.</param>
public sealed class FileMigrationService(
	WikiWikiWorldDbContext Context,
	IOptions<FileStorageOptions> FileStorageOptions)
{
	/// <summary>
	/// Scans the site files directory for legacy-layout image files without moving anything.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A report describing every legacy file found and any lost revision history.</returns>
	public async Task<FileMigrationReport> ScanAsync(CancellationToken CancellationToken)
		=> await BuildReportAsync(Execute: false, CancellationToken);

	/// <summary>
	/// Moves every legacy-layout image file into its revision-keyed location.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A report describing the result of each move.</returns>
	public async Task<FileMigrationReport> MigrateAsync(CancellationToken CancellationToken)
		=> await BuildReportAsync(Execute: true, CancellationToken);

	private async Task<FileMigrationReport> BuildReportAsync(bool Execute, CancellationToken CancellationToken)
	{
		List<FileMigrationItem> Items = [];
		List<LostRevisionGroup> LostHistory = [];

		string Root = FileStorageOptions.Value.SiteFilesPath;
		if (!Directory.Exists(Root))
		{
			return new FileMigrationReport(Items, LostHistory, Root);
		}

		// 1. Discover legacy files (files sitting directly in a site's images/ directory whose
		//    name is a bare GUID), grouped by canonical id, across every site.
		List<LegacyFile> LegacyFiles = [];
		foreach (string SiteDirectory in Directory.EnumerateDirectories(Root))
		{
			if (!int.TryParse(Path.GetFileName(SiteDirectory), CultureInfo.InvariantCulture, out int SiteId))
			{
				continue;
			}

			string ImagesDirectory = Path.Combine(SiteDirectory, "images");
			if (!Directory.Exists(ImagesDirectory))
			{
				continue;
			}

			foreach (string FilePath in Directory.EnumerateFiles(ImagesDirectory))
			{
				string NameWithoutExtension = Path.GetFileNameWithoutExtension(FilePath);
				if (Guid.TryParse(NameWithoutExtension, out Guid CanonicalFileId))
				{
					LegacyFiles.Add(new LegacyFile(SiteId, CanonicalFileId, FilePath, Path.GetExtension(FilePath)));
				}
			}
		}

		if (LegacyFiles.Count == 0)
		{
			return new FileMigrationReport(Items, LostHistory, Root);
		}

		// 2. Load every revision for the canonical ids we found.
		List<Guid> CanonicalFileIds = [.. LegacyFiles.Select(F => F.CanonicalFileId).Distinct()];
		List<FileRevision> Revisions = await Context.FileRevisions
			.AsNoTracking()
			.Where(F => CanonicalFileIds.Contains(F.CanonicalFileId))
			.ToListAsync(CancellationToken);

		Dictionary<Guid, List<FileRevision>> RevisionsByCanonicalId = Revisions
			.GroupBy(R => R.CanonicalFileId)
			.ToDictionary(G => G.Key, G => G.OrderBy(R => R.Id).ToList());

		Dictionary<Guid, int> LegacyFileCountByCanonicalId = LegacyFiles
			.GroupBy(F => F.CanonicalFileId)
			.ToDictionary(G => G.Key, G => G.Count());

		// 3. Build a row per legacy file.
		foreach (LegacyFile Legacy in LegacyFiles.OrderBy(F => F.SiteId).ThenBy(F => F.CanonicalFileId))
		{
			if (!RevisionsByCanonicalId.TryGetValue(Legacy.CanonicalFileId, out List<FileRevision>? FileRevisions) || FileRevisions.Count == 0)
			{
				Items.Add(new FileMigrationItem(
					Legacy.SiteId,
					Legacy.CanonicalFileId,
					ToRelative(Root, Legacy.FullPath),
					TargetRelativePath: null,
					FileMigrationStatus.OrphanNoDbMatch,
					RevisionCount: 0,
					Note: "No file revision rows exist for this canonical id."));
				continue;
			}

			FileRevision? Target = SelectTargetRevision(FileRevisions, Legacy.Extension);
			string? Note = null;
			if (Target is null)
			{
				// No extension match; fall back to the current (or newest) revision and flag it.
				Target = FileRevisions.FirstOrDefault(R => R.IsCurrent == true) ?? FileRevisions[^1];
				Note = $"Legacy extension '{Legacy.Extension}' does not match any revision's filename; mapped to revision {Target.Id} ('{Target.Filename}').";
			}

			string TargetFullPath = GetFinalPath(Root, Legacy.SiteId, Legacy.CanonicalFileId, Target);
			string TargetRelative = ToRelative(Root, TargetFullPath);

			FileMigrationStatus Status;
			if (File.Exists(TargetFullPath))
			{
				Status = FileMigrationStatus.AlreadyMigrated;
			}
			else if (Execute)
			{
				try
				{
					string? Directory = Path.GetDirectoryName(TargetFullPath);
					if (!string.IsNullOrWhiteSpace(Directory))
					{
						System.IO.Directory.CreateDirectory(Directory);
					}

					File.Move(Legacy.FullPath, TargetFullPath, overwrite: false);
					Status = FileMigrationStatus.Moved;
				}
				catch (Exception Ex)
				{
					Status = FileMigrationStatus.Failed;
					Note = string.Join(" ", [Note, $"Move failed: {Ex.Message}"]).Trim();
				}
			}
			else
			{
				Status = FileMigrationStatus.ReadyToMove;
			}

			Items.Add(new FileMigrationItem(
				Legacy.SiteId,
				Legacy.CanonicalFileId,
				ToRelative(Root, Legacy.FullPath),
				TargetRelative,
				Status,
				FileRevisions.Count,
				Note));
		}

		// 4. Flag canonical files whose history is partly lost: more revisions in the DB than
		//    surviving legacy files on disk means older revisions were overwritten.
		foreach ((Guid CanonicalFileId, List<FileRevision> FileRevisions) in RevisionsByCanonicalId)
		{
			int SurvivingFiles = LegacyFileCountByCanonicalId.GetValueOrDefault(CanonicalFileId, 0);
			if (FileRevisions.Count > SurvivingFiles)
			{
				LostHistory.Add(new LostRevisionGroup(
					CanonicalFileId,
					FileRevisions.Count,
					SurvivingFiles,
					FileRevisions.Count - SurvivingFiles));
			}
		}

		return new FileMigrationReport(Items, [.. LostHistory.OrderByDescending(G => G.LostRevisionCount)], Root);
	}

	/// <summary>
	/// Picks the revision a legacy file's bytes belong to, matching on file extension and
	/// preferring the current revision.
	/// </summary>
	private static FileRevision? SelectTargetRevision(List<FileRevision> Revisions, string Extension)
	{
		List<FileRevision> ExtensionMatches = [.. Revisions
			.Where(R => Path.GetExtension(R.Filename).Equals(Extension, StringComparison.OrdinalIgnoreCase))];

		if (ExtensionMatches.Count == 0)
		{
			return null;
		}

		return ExtensionMatches.FirstOrDefault(R => R.IsCurrent == true) ?? ExtensionMatches[^1];
	}

	private string GetFinalPath(string Root, int SiteId, Guid CanonicalFileId, FileRevision Revision)
	{
		string Relative = ImageUrlHelper.BuildImageRelativePath(CanonicalFileId, Revision.Id, Revision.Filename);
		return Path.Combine(Root, SiteId.ToString(CultureInfo.InvariantCulture), "images", Relative.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string ToRelative(string Root, string FullPath)
		=> Path.GetRelativePath(Root, FullPath).Replace('\\', '/');

	private sealed record LegacyFile(int SiteId, Guid CanonicalFileId, string FullPath, string Extension);
}

/// <summary>
/// The result of scanning or migrating legacy image files.
/// </summary>
/// <param name="Items">One row per legacy file discovered on disk.</param>
/// <param name="LostHistory">Canonical files whose older revision bytes were overwritten and lost.</param>
/// <param name="RootPath">The site files root that was scanned.</param>
public sealed record FileMigrationReport(
	IReadOnlyList<FileMigrationItem> Items,
	IReadOnlyList<LostRevisionGroup> LostHistory,
	string RootPath);

/// <summary>
/// A single legacy file and what should happen (or happened) to it.
/// </summary>
/// <param name="SiteId">The site the file belongs to.</param>
/// <param name="CanonicalFileId">The canonical file id parsed from the legacy filename.</param>
/// <param name="SourceRelativePath">The legacy file path, relative to the site files root.</param>
/// <param name="TargetRelativePath">The revision-keyed destination path, relative to the site files root.</param>
/// <param name="Status">The migration status for this file.</param>
/// <param name="RevisionCount">The number of revisions the database holds for this canonical id.</param>
/// <param name="Note">An optional note explaining ambiguity or failures.</param>
public sealed record FileMigrationItem(
	int SiteId,
	Guid CanonicalFileId,
	string SourceRelativePath,
	string? TargetRelativePath,
	FileMigrationStatus Status,
	int RevisionCount,
	string? Note);

/// <summary>
/// A canonical file whose history is partly unrecoverable under the legacy layout.
/// </summary>
/// <param name="CanonicalFileId">The canonical file id.</param>
/// <param name="RevisionCount">The number of revisions recorded in the database.</param>
/// <param name="SurvivingFileCount">The number of legacy files still present on disk.</param>
/// <param name="LostRevisionCount">The number of revisions whose bytes are gone.</param>
public sealed record LostRevisionGroup(
	Guid CanonicalFileId,
	int RevisionCount,
	int SurvivingFileCount,
	int LostRevisionCount);

/// <summary>
/// The disposition of a single legacy file during scan or migration.
/// </summary>
public enum FileMigrationStatus
{
	/// <summary>The file can be moved into the revision-keyed layout.</summary>
	ReadyToMove,
	/// <summary>The file was moved successfully.</summary>
	Moved,
	/// <summary>A file already exists at the destination; nothing was done.</summary>
	AlreadyMigrated,
	/// <summary>No matching file revision rows exist for the canonical id.</summary>
	OrphanNoDbMatch,
	/// <summary>The move was attempted but failed.</summary>
	Failed
}
