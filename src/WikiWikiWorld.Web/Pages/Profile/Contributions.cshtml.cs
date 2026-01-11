using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// Page model for displaying a user's contributions (edits).
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public class ContributionsModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the username to view contributions for.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the user's profile picture.
    /// </summary>
    public string? ProfilePicPath { get; set; }

    /// <summary>
    /// Gets or sets the date the user joined.
    /// </summary>
    public DateTimeOffset DateJoined { get; set; }

    /// <summary>
    /// Gets or sets the list of article revisions contributed by the user.
    /// </summary>
    public IReadOnlyList<ArticleRevision> Contributions { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the current user is viewing their own profile.
    /// </summary>
    public bool IsViewingOwnProfile { get; set; }

    /// <summary>
    /// Handles the GET request to view user contributions.
    /// </summary>
    /// <returns>The page or NotFound.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            return NotFound();
        }

        User? TargetUser = await UserManager.FindByNameAsync(Username);
        if (TargetUser is null)
        {
            return NotFound();
        }

        DateJoined = TargetUser.DateCreated;
        
        if (TargetUser.ProfilePicGuid.HasValue)
        {
            ProfilePicPath = Url.Content($"~/sitefiles/{SiteId}/profilepics/{TargetUser.ProfilePicGuid}.png");
        }

        // Check if viewing own profile
        User? CurrentUser = await UserManager.GetUserAsync(User);
        IsViewingOwnProfile = CurrentUser?.Id == TargetUser.Id;

        ArticleRevisionsByUserIdSpec Spec = new(TargetUser.Id);
        Contributions = await Context.ArticleRevisions.WithSpecification(Spec).ToListAsync();

        return Page();
    }
}

