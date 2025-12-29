using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;

namespace WikiWikiWorld.Web.Pages.Profile;

public class ContributionsModel(
    WikiWikiWorldDbContext Context,
    UserManager<User> UserManager,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    [BindProperty(SupportsGet = true)]
    public string Username { get; set; } = string.Empty;

    public string? ProfilePicPath { get; set; }
    public DateTimeOffset DateJoined { get; set; }
    public IReadOnlyList<ArticleRevision> Contributions { get; set; } = [];
    public bool IsViewingOwnProfile { get; set; }

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

        // Check if viewing own profile
        User? CurrentUser = await UserManager.GetUserAsync(User);
        IsViewingOwnProfile = CurrentUser?.Id == TargetUser.Id;

        ArticleRevisionsByUserIdSpec Spec = new(TargetUser.Id);
        Contributions = await Context.ArticleRevisions.WithSpecification(Spec).ToListAsync();

        return Page();
    }
}

