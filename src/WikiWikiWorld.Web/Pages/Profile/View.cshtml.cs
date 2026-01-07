using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// Page model for viewing a user's profile.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="UserManager">The user manager.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public class ViewModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the username of the profile to view.
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
    /// Gets or sets a value indicating whether the user has a home page article.
    /// </summary>
    public bool HasHomePage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current user is viewing their own profile.
    /// </summary>
    public bool IsViewingOwnProfile { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current visitor is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Handles the GET request to view the user profile.
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
        
        if (!string.IsNullOrWhiteSpace(TargetUser.ProfilePicGuid))
        {
            ProfilePicPath = Url.Content($"~/sitefiles/{SiteId}/profilepics/{TargetUser.ProfilePicGuid}.png");
        }

        // Check if user has a home page article
        string HomePageSlug = $"@{Username}";
        ArticleRevisionsBySlugSpec Spec = new(HomePageSlug, IsCurrent: true);
        ArticleRevision? Article = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();
        
        HasHomePage = Article is not null;

        // Check if viewing own profile
        IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
        User? CurrentUser = IsAuthenticated ? await UserManager.GetUserAsync(User) : null;
        IsViewingOwnProfile = CurrentUser?.Id == TargetUser.Id;

        return Page();
    }
}
