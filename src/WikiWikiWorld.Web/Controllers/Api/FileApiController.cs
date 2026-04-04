using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Configuration;
using WikiWikiWorld.Web.Helpers;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// API controller for file upload and metadata retrieval.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="FileStorageOptions">The file storage options.</param>
[Route("api/file")]
[ApiController]
public sealed class FileApiController(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	IOptions<FileStorageOptions> FileStorageOptions) : ControllerBase
{
	/// <summary>
	/// Uploads an image file, validates it, and creates a FileRevision record.
	/// </summary>
	/// <param name="File">The image file to upload.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The CanonicalFileId of the newly created file revision.</returns>
	[HttpPost]
	[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
	public async Task<IActionResult> UploadFile(IFormFile File, CancellationToken CancellationToken)
	{
		if (File is null || File.Length == 0)
		{
			return BadRequest("No file was uploaded.");
		}

		if (!ImageValidationHelper.IsValidImageFile(File, out string ValidationError))
		{
			return BadRequest(ValidationError);
		}

		string? UserId = User.Claims.FirstOrDefault(C => C.Type == ClaimTypes.NameIdentifier)?.Value;
		if (!Guid.TryParse(UserId, out Guid ParsedUserId))
		{
			return Unauthorized("User ID not found in token.");
		}

		(int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

		Guid CanonicalFileId = Guid.NewGuid();
		string OriginalFileName = Path.GetFileName(File.FileName);
		string FileExtension = Path.GetExtension(OriginalFileName);
		string UniqueFileName = $"{CanonicalFileId}{FileExtension}";

		string SiteFilesDirectory = Path.Combine(
			FileStorageOptions.Value.SiteFilesPath,
			SiteId.ToString(),
			"images");
		Directory.CreateDirectory(SiteFilesDirectory);

		string FinalFilePath = Path.Combine(SiteFilesDirectory, UniqueFileName);
		string TemporaryFilePath = Path.Combine(SiteFilesDirectory, $"{UniqueFileName}.{Guid.NewGuid():N}.tmp");

		try
		{
			// Stage the file to a temporary path
			await using (FileStream FileStream = new(TemporaryFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await File.CopyToAsync(FileStream, CancellationToken);
			}

			IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

			FileUploadResponse Response = await Strategy.ExecuteAsync(async CT =>
			{
				await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

				FileRevision NewFile = new()
				{
					CanonicalFileId = CanonicalFileId,
					Type = FileType.Image2D,
					Filename = OriginalFileName,
					MimeType = File.ContentType,
					FileSizeBytes = File.Length,
					Source = null,
					RevisionReason = "API upload",
					SourceAndRevisionReasonCulture = Culture,
					CreatedByUserId = ParsedUserId,
					DateCreated = DateTimeOffset.UtcNow,
					IsCurrent = true
				};

				Context.FileRevisions.Add(NewFile);
				await Context.SaveChangesAsync(CT);

				// Move from temp to final path
				System.IO.File.Move(TemporaryFilePath, FinalFilePath, overwrite: false);

				await Transaction.CommitAsync(CT);

				return new FileUploadResponse(CanonicalFileId, OriginalFileName, File.ContentType, File.Length);
			}, CancellationToken);

			return Ok(Response);
		}
		catch (Exception)
		{
			// Clean up staged files on failure
			if (System.IO.File.Exists(TemporaryFilePath))
			{
				System.IO.File.Delete(TemporaryFilePath);
			}

			if (System.IO.File.Exists(FinalFilePath))
			{
				System.IO.File.Delete(FinalFilePath);
			}

			throw;
		}
	}

	/// <summary>
	/// Gets metadata for a file revision by its canonical file ID.
	/// </summary>
	/// <param name="CanonicalFileId">The canonical file identifier.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The file revision metadata.</returns>
	[HttpGet("{CanonicalFileId}")]
	public async Task<IActionResult> GetFileMetadata(Guid CanonicalFileId, CancellationToken CancellationToken)
	{
		FileRevision? FileRevision = await Context.FileRevisions
			.Where(F => F.CanonicalFileId == CanonicalFileId && F.IsCurrent == true)
			.AsNoTracking()
			.FirstOrDefaultAsync(CancellationToken);

		if (FileRevision is null)
		{
			return NotFound("File not found.");
		}

		return Ok(new FileMetadataResponse(
			FileRevision.CanonicalFileId,
			FileRevision.Filename,
			FileRevision.MimeType,
			FileRevision.FileSizeBytes,
			FileRevision.Type,
			FileRevision.Source,
			FileRevision.DateCreated));
	}
}

/// <summary>
/// Response for a successful file upload.
/// </summary>
/// <param name="CanonicalFileId">The canonical file identifier.</param>
/// <param name="Filename">The original filename.</param>
/// <param name="MimeType">The MIME type.</param>
/// <param name="FileSizeBytes">The file size in bytes.</param>
public sealed record FileUploadResponse(Guid CanonicalFileId, string Filename, string MimeType, long FileSizeBytes);

/// <summary>
/// Response for file metadata retrieval.
/// </summary>
/// <param name="CanonicalFileId">The canonical file identifier.</param>
/// <param name="Filename">The filename.</param>
/// <param name="MimeType">The MIME type.</param>
/// <param name="FileSizeBytes">The file size in bytes.</param>
/// <param name="Type">The file type.</param>
/// <param name="Source">The file source, if any.</param>
/// <param name="DateCreated">The creation date.</param>
public sealed record FileMetadataResponse(
	Guid CanonicalFileId,
	string Filename,
	string MimeType,
	long FileSizeBytes,
	FileType Type,
	string? Source,
	DateTimeOffset DateCreated);
