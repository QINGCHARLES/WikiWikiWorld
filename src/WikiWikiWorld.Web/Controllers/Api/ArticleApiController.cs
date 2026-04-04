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

namespace WikiWikiWorld.Web.Controllers.Api;
 
/// <summary>
/// API controller for managing articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
[Route("api/article")]
[ApiController]
public class ArticleApiController(WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService) : ControllerBase
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

        await Strategy.ExecuteAsync(async CT =>
        {
            await using IDbContextTransaction Transaction = await Context.Database.BeginImmediateTransactionAsync(CT);

            ArticleRevisionsBySlugSpec CurrentSpec = new(UrlSlug, IsCurrent: true);
            ArticleRevision? CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync(CT);

            if (CurrentRevision is not null)
            {
                CurrentRevision.IsCurrent = false;
                Context.ArticleRevisions.Update(CurrentRevision);
            }

            Guid CanonicalArticleId = CurrentRevision?.CanonicalArticleId ?? Model.CanonicalArticleId ?? Guid.NewGuid();

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

        return Ok("Article revision updated successfully.");
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

