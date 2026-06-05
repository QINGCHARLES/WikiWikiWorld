using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Helpers;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// API controller for file upload and metadata retrieval.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="FileUploadStorageService">The file upload storage service.</param>
[Route("api/file")]
[ApiController]
[Produces("application/json")]
public sealed class FileApiController(
	WikiWikiWorldDbContext Context,
	SiteResolverService SiteResolverService,
	FileUploadStorageService FileUploadStorageService) : ControllerBase
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
	[Consumes("multipart/form-data")]
	[ProducesResponseType<FileUploadResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
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
		StagedFileUpload? StagedUpload = null;

		try
		{
			StagedUpload = await FileUploadStorageService.StageUploadedFileAsync(File, SiteId, CanonicalFileId, CancellationToken);

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
					Filename = StagedUpload.OriginalFileName,
					MimeType = StagedUpload.ContentType,
					FileSizeBytes = StagedUpload.FileSizeBytes,
					ImageWidthPixels = StagedUpload.ImageWidthPixels > 0 ? StagedUpload.ImageWidthPixels : null,
					ImageHeightPixels = StagedUpload.ImageHeightPixels > 0 ? StagedUpload.ImageHeightPixels : null,
					Source = Source,
					RevisionReason = FinalRevisionReason,
					SourceAndRevisionReasonCulture = FinalCulture,
					CreatedByUserId = ParsedUserId,
					DateCreated = DateTimeOffset.UtcNow,
					IsCurrent = true
				};

				Context.FileRevisions.Add(NewFile);

				using (WriteDurabilityScope.High())
				{
					await Context.SaveChangesAsync(CT);
				}

				await Transaction.CommitAsync(CT);

				return new FileUploadResponse(
					CanonicalFileId,
					StagedUpload.OriginalFileName,
					StagedUpload.ContentType,
					StagedUpload.FileSizeBytes,
					StagedUpload.ImageWidthPixels > 0 ? StagedUpload.ImageWidthPixels : null,
					StagedUpload.ImageHeightPixels > 0 ? StagedUpload.ImageHeightPixels : null);
			}, CancellationToken);

			try
			{
				FileUploadStorageService.FinalizeStagedUpload(StagedUpload);
			}
			catch (Exception Ex)
			{
				return StatusCode(StatusCodes.Status202Accepted, $"Upload metadata was saved, but file finalization is pending recovery: {Ex.Message}");
			}

			return Ok(Response);
		}
		catch (Exception)
		{
			FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
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
	[ProducesResponseType<FileMetadataResponse>(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
	public async Task<IActionResult> GetFileMetadata(Guid CanonicalFileId, CancellationToken CancellationToken)
	{
		FileRevision? FileRevision = await Context.FileRevisions
			.AsNoTracking()
			.Where(F => F.CanonicalFileId == CanonicalFileId && F.IsCurrent == true)
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
			FileRevision.ImageWidthPixels,
			FileRevision.ImageHeightPixels,
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
/// <param name="ImageWidthPixels">The width of the image in pixels, if applicable.</param>
/// <param name="ImageHeightPixels">The height of the image in pixels, if applicable.</param>
public sealed record FileUploadResponse(Guid CanonicalFileId, string Filename, string MimeType, long FileSizeBytes, int? ImageWidthPixels, int? ImageHeightPixels);

/// <summary>
/// Response for file metadata retrieval.
/// </summary>
/// <param name="CanonicalFileId">The canonical file identifier.</param>
/// <param name="Filename">The filename.</param>
/// <param name="MimeType">The MIME type.</param>
/// <param name="FileSizeBytes">The file size in bytes.</param>
/// <param name="Type">The file type.</param>
/// <param name="Source">The file source, if any.</param>
/// <param name="ImageWidthPixels">The width of the image in pixels, if applicable.</param>
/// <param name="ImageHeightPixels">The height of the image in pixels, if applicable.</param>
/// <param name="DateCreated">The creation date.</param>
public sealed record FileMetadataResponse(
	Guid CanonicalFileId,
	string Filename,
	string MimeType,
	long FileSizeBytes,
	FileType Type,
	string? Source,
	int? ImageWidthPixels,
	int? ImageHeightPixels,
	DateTimeOffset DateCreated);
