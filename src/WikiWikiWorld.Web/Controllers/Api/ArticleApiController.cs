using WikiWikiWorld.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

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
    /// <returns>The article revision.</returns>
    [HttpGet("{UrlSlug}")]
    public async Task<IActionResult> GetArticleRevision(string UrlSlug, [FromQuery] string? Revision)
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
            ArticleRevisionBySlugAndDateSpec SpecificSpec = new(SiteId, Culture, UrlSlug, RevisionDate);
            SpecificRevision = await Context.ArticleRevisions.WithSpecification(SpecificSpec).FirstOrDefaultAsync();
        }

        // Return the current revision
        ArticleRevisionsBySlugSpec CurrentSpec = new(SiteId, Culture, UrlSlug, IsCurrent: true);
        CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync();

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
    /// <returns>The result of the update operation.</returns>
    [HttpPut("{UrlSlug}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UpdateArticleRevision(string UrlSlug, [FromBody] UpdateArticleRevisionModel Model)
    {
        if (string.IsNullOrWhiteSpace(UrlSlug) || Model == null)
        {
            return BadRequest("Invalid parameters.");
        }

        (int SiteId, string Culture) = SiteResolverService.ResolveSiteAndCulture();

        string? UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        if (UserId == null)
        {
            return Unauthorized("User ID not found in token.");
        }

        // Find current revision and set IsCurrent = false
        ArticleRevisionsBySlugSpec CurrentSpec = new(SiteId, Culture, UrlSlug, IsCurrent: true);
        ArticleRevision? CurrentRevision = await Context.ArticleRevisions.WithSpecification(CurrentSpec).FirstOrDefaultAsync();

        if (CurrentRevision is not null)
        {
            CurrentRevision.IsCurrent = false;
            Context.ArticleRevisions.Update(CurrentRevision);
            await Context.SaveChangesAsync();
        }

        ArticleRevision ArticleRevision = new()
        {
            CanonicalArticleId = Model.CanonicalArticleId ?? Guid.NewGuid(),
            SiteId = SiteId,
            Culture = Culture,
            Title = Model.Title,
            DisplayTitle = Model.DisplayTitle,
            UrlSlug = UrlSlug,
            Type = Model.Type,
            CanonicalFileId = Model.CanonicalFileId,
            Text = Model.Text,
            RevisionReason = Model.RevisionReason,
            CreatedByUserId = Guid.Parse(UserId),
            DateCreated = DateTimeOffset.UtcNow,
            IsCurrent = true
        };

        Context.ArticleRevisions.Add(ArticleRevision);
        await Context.SaveChangesAsync();

        return Ok("Article revision updated successfully.");
    }
}

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
