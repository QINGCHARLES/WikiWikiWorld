using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for deleting articles.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
[Authorize] // ✅ Requires authentication
public sealed class DeleteModel(WikiWikiWorldDbContext Context, UserManager<User> UserManager, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    private readonly UserManager<User> UserManager = UserManager;

    /// <summary>
    /// Gets or sets the URL slug of the article to delete.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets any error message generated during processing.
    /// </summary>
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Handles the POST request to delete an article.
    /// </summary>
    /// <returns>A redirect to the home page or page with errors.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlSlug))
        {
            ErrorMessage = "Invalid article identifier.";
            return Page();
        }

        // ✅ Ensure the user is logged in
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Challenge();
        }

        // ✅ Fetch the article
        var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, UrlSlug, IsCurrent: true);
        ArticleRevision? CurrentArticle = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();

        if (CurrentArticle is null)
        {
            return NotFound("Article not found.");
        }

        // ✅ Get current user ID
        Guid? CurrentUserId = GetCurrentUserId();
        if (CurrentUserId is null)
        {
            return Challenge();
        }

        // ✅ Perform a soft delete (sets DateDeleted on all revisions)
        // Get all revisions
        var AllRevisionsSpec = new ArticleRevisionsByCanonicalIdSpec(CurrentArticle.CanonicalArticleId, null);
        IReadOnlyList<ArticleRevision> Revisions = await Context.ArticleRevisions.WithSpecification(AllRevisionsSpec).ToListAsync();

        foreach (var Revision in Revisions)
        {
            Revision.DateDeleted = DateTimeOffset.UtcNow;
            Revision.IsCurrent = false; // Also ensure it's not current anymore
            Context.ArticleRevisions.Update(Revision);
        }

        await Context.SaveChangesAsync();

        // ✅ Redirect after deletion
        return Redirect("/");
    }

    // ✅ Helper method to get current user ID
    private Guid? GetCurrentUserId()
    {
        string? UserIdString = UserManager.GetUserId(User);
        return Guid.TryParse(UserIdString, out Guid ParsedId) ? ParsedId : null;
    }
}
