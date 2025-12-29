using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Pages.Profile;

public class ViewModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    [BindProperty(SupportsGet = true)]
    public string Username { get; set; } = string.Empty;

    public string? ProfilePicPath { get; set; }
    public DateTimeOffset DateJoined { get; set; }
    public bool HasHomePage { get; set; }
    public bool IsViewingOwnProfile { get; set; }
    public bool IsAuthenticated { get; set; }

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
        var Spec = new ArticleRevisionsBySlugSpec(SiteId, Culture, HomePageSlug, IsCurrent: true);
        var Article = await Context.ArticleRevisions.WithSpecification(Spec).FirstOrDefaultAsync();
        
        HasHomePage = Article is not null;

        // Check if viewing own profile
        IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
        User? CurrentUser = IsAuthenticated ? await UserManager.GetUserAsync(User) : null;
        IsViewingOwnProfile = CurrentUser?.Id == TargetUser.Id;

        return Page();
    }
}
