using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Stages, finalizes, and recovers uploaded site files.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="FileStorageOptions">The file storage options.</param>
public sealed class FileUploadStorageService(
	WikiWikiWorldDbContext Context,
	IOptions<FileStorageOptions> FileStorageOptions)
{
	/// <summary>
	/// Stages an uploaded image file under a recoverable temporary path.
	/// </summary>
	/// <param name="UploadedFile">The uploaded file.</param>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The staged upload metadata and filesystem paths.</returns>
	public async Task<StagedFileUpload> StageUploadedFileAsync(IFormFile UploadedFile, int SiteId, Guid CanonicalFileId, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(UploadedFile);

		string OriginalFileName = Path.GetFileName(UploadedFile.FileName);
		string StagingDirectory = GetStagingDirectory(SiteId);
		Directory.CreateDirectory(StagingDirectory);

		string FileExtension = Path.GetExtension(OriginalFileName);
		string TemporaryFilePath = Path.Combine(StagingDirectory, $"{CanonicalFileId}{FileExtension}.{Guid.NewGuid():N}.tmp");

		await using FileStream FileStream = new(TemporaryFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		await UploadedFile.CopyToAsync(FileStream, CancellationToken);

		(int WidthPx, int HeightPx) = ImageDimensionReader.ReadDimensions(TemporaryFilePath);

		return new StagedFileUpload(
			CanonicalFileId,
			OriginalFileName,
			UploadedFile.ContentType,
			UploadedFile.Length,
			TemporaryFilePath,
			WidthPx,
			HeightPx);
	}

	/// <summary>
	/// Moves a staged upload into its final, revision-keyed path.
	/// </summary>
	/// <param name="Upload">The staged upload to finalize.</param>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="RevisionId">The persisted file revision identifier (<see cref="FileRevision.Id"/>).</param>
	/// <remarks>
	/// The final path depends on the revision id, which is only known after the
	/// <see cref="FileRevision"/> row has been saved, so callers must finalize after commit.
	/// </remarks>
	public void FinalizeStagedUpload(StagedFileUpload Upload, int SiteId, int RevisionId)
	{
		ArgumentNullException.ThrowIfNull(Upload);

		string FinalFilePath = GetFinalFilePath(SiteId, Upload.CanonicalFileId, RevisionId, Upload.OriginalFileName);
		string? FinalDirectory = Path.GetDirectoryName(FinalFilePath);
		if (!string.IsNullOrWhiteSpace(FinalDirectory))
		{
			Directory.CreateDirectory(FinalDirectory);
		}

		File.Move(Upload.TemporaryFilePath, FinalFilePath, overwrite: true);
	}

	/// <summary>
	/// Deletes a staged upload if it has not been committed to the database.
	/// </summary>
	/// <param name="Upload">The staged upload to delete, if present.</param>
	public void CleanupUncommittedUpload(StagedFileUpload? Upload)
	{
		if (Upload is null)
		{
			return;
		}

		if (File.Exists(Upload.TemporaryFilePath))
		{
			File.Delete(Upload.TemporaryFilePath);
		}
	}

	/// <summary>
	/// Finalizes committed staged uploads and deletes stale uncommitted temporary files.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A summary of the startup maintenance work performed.</returns>
	public async Task<FileUploadStartupMaintenanceResult> FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(CancellationToken CancellationToken)
	{
		int FinalizedCount = await FinalizeCommittedUploadsAsync(CancellationToken);
		int DeletedTempCount = await DeleteStaleTemporaryFilesAsync(CancellationToken);

		return new FileUploadStartupMaintenanceResult(FinalizedCount, DeletedTempCount);
	}

	/// <summary>
	/// Gets the final filesystem path for a specific revision of an uploaded file.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="RevisionId">The file revision identifier (<see cref="FileRevision.Id"/>).</param>
	/// <param name="OriginalFileName">The original uploaded filename.</param>
	/// <returns>The final filesystem path, keyed per revision so revisions never overwrite each other.</returns>
	public string GetFinalFilePath(int SiteId, Guid CanonicalFileId, int RevisionId, string OriginalFileName)
	{
		string RelativePath = ImageUrlHelper.BuildImageRelativePath(CanonicalFileId, RevisionId, OriginalFileName);
		return Path.Combine(GetImageDirectory(SiteId), RelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>
	/// Finalizes committed file revisions whose final file is missing but matching staged content exists.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The number of staged files finalized.</returns>
	private async Task<int> FinalizeCommittedUploadsAsync(CancellationToken CancellationToken)
	{
		List<FileRevision> CurrentFiles = await Context.FileRevisions
			.AsNoTracking()
			.Where(FileRevision => FileRevision.IsCurrent == true)
			.ToListAsync(CancellationToken);

		int FinalizedCount = 0;
		foreach (FileRevision FileRevision in CurrentFiles)
		{
			List<int> SiteIds = await Context.ArticleRevisions
				.AsNoTracking()
				.Where(ArticleRevision => ArticleRevision.CanonicalFileId == FileRevision.CanonicalFileId)
				.Select(ArticleRevision => ArticleRevision.SiteId)
				.Distinct()
				.ToListAsync(CancellationToken);

			foreach (int SiteId in SiteIds)
			{
				string FinalFilePath = GetFinalFilePath(SiteId, FileRevision.CanonicalFileId, FileRevision.Id, FileRevision.Filename);
				if (File.Exists(FinalFilePath))
				{
					continue;
				}

				FileInfo? StagedFile = FindNewestStagedFile(SiteId, FileRevision.CanonicalFileId, FileRevision.Filename);
				if (StagedFile is null)
				{
					continue;
				}

				string? FinalDirectory = Path.GetDirectoryName(FinalFilePath);
				if (!string.IsNullOrWhiteSpace(FinalDirectory))
				{
					Directory.CreateDirectory(FinalDirectory);
				}

				StagedFile.MoveTo(FinalFilePath, overwrite: true);
				FinalizedCount++;
			}
		}

		return FinalizedCount;
	}

	/// <summary>
	/// Deletes temporary staged files that no current file revision can claim.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The number of temporary files deleted.</returns>
	private async Task<int> DeleteStaleTemporaryFilesAsync(CancellationToken CancellationToken)
	{
		DirectoryInfo SiteFilesDirectory = new(FileStorageOptions.Value.SiteFilesPath);
		if (!SiteFilesDirectory.Exists)
		{
			return 0;
		}

		DateTimeOffset CutoffUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(FileStorageOptions.Value.StaleTempFileAgeMinutes);
		int DeletedCount = 0;

		foreach (DirectoryInfo StagingDirectory in SiteFilesDirectory.EnumerateDirectories("*", SearchOption.AllDirectories)
			         .Where(Directory => Directory.Name.Equals("_staging", StringComparison.OrdinalIgnoreCase)))
		{
			foreach (FileInfo TemporaryFile in StagingDirectory.EnumerateFiles("*.tmp"))
			{
				CancellationToken.ThrowIfCancellationRequested();

				if (TemporaryFile.LastWriteTimeUtc > CutoffUtc.UtcDateTime)
				{
					continue;
				}

				if (await IsTemporaryFileClaimedAsync(TemporaryFile, CancellationToken))
				{
					continue;
				}

				TemporaryFile.Delete();
				DeletedCount++;
			}
		}

		return DeletedCount;
	}

	/// <summary>
	/// Determines whether a staged temporary file can still be matched to a current file revision.
	/// </summary>
	/// <param name="TemporaryFile">The staged temporary file.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>True when the temporary file belongs to a current file revision; otherwise, false.</returns>
	private async Task<bool> IsTemporaryFileClaimedAsync(FileInfo TemporaryFile, CancellationToken CancellationToken)
	{
		if (!TryParseTemporaryFileName(TemporaryFile.Name, out Guid CanonicalFileId, out string FileExtension))
		{
			return false;
		}

		if (!TryGetSiteId(TemporaryFile, out int SiteId))
		{
			return false;
		}

		List<FileRevision> MatchingRevisions = await Context.FileRevisions
			.AsNoTracking()
			.Where(FileRevision => FileRevision.IsCurrent == true && FileRevision.CanonicalFileId == CanonicalFileId)
			.ToListAsync(CancellationToken);

		return MatchingRevisions.Any(FileRevision =>
			Path.GetExtension(FileRevision.Filename).Equals(FileExtension, StringComparison.OrdinalIgnoreCase)
			&& !File.Exists(GetFinalFilePath(SiteId, CanonicalFileId, FileRevision.Id, FileRevision.Filename)));
	}

	/// <summary>
	/// Finds the newest staged temporary file for a file revision.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="OriginalFileName">The original uploaded filename.</param>
	/// <returns>The newest matching staged file, or null.</returns>
	private FileInfo? FindNewestStagedFile(int SiteId, Guid CanonicalFileId, string OriginalFileName)
	{
		DirectoryInfo StagingDirectory = new(GetStagingDirectory(SiteId));
		if (!StagingDirectory.Exists)
		{
			return null;
		}

		string FileExtension = Path.GetExtension(OriginalFileName);
		string SearchPattern = $"{CanonicalFileId}{FileExtension}.*.tmp";

		return StagingDirectory
			.EnumerateFiles(SearchPattern)
			.OrderByDescending(File => File.LastWriteTimeUtc)
			.FirstOrDefault();
	}

	/// <summary>
	/// Parses a recoverable temporary upload filename.
	/// </summary>
	/// <param name="FileName">The temporary filename.</param>
	/// <param name="CanonicalFileId">The parsed canonical file identifier.</param>
	/// <param name="FileExtension">The parsed file extension.</param>
	/// <returns>True when parsing succeeds; otherwise, false.</returns>
	private static bool TryParseTemporaryFileName(string FileName, out Guid CanonicalFileId, out string FileExtension)
	{
		CanonicalFileId = Guid.Empty;
		FileExtension = string.Empty;

		if (!FileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string WithoutTempExtension = FileName[..^4];
		int FirstDotIndex = WithoutTempExtension.IndexOf('.');
		int LastDotIndex = WithoutTempExtension.LastIndexOf('.');

		if (FirstDotIndex <= 0 || LastDotIndex <= FirstDotIndex)
		{
			return false;
		}

		string CanonicalFileIdText = WithoutTempExtension[..FirstDotIndex];
		if (!Guid.TryParse(CanonicalFileIdText, out CanonicalFileId))
		{
			return false;
		}

		FileExtension = WithoutTempExtension[FirstDotIndex..LastDotIndex];
		return !string.IsNullOrWhiteSpace(FileExtension);
	}

	/// <summary>
	/// Attempts to read the site identifier from a staged temporary file path.
	/// </summary>
	/// <param name="TemporaryFile">The staged temporary file.</param>
	/// <param name="SiteId">The parsed site identifier.</param>
	/// <returns>True when parsing succeeds; otherwise, false.</returns>
	private static bool TryGetSiteId(FileInfo TemporaryFile, out int SiteId)
	{
		SiteId = 0;

		DirectoryInfo? ImagesDirectory = TemporaryFile.Directory?.Parent;
		DirectoryInfo? SiteDirectory = ImagesDirectory?.Parent;

		return SiteDirectory is not null
			&& int.TryParse(SiteDirectory.Name, CultureInfo.InvariantCulture, out SiteId);
	}

	/// <summary>
	/// Gets the final image directory for a site.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <returns>The final image directory path.</returns>
	private string GetImageDirectory(int SiteId)
		=> Path.Combine(FileStorageOptions.Value.SiteFilesPath, SiteId.ToString(CultureInfo.InvariantCulture), "images");

	/// <summary>
	/// Gets the staging directory for a site.
	/// </summary>
	/// <param name="SiteId">The site identifier.</param>
	/// <returns>The staging directory path.</returns>
	private string GetStagingDirectory(int SiteId)
		=> Path.Combine(GetImageDirectory(SiteId), "_staging");
}

/// <summary>
/// Describes an uploaded file staged on disk before database commit.
/// </summary>
/// <param name="CanonicalFileId">The canonical file identifier.</param>
/// <param name="OriginalFileName">The original uploaded filename.</param>
/// <param name="ContentType">The uploaded content type.</param>
/// <param name="FileSizeBytes">The uploaded file size in bytes.</param>
/// <param name="TemporaryFilePath">The temporary staged file path.</param>
/// <param name="ImageWidthPixels">The detected image width in pixels.</param>
/// <param name="ImageHeightPixels">The detected image height in pixels.</param>
/// <remarks>
/// The final path is intentionally absent: it depends on the file revision id,
/// which is only assigned once the revision is persisted. Call
/// <see cref="FileUploadStorageService.FinalizeStagedUpload"/> with that id after commit.
/// </remarks>
public sealed record class StagedFileUpload(
	Guid CanonicalFileId,
	string OriginalFileName,
	string ContentType,
	long FileSizeBytes,
	string TemporaryFilePath,
	int ImageWidthPixels,
	int ImageHeightPixels);

/// <summary>
/// Summarizes upload filesystem recovery work.
/// </summary>
/// <param name="FinalizedCount">The number of committed staged files moved to final paths.</param>
/// <param name="DeletedTempCount">The number of stale temporary files deleted.</param>
public sealed record class FileUploadStartupMaintenanceResult(int FinalizedCount, int DeletedTempCount);
