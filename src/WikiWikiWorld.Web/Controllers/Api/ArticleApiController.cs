using WikiWikiWorld.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Controllers.Api;
 
/// <summary>
/// API controller for managing articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="FileStorageOptions">The file storage options.</param>
[Route("api/article")]
[ApiController]
public class ArticleApiController(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService,
    IOptions<FileStorageOptions> FileStorageOptions) : ControllerBase
{
    /// <summary>
    /// Gets an article revision.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="Revision">The optional revision date string.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The article revision.</returns>
    [HttpGet("{UrlSlug}")]
    public async Task<IActionResult> GetArticleRevision(string UrlSlug, [FromQuery] string? Revision, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("Invalid parameters.");
        }

        (int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

        ArticleRevision? SpecificRevision = null;
        ArticleRevision? CurrentRevision;

        // If a revision is specified, parse the date and retrieve the specific revision
        if (!string.IsNullOrWhiteSpace(Revision) && RevisionDateParser.TryParseRevisionDate(Revision, out DateTimeOffset RevisionDate))
        {
            ArticleRevisionBySlugAndDateSpec SpecificSpec = new(UrlSlug, RevisionDate);
            SpecificRevision = await Context.ArticleRevisions.WithSpecification(SpecificSpec).FirstOrDefaultAsync(CancellationToken);
        }

        // Return the current revision
        ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
        CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CancellationToken);

        if (SpecificRevision is not null)
        {
            return Ok(SpecificRevision);
        }

        if (CurrentRevision is null)
        {
            return NotFound("Article revision not found.");
        }

        return Ok(CurrentRevision);
    }

    /// <summary>
    /// Updates an article revision.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="Model">The update model.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The result of the update operation.</returns>
    [HttpPut("{UrlSlug}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UpdateArticleRevision(string UrlSlug, [FromBody] UpdateArticleRevisionModel Model, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug) || Model == null)
        {
            return BadRequest("Invalid parameters.");
        }

        (int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

        string? UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(UserId, out Guid ParsedUserId))
        {
            return Unauthorized("User ID not found in token.");
        }

        IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

        try
        {
            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

                ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
                ArticleRevision? CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CT);

                if (CurrentRevision is null)
                {
                    throw new InvalidOperationException("Article not found.");
                }

                CurrentRevision.IsCurrent = false;
                Context.ArticleRevisions.Update(CurrentRevision);

                Guid CanonicalArticleId = CurrentRevision.CanonicalArticleId;

                ArticleRevision ArticleRevision = new()
                {
                    CanonicalArticleId = CanonicalArticleId,
                    SiteId = SiteId,
                    Culture = Culture,
                    Title = Model.Title,
                    DisplayTitle = Model.DisplayTitle,
                    UrlSlug = UrlSlug,
                    Type = Model.Type,
                    CanonicalFileId = Model.CanonicalFileId,
                    Text = Model.Text,
                    RevisionReason = Model.RevisionReason,
                    CreatedByUserId = ParsedUserId,
                    DateCreated = DateTimeOffset.UtcNow,
                    IsCurrent = true
                };

                Context.ArticleRevisions.Add(ArticleRevision);
                await Context.SaveChangesAsync(CT);
                await Transaction.CommitAsync(CT);
            }, CancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message == "Article not found.")
        {
            return NotFound("Article not found. To create a new article, use the POST endpoint.");
        }

        return Ok("Article revision updated successfully.");
    }

    /// <summary>
    /// Creates a new article revision. If creating a file article, accepts an optional uploaded file.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="Title">The title of the article.</param>
    /// <param name="DisplayTitle">The optional display title.</param>
    /// <param name="Type">The type of the article.</param>
    /// <param name="Text">The article text.</param>
    /// <param name="RevisionReason">The revision reason.</param>
    /// <param name="Source">The optional image source.</param>
    /// <param name="File">The optional image file.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    [HttpPost("{UrlSlug}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> CreateArticleRevision(
        string UrlSlug,
        [FromForm] string Title,
        [FromForm] string? DisplayTitle,
        [FromForm] ArticleType Type,
        [FromForm] string Text,
        [FromForm] string RevisionReason,
        [FromForm] string? Source,
        [FromForm] IFormFile? File,
        CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug) || string.IsNullOrWhiteSpace(Title) || string.IsNullOrWhiteSpace(Text) || string.IsNullOrWhiteSpace(RevisionReason))
        {
            return BadRequest("UrlSlug, Title, Text, and RevisionReason are required.");
        }

        if (Type == ArticleType.File && (File is null || File.Length == 0))
        {
            return BadRequest("A file is required when creating a File-type article.");
        }
        
        if (Type != ArticleType.File && File is not null && File.Length > 0)
        {
            return BadRequest("Files can only be uploaded for File-type articles.");
        }

        string? UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(UserId, out Guid ParsedUserId))
        {
            return Unauthorized("User ID not found in token.");
        }

        (int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

        Guid CanonicalArticleId = Guid.NewGuid();
        Guid? CanonicalFileId = null;
        string? OriginalFileName = null;
        string? UploadedContentType = null;
        long UploadedFileSizeBytes = 0;
        string? TemporaryFilePath = null;
        string? FinalFilePath = null;

        if (Type == ArticleType.File && File is not null)
        {
            if (!ImageValidationHelper.IsValidImageFile(File, out string ValidationError))
            {
                return BadRequest(ValidationError);
            }

            CanonicalFileId = Guid.NewGuid();
            OriginalFileName = Path.GetFileName(File.FileName);
            string FileExtension = Path.GetExtension(OriginalFileName);
            string UniqueFileName = $"{CanonicalFileId}{FileExtension}";

            string SiteFilesDirectory = Path.Combine(
                FileStorageOptions.Value.SiteFilesPath,
                SiteId.ToString(),
                "images");
            Directory.CreateDirectory(SiteFilesDirectory);

            FinalFilePath = Path.Combine(SiteFilesDirectory, UniqueFileName);
            TemporaryFilePath = Path.Combine(SiteFilesDirectory, $"{UniqueFileName}.{Guid.NewGuid():N}.tmp");

            // Stage the file
            await using FileStream FileStream = new(TemporaryFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await File.CopyToAsync(FileStream, CancellationToken);
            UploadedContentType = File.ContentType;
            UploadedFileSizeBytes = File.Length;
        }

        IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

        try
        {
            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

                ArticleRevisionsBySlugSpec Spec = new(UrlSlug, IsCurrent: true);
                ArticleRevision? ExistingArticle = await Context.ArticleRevisions
                    .AsNoTracking()
                    .WithSpecification(Spec)
                    .FirstOrDefaultAsync(CT);

                if (ExistingArticle is not null)
                {
                    throw new InvalidOperationException("An article with this URL slug already exists.");
                }

                if (Type == ArticleType.File && CanonicalFileId.HasValue && OriginalFileName is not null && UploadedContentType is not null)
                {
                    FileRevision NewFile = new()
                    {
                        CanonicalFileId = CanonicalFileId.Value,
                        Type = FileType.Image2D,
                        Filename = OriginalFileName,
                        MimeType = UploadedContentType,
                        FileSizeBytes = UploadedFileSizeBytes,
                        Source = Source,
                        RevisionReason = "Initial upload",
                        SourceAndRevisionReasonCulture = Culture,
                        CreatedByUserId = ParsedUserId,
                        DateCreated = DateTimeOffset.UtcNow,
                        IsCurrent = true
                    };

                    Context.FileRevisions.Add(NewFile);
                }

                ArticleRevision NewArticle = new()
                {
                    CanonicalArticleId = CanonicalArticleId,
                    SiteId = SiteId,
                    Culture = Culture,
                    Title = Title,
                    DisplayTitle = DisplayTitle,
                    UrlSlug = UrlSlug,
                    Type = Type,
                    CanonicalFileId = CanonicalFileId,
                    Text = Text,
                    RevisionReason = RevisionReason,
                    CreatedByUserId = ParsedUserId,
                    DateCreated = DateTimeOffset.UtcNow,
                    IsCurrent = true
                };

                Context.ArticleRevisions.Add(NewArticle);
                await Context.SaveChangesAsync(CT);

                if (TemporaryFilePath is not null && FinalFilePath is not null)
                {
                    System.IO.File.Move(TemporaryFilePath, FinalFilePath, overwrite: false);
                }

                await Transaction.CommitAsync(CT);
            }, CancellationToken);
        }
        catch (InvalidOperationException Ex) when (Ex.Message.Contains("URL slug already exists"))
        {
            CleanupStagedUpload(TemporaryFilePath, FinalFilePath);
            return Conflict(Ex.Message);
        }
        catch (Exception)
        {
            CleanupStagedUpload(TemporaryFilePath, FinalFilePath);
            throw;
        }

        return Ok("Article created successfully.");
    }

    private static void CleanupStagedUpload(string? TemporaryFilePath, string? FinalFilePath)
    {
        if (!string.IsNullOrWhiteSpace(TemporaryFilePath) && System.IO.File.Exists(TemporaryFilePath))
        {
            System.IO.File.Delete(TemporaryFilePath);
        }

        if (!string.IsNullOrWhiteSpace(FinalFilePath) && System.IO.File.Exists(FinalFilePath))
        {
            System.IO.File.Delete(FinalFilePath);
        }
    }

    /// <summary>
    /// Gets the revision history for an article.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A list of article revisions ordered by date descending.</returns>
    [HttpGet("{UrlSlug}/history")]
    public async Task<IActionResult> GetArticleHistory(string UrlSlug, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("URL slug is required.");
        }

        // First find the current revision to get the CanonicalArticleId
        ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
        ArticleRevision? CurrentRevision = await Context.ArticleRevisions
            .AsNoTracking()
            .WithSpecification(CurrentSpec)
            .FirstOrDefaultAsync(CancellationToken);

        if (CurrentRevision is null)
        {
            return NotFound("Article not found.");
        }

        // Get all revisions for this article
        List<ArticleRevisionHistoryDto> History = await Context.ArticleRevisions
            .Where(R => R.CanonicalArticleId == CurrentRevision.CanonicalArticleId)
            .OrderByDescending(R => R.DateCreated)
            .AsNoTracking()
            .Select(R => new ArticleRevisionHistoryDto(
                R.Id,
                R.Title,
                R.UrlSlug,
                R.IsCurrent,
                R.RevisionReason,
                R.CreatedByUserId,
                R.DateCreated))
            .ToListAsync(CancellationToken);

        return Ok(History);
    }

    /// <summary>
    /// Soft-deletes the current revision of an article.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A success message or error.</returns>
    [HttpDelete("{UrlSlug}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> DeleteArticle(string UrlSlug, CancellationToken CancellationToken)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug))
        {
            return BadRequest("URL slug is required.");
        }

        string? UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(UserId, out Guid ParsedUserId))
        {
            return Unauthorized("User ID not found in token.");
        }

        ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
        ArticleRevision? CurrentRevision = await Context.ArticleRevisions
            .WithSpecification(CurrentSpec)
            .FirstOrDefaultAsync(CancellationToken);

        if (CurrentRevision is null)
        {
            return NotFound("Article not found.");
        }

        CurrentRevision.IsCurrent = false;
        CurrentRevision.DateDeleted = DateTimeOffset.UtcNow;
        Context.ArticleRevisions.Update(CurrentRevision);

        using (WriteDurabilityScope.High())
        {
            await Context.SaveChangesAsync(CancellationToken);
        }

        return Ok("Article deleted successfully.");
    }
}

/// <summary>
/// DTO for article revision history entries.
/// </summary>
/// <param name="Id">The revision ID.</param>
/// <param name="Title">The article title.</param>
/// <param name="UrlSlug">The URL slug.</param>
/// <param name="IsCurrent">Whether this is the current revision.</param>
/// <param name="RevisionReason">The reason for this revision.</param>
/// <param name="CreatedByUserId">The user who created this revision.</param>
/// <param name="DateCreated">When this revision was created.</param>
public sealed record ArticleRevisionHistoryDto(
    int Id,
    string Title,
    string UrlSlug,
    bool IsCurrent,
    string RevisionReason,
    Guid CreatedByUserId,
    DateTimeOffset DateCreated);

/// <summary>
/// Model for updating an article revision.
/// </summary>
public class UpdateArticleRevisionModel
{
    /// <summary>
    /// Gets or sets the canonical article identifier.
    /// </summary>
    public Guid? CanonicalArticleId { get; set; }

    /// <summary>
    /// Gets or sets the title of the article.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Gets or sets the display title of the article.
    /// </summary>
    public string? DisplayTitle { get; set; }

    /// <summary>
    /// Gets or sets the type of the article.
    /// </summary>
    public ArticleType Type { get; set; }

    /// <summary>
    /// Gets or sets the canonical file identifier.
    /// </summary>
    public Guid? CanonicalFileId { get; set; }

    /// <summary>
    /// Gets or sets the text content of the article.
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Gets or sets the reason for the revision.
    /// </summary>
    public required string RevisionReason { get; set; }
}

