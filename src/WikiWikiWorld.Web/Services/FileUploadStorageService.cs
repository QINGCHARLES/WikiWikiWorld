using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Services;

/// <summary>Stages uploads and recovers only bytes claimed by an exact committed revision.</summary>
/// <param name="Context">The database context used to verify committed upload claims.</param>
/// <param name="FileStorageOptions">The upload root and uncommitted staging retention.</param>
public sealed class FileUploadStorageService(
	WikiWikiWorldDbContext Context,
	IOptions<FileStorageOptions> FileStorageOptions)
{
	/// <summary>Writes recoverable bytes before a database transaction; removes a partial copy if staging fails.</summary>
	/// <param name="UploadedFile">The image selected by the uploader.</param>
	/// <param name="SiteId">The site whose staging directory holds these bytes.</param>
	/// <param name="CanonicalFileId">The identity shared by this file's revisions.</param>
	/// <param name="CancellationToken">Cancels the copy before any revision is committed.</param>
	/// <returns>Metadata including the identifier that must be committed with the new file revision.</returns>
	public async Task<StagedFileUpload> StageUploadedFileAsync(IFormFile UploadedFile, int SiteId, Guid CanonicalFileId, CancellationToken CancellationToken)
	{
		ArgumentNullException.ThrowIfNull(UploadedFile);
		string OriginalFileName = Path.GetFileName(UploadedFile.FileName);
		string StagingDirectory = Path.Combine(GetStagingDirectory(SiteId), "v2");
		Directory.CreateDirectory(StagingDirectory);
		Guid StagingUploadId = Guid.NewGuid();
		string FileExtension = Path.GetExtension(OriginalFileName);
		string TemporaryFilePath = Path.Combine(StagingDirectory, $"{CanonicalFileId:D}.{StagingUploadId:N}{FileExtension}.tmp");

		try
		{
			await using (FileStream Output = new(TemporaryFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await UploadedFile.CopyToAsync(Output, CancellationToken);
			}
			// Dimensions are read after closing the exclusive writer; the staged extension itself is .tmp.
			(int WidthPixels, int HeightPixels) = ImageDimensionReader.ReadDimensions(TemporaryFilePath, FileExtension);
			return new(StagingUploadId, CanonicalFileId, OriginalFileName, UploadedFile.ContentType,
				UploadedFile.Length, TemporaryFilePath, WidthPixels, HeightPixels);
		}
		catch
		{
			// Staging has not returned, so no caller can have committed this newly generated identifier.
			File.Delete(TemporaryFilePath);
			throw;
		}
	}

	/// <summary>Moves committed bytes to their revision path without overwriting an existing destination.</summary>
	/// <param name="Upload">The exact staged upload committed with the revision.</param>
	/// <param name="SiteId">The site whose upload was committed.</param>
	/// <param name="RevisionId">The generated identifier of the committed file revision.</param>
	/// <remarks>Call after commit. Repeating a successful move is harmless when staging is absent and the final file exists.</remarks>
	/// <exception cref="IOException">An existing destination conflicts with staged bytes, or the move fails; staging is retained.</exception>
	public void FinalizeStagedUpload(StagedFileUpload Upload, int SiteId, int RevisionId)
	{
		ArgumentNullException.ThrowIfNull(Upload);
		MoveStagedFile(Upload.TemporaryFilePath, GetFinalFilePath(SiteId, Upload.CanonicalFileId, RevisionId, Upload.OriginalFileName));
	}

	/// <summary>Removes an abandoned upload only after checking that no committed revision, including a deleted one, claims it.</summary>
	/// <param name="Upload">The attempt to clean up, or null when staging never completed.</param>
	/// <param name="CancellationToken">Cancels the claim check; cancellation retains the staged bytes.</param>
	/// <returns>A task completing after unclaimed bytes are removed or committed bytes are retained.</returns>
	public async Task CleanupUncommittedUploadAsync(StagedFileUpload? Upload, CancellationToken CancellationToken)
	{
		if (Upload is null) return;
		bool IsCommitted = await Context.FileRevisions
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
			.AnyAsync(r => r.StagingUploadId == Upload.StagingUploadId, CancellationToken);
		if (!IsCommitted)
		{
			File.Delete(Upload.TemporaryFilePath);
		}
	}

	/// <summary>Recovers exact committed uploads, removes old unclaimed v2 attempts, and reports retained ambiguous files.</summary>
	/// <param name="CancellationToken">Cancels maintenance without deleting unchecked staging.</param>
	/// <returns>Move and cleanup counts plus issues whose staged bytes remain on disk.</returns>
	public async Task<FileUploadStartupMaintenanceResult> FinalizeCommittedUploadsAndDeleteStaleTempFilesAsync(CancellationToken CancellationToken)
	{
		DirectoryInfo Root = new(FileStorageOptions.Value.SiteFilesPath);
		int FinalizedCount = 0;
		int DeletedTempCount = 0;
		List<FileUploadRecoveryIssue> Issues = [];
		if (!Root.Exists) return new(0, 0, Issues);
		DateTime CutoffUtc = DateTime.UtcNow.AddMinutes(-FileStorageOptions.Value.StaleTempFileAgeMinutes);

		foreach (DirectoryInfo SiteDirectory in Root.EnumerateDirectories())
		{
			CancellationToken.ThrowIfCancellationRequested();
			if (!int.TryParse(SiteDirectory.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int SiteId) || SiteId < 1)
			{
				continue;
			}
			DirectoryInfo Staging = new(GetStagingDirectory(SiteId));
			if (!Staging.Exists)
			{
				continue;
			}
			// Old binaries only enumerate files directly in _staging. They cannot delete v2 files on rollback.
			foreach (FileInfo Legacy in Staging.EnumerateFiles("*.tmp"))
			{
				CancellationToken.ThrowIfCancellationRequested();
				Issues.Add(new(Legacy.FullName, "Legacy staging has no revision-specific claim; retained for reconciliation."));
			}
			DirectoryInfo VersionedStaging = new(Path.Combine(Staging.FullName, "v2"));
			if (!VersionedStaging.Exists)
			{
				continue;
			}
			foreach (FileInfo TemporaryFile in VersionedStaging.EnumerateFiles("*.tmp"))
			{
				CancellationToken.ThrowIfCancellationRequested();
				try
				{
					(bool Finalized, bool Deleted) = await RecoverOrDeleteAsync(SiteId, TemporaryFile, CutoffUtc, Issues, CancellationToken);
					if (Finalized)
					{
						FinalizedCount++;
					}
					if (Deleted)
					{
						DeletedTempCount++;
					}
				}
				catch (Exception Failure) when (Failure is IOException or UnauthorizedAccessException)
				{
					Issues.Add(new(TemporaryFile.FullName, $"Filesystem recovery failed; staging retained: {Failure.Message}"));
				}
			}
		}
		return new(FinalizedCount, DeletedTempCount, Issues);
	}

	/// <summary>Builds the established public file layout, with a distinct path for each persisted revision.</summary>
	/// <param name="SiteId">The site owning the physical bytes.</param>
	/// <param name="CanonicalFileId">The file's stable identity.</param>
	/// <param name="RevisionId">The committed revision identifier.</param>
	/// <param name="OriginalFileName">The revision filename supplying the extension.</param>
	/// <returns>The absolute final path below the configured site-files root.</returns>
	public string GetFinalFilePath(int SiteId, Guid CanonicalFileId, int RevisionId, string OriginalFileName)
	{
		string RelativePath = ImageUrlHelper.BuildImageRelativePath(CanonicalFileId, RevisionId, OriginalFileName);
		return Path.Combine(GetImageDirectory(SiteId), RelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>Resolves a v2 token against committed metadata before moving or age-deleting its bytes.</summary>
	/// <param name="SiteId">The site from the staging directory.</param>
	/// <param name="TemporaryFile">The v2 staging candidate.</param>
	/// <param name="CutoffUtc">Only unclaimed attempts older than this instant may be deleted.</param>
	/// <param name="Issues">Receives reasons for retaining ambiguous or deleted-revision staging.</param>
	/// <param name="CancellationToken">Cancels metadata queries.</param>
	/// <returns>Whether bytes were finalized or an old uncommitted attempt was deleted.</returns>
	private async Task<(bool Finalized, bool Deleted)> RecoverOrDeleteAsync(int SiteId, FileInfo TemporaryFile, DateTime CutoffUtc,
		List<FileUploadRecoveryIssue> Issues, CancellationToken CancellationToken)
	{
		if (!TryParseTemporaryFileName(TemporaryFile.Name, out Guid CanonicalFileId, out Guid StagingUploadId, out string Extension))
		{
			Issues.Add(new(TemporaryFile.FullName, "Unrecognized staging filename; retained for reconciliation."));
			return (false, false);
		}
		FileRevision? Revision = await Context.FileRevisions.AsNoTracking()
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter])
			.SingleOrDefaultAsync(r => r.StagingUploadId == StagingUploadId, CancellationToken);
		if (Revision is null)
		{
			if (TemporaryFile.LastWriteTimeUtc > CutoffUtc) return (false, false);
			TemporaryFile.Delete();
			return (false, true);
		}
		if (Revision.CanonicalFileId != CanonicalFileId || !Path.GetExtension(Revision.Filename).Equals(Extension, StringComparison.Ordinal))
		{
			Issues.Add(new(TemporaryFile.FullName, "Upload claim disagrees with canonical file or extension; retained for reconciliation."));
			return (false, false);
		}
		if (Revision.DateDeleted is not null)
		{
			Issues.Add(new(TemporaryFile.FullName, "Deleted revision still claims these bytes; retained without changing deletion policy."));
			return (false, false);
		}
		bool HasSiteAssociation = await Context.ArticleRevisions
			.IgnoreQueryFilters([WikiWikiWorldDbContext.SoftDeleteFilter, WikiWikiWorldDbContext.SiteFilter, WikiWikiWorldDbContext.CultureFilter])
			.AnyAsync(r => r.SiteId == SiteId && r.CanonicalFileId == CanonicalFileId, CancellationToken);
		if (!HasSiteAssociation)
		{
			Issues.Add(new(TemporaryFile.FullName, "Committed upload has no article association on this site; retained for reconciliation."));
			return (false, false);
		}
		bool Finalized = MoveStagedFile(TemporaryFile.FullName, GetFinalFilePath(SiteId, CanonicalFileId, Revision.Id, Revision.Filename));
		return (Finalized, false);
	}

	/// <summary>Moves once without replacement; a completed earlier move is accepted only when staging is absent.</summary>
	/// <param name="TemporaryFilePath">The exact bytes assigned to the revision.</param>
	/// <param name="FinalFilePath">The revision-specific destination.</param>
	/// <returns>True after a move; false when an earlier move already completed.</returns>
	/// <exception cref="IOException">The destination conflicts or the source cannot be moved.</exception>
	private static bool MoveStagedFile(string TemporaryFilePath, string FinalFilePath)
	{
		if (!File.Exists(TemporaryFilePath) && File.Exists(FinalFilePath)) return false;
		Directory.CreateDirectory(Path.GetDirectoryName(FinalFilePath)!);
		File.Move(TemporaryFilePath, FinalFilePath, overwrite: false);
		return true;
	}

	/// <summary>Reads only the exact v2 canonical/token/extension format; legacy and malformed names remain untouched.</summary>
	/// <param name="FileName">The temporary filename, without directories.</param>
	/// <param name="CanonicalFileId">The parsed canonical identity.</param>
	/// <param name="StagingUploadId">The token persisted with one revision.</param>
	/// <param name="Extension">The original file extension.</param>
	/// <returns>True when both identifiers and the extension are well formed.</returns>
	private static bool TryParseTemporaryFileName(string FileName, out Guid CanonicalFileId, out Guid StagingUploadId, out string Extension)
	{
		CanonicalFileId = Guid.Empty;
		StagingUploadId = Guid.Empty;
		Extension = string.Empty;
		// D-format canonical ID (36), dot, N-format upload ID (32), extension, and .tmp.
		if (FileName.Length < 75 || !FileName.EndsWith(".tmp", StringComparison.Ordinal) || FileName[36] != '.') return false;
		if (!Guid.TryParseExact(FileName[..36], "D", out CanonicalFileId) ||
			!Guid.TryParseExact(FileName[37..69], "N", out StagingUploadId)) return false;
		Extension = FileName[69..^4];
		return CanonicalFileId != Guid.Empty && StagingUploadId != Guid.Empty && Extension.Length > 1 &&
			Extension[0] == '.' && Path.GetExtension(Extension).Equals(Extension, StringComparison.Ordinal);
	}

	/// <summary>Locates the site's established image directory without changing served URLs.</summary>
	/// <param name="SiteId">The physical site's identifier.</param>
	/// <returns>The image root for this site.</returns>
	private string GetImageDirectory(int SiteId) => Path.Combine(FileStorageOptions.Value.SiteFilesPath, SiteId.ToString(CultureInfo.InvariantCulture), "images");

	/// <summary>Locates staging shared by retained legacy files and the versioned v2 subdirectory.</summary>
	/// <param name="SiteId">The physical site's identifier.</param>
	/// <returns>The staging root for this site.</returns>
	private string GetStagingDirectory(int SiteId) => Path.Combine(GetImageDirectory(SiteId), "_staging");
}

/// <summary>Retains the exact staging token and metadata until its revision is committed and finalized.</summary>
/// <param name="StagingUploadId">The identifier that must be saved on the new file revision.</param>
/// <param name="CanonicalFileId">The stable file identity.</param>
/// <param name="OriginalFileName">The uploader's filename without directories.</param>
/// <param name="ContentType">The uploaded MIME type.</param>
/// <param name="FileSizeBytes">The staged byte count.</param>
/// <param name="TemporaryFilePath">The exact v2 path; never reconstructed by modification time.</param>
/// <param name="ImageWidthPixels">The detected width, or zero when unavailable.</param>
/// <param name="ImageHeightPixels">The detected height, or zero when unavailable.</param>
public sealed record class StagedFileUpload(Guid StagingUploadId, Guid CanonicalFileId, string OriginalFileName,
	string ContentType, long FileSizeBytes, string TemporaryFilePath, int ImageWidthPixels, int ImageHeightPixels);

/// <summary>Describes staging retained for operator reconciliation rather than guessed or discarded.</summary>
/// <param name="TemporaryFilePath">The retained file's full path.</param>
/// <param name="Reason">The concrete reason automatic recovery or deletion was refused.</param>
public sealed record class FileUploadRecoveryIssue(string TemporaryFilePath, string Reason);

/// <summary>Reports startup recovery counts and retained files that require attention.</summary>
/// <param name="FinalizedCount">The number of exact committed uploads moved to final paths.</param>
/// <param name="DeletedTempCount">The number of old v2 attempts without any committed claim that were deleted.</param>
/// <param name="Issues">Retained legacy, conflicting, or otherwise unresolved staging.</param>
public sealed record class FileUploadStartupMaintenanceResult(int FinalizedCount, int DeletedTempCount, IReadOnlyList<FileUploadRecoveryIssue> Issues);
