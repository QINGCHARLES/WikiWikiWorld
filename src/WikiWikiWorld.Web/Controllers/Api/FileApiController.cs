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
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;

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
	/// Uploads an image file as a new revision for the file article identified by the given slug.
	/// </summary>
	/// <param name="UrlSlug">The URL slug of the article.</param>
	/// <param name="File">The image file to upload.</param>
	/// <param name="Source">The optional source of the file.</param>
	/// <param name="RevisionReason">The optional reason for the revision.</param>
	/// <param name="Culture">The culture of the source/revision reason (required if either is provided).</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The CanonicalFileId of the newly created file revision.</returns>
	[HttpPost("{UrlSlug}")]
	[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
	public async Task<IActionResult> UploadFile(
		string UrlSlug,
		[FromForm] IFormFile File,
		[FromForm] string? Source,
		[FromForm] string? RevisionReason,
		[FromForm] string? Culture,
		CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(UrlSlug))
		{
			return BadRequest("URL slug is required.");
		}

		if (File is null || File.Length == 0)
		{
			return BadRequest("No file was uploaded.");
		}

		if ((!string.IsNullOrWhiteSpace(Source) || !string.IsNullOrWhiteSpace(RevisionReason)) && string.IsNullOrWhiteSpace(Culture))
		{
			return BadRequest("Culture is required when providing a Source or RevisionReason.");
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

		(int SiteId, string ResolvedCulture) = SiteResolverService.ResolveSiteAndCulture();

		ArticleRevisionsBySlugSpec Spec = new(UrlSlug, IsCurrent: true);
		ArticleRevision? CurrentArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync(CancellationToken);

		if (CurrentArticle is null)
		{
			return NotFound("Article not found.");
		}

		if (CurrentArticle.Type != ArticleType.File || CurrentArticle.CanonicalFileId is null)
		{
			return BadRequest("Article is not a file article or is missing a canonical file ID.");
		}

		Guid CanonicalFileId = CurrentArticle.CanonicalFileId.Value;
		string FinalCulture = string.IsNullOrWhiteSpace(Culture) ? ResolvedCulture : Culture;
		string FinalRevisionReason = string.IsNullOrWhiteSpace(RevisionReason) ? "API upload" : RevisionReason;

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

				FileRevision? CurrentFile = await Context.FileRevisions
					.Where(f => f.CanonicalFileId == CanonicalFileId && f.IsCurrent == true)
					.FirstOrDefaultAsync(CT);

				if (CurrentFile is not null)
				{
					CurrentFile.IsCurrent = false;
					Context.FileRevisions.Update(CurrentFile);
				}

				FileRevision NewFile = new()
				{
					CanonicalFileId = CanonicalFileId,
					Type = FileType.Image2D,
					Filename = OriginalFileName,
					MimeType = File.ContentType,
					FileSizeBytes = File.Length,
					Source = Source,
					RevisionReason = FinalRevisionReason,
					SourceAndRevisionReasonCulture = FinalCulture,
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
