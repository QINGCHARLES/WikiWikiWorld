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
using WikiWikiWorld.Web.Services;

namespace WikiWikiWorld.Web.Controllers.Api;
 
/// <summary>
/// API controller for managing articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
/// <param name="ArticleRevisionService">The article revision workflow service.</param>
/// <param name="FileUploadStorageService">The file upload storage service.</param>
[Route("api/article")]
[ApiController]
[Produces("application/json")]
public class ArticleApiController(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService,
    ArticleRevisionService ArticleRevisionService,
    FileUploadStorageService FileUploadStorageService) : ControllerBase
{
    /// <summary>
    /// Gets an article revision.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="Revision">The optional revision date string.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>The article revision.</returns>
    [HttpGet("{UrlSlug}")]
    [ProducesResponseType<ArticleRevision>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
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
    [Consumes("application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
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

        try
        {
            ArticleRevisionUpdateCommand Command = new(
                OriginalUrlSlug: UrlSlug,
                NewUrlSlug: UrlSlug,
                SiteId: SiteId,
                Culture: Culture,
                Title: Model.Title,
                DisplayTitle: Model.DisplayTitle,
                Type: Model.Type,
                CanonicalFileId: Model.CanonicalFileId,
                Text: Model.Text,
                RevisionReason: Model.RevisionReason,
                CreatedByUserId: ParsedUserId);

            await ArticleRevisionService.UpdateArticleAsync(Command, CancellationToken);
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
        {
            return NotFound("Article not found. To create a new article, use the POST endpoint.");
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.Conflict)
        {
            return Conflict(Ex.Message);
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
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
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
        StagedFileUpload? StagedUpload = null;
        FileRevision? CreatedFile = null;

        if (Type == ArticleType.File && File is not null)
        {
            if (!ImageValidationHelper.IsValidImageFile(File, out string ValidationError))
            {
                return BadRequest(ValidationError);
            }

            // Stage the upload under a recoverable temporary path. It is finalized only
            // after the database transaction commits (see below), so a crash never leaves
            // an orphaned final file with no committed revision.
            StagedUpload = await FileUploadStorageService.StageUploadedFileAsync(File, SiteId, Guid.NewGuid(), CancellationToken);
            CanonicalFileId = StagedUpload.CanonicalFileId;
        }

        IExecutionStrategy Strategy = Context.Database.CreateExecutionStrategy();

        try
        {
            await Strategy.ExecuteAsync(async CT =>
            {
                await using IDbContextTransaction Transaction = await Context.Database.BeginTransactionAsync(CT);

                ArticleRevisionsBySlugSpec Spec = new(UrlSlug, IsCurrent: true);
                ArticleRevision? ExistingArticle = await Context.ArticleRevisions
                    .AsNoTracking()
                    .WithSpecification(Spec)
                    .FirstOrDefaultAsync(CT);

                if (ExistingArticle is not null)
                {
                    throw new InvalidOperationException("An article with this URL slug already exists.");
                }

                if (Type == ArticleType.File && CanonicalFileId.HasValue && StagedUpload is not null)
                {
                    FileRevision NewFile = new()
                    {
                        CanonicalFileId = CanonicalFileId.Value,
                        Type = FileType.Image2D,
                        Filename = StagedUpload.OriginalFileName,
                        MimeType = StagedUpload.ContentType,
                        FileSizeBytes = StagedUpload.FileSizeBytes,
                        ImageWidthPixels = StagedUpload.ImageWidthPixels > 0 ? StagedUpload.ImageWidthPixels : null,
                        ImageHeightPixels = StagedUpload.ImageHeightPixels > 0 ? StagedUpload.ImageHeightPixels : null,
                        Source = Source,
                        RevisionReason = "Initial upload",
                        SourceAndRevisionReasonCulture = Culture,
                        CreatedByUserId = ParsedUserId,
                        DateCreated = DateTimeOffset.UtcNow,
                        IsCurrent = true
                    };

                    Context.FileRevisions.Add(NewFile);
                    CreatedFile = NewFile;
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

                await Transaction.CommitAsync(CT);
            }, CancellationToken);

            // Finalize the staged upload only after the transaction has committed. The
            // final path depends on the revision id assigned during SaveChanges, and
            // finalizing post-commit means a crash here leaves a recoverable staged file
            // (reclaimed by startup recovery) rather than an orphaned final file.
            if (StagedUpload is not null && CreatedFile is not null)
            {
                try
                {
                    FileUploadStorageService.FinalizeStagedUpload(StagedUpload, SiteId, CreatedFile.Id);
                }
                catch (Exception)
                {
                    // The article is committed and the staged file is recoverable, so this
                    // is a success: startup recovery finalizes the file. Reporting an error
                    // here would invite a duplicate-create retry.
                    return Ok("Article created successfully. The uploaded file is awaiting filesystem recovery.");
                }
            }
        }
        catch (InvalidOperationException Ex) when (Ex.Message.Contains("URL slug already exists"))
        {
            FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
            return Conflict(Ex.Message);
        }
        catch (Exception)
        {
            FileUploadStorageService.CleanupUncommittedUpload(StagedUpload);
            throw;
        }

        return Ok("Article created successfully.");
    }

    /// <summary>
    /// Gets the revision history for an article.
    /// </summary>
    /// <param name="UrlSlug">The URL slug of the article.</param>
    /// <param name="CancellationToken">The cancellation token.</param>
    /// <returns>A list of article revisions ordered by date descending.</returns>
    [HttpGet("{UrlSlug}/history")]
    [ProducesResponseType<List<ArticleRevisionHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
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

        try
        {
            await ArticleRevisionService.DeleteArticleAsync(UrlSlug, CancellationToken);
        }
        catch (ArticleRevisionWorkflowException Ex) when (Ex.Kind == ArticleRevisionWorkflowFailureKind.NotFound)
        {
            return NotFound("Article not found.");
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

