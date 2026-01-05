using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for viewing a talk subject (discussion thread).
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public class TalkSubjectModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets or sets the URL slug of the talk subject.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string UrlSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the talk subject entity.
    /// </summary>
    public ArticleTalkSubject? TalkSubject { get; set; }

    /// <summary>
    /// Gets or sets the dictionary of authors who posted in the thread.
    /// </summary>
    public Dictionary<Guid, User> PostAuthors { get; set; } = [];

    /// <summary>
    /// Gets or sets the name of the creator of the thread.
    /// </summary>
    public string? CreatorName { get; set; }

    /// <summary>
    /// Gets or sets the name of the recipient (article owner).
    /// </summary>
    public string? RecipientName { get; set; }

    /// <summary>
    /// Handles the GET request to view the talk thread.
    /// </summary>
    /// <returns>The page or NotFound.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlSlug))
        {
            return NotFound();
        }

        ArticleTalkSubjectBySlugSpec Spec = new(SiteId, UrlSlug);
        TalkSubject = await Context.ArticleTalkSubjects.WithSpecification(Spec).FirstOrDefaultAsync();

        if (TalkSubject is null)
        {
            return NotFound();
        }

        // Fetch authors for the posts (including key participant: the creator)
        List<Guid> AuthorIds = TalkSubject.ArticleTalkSubjectPosts
            .Select(p => p.CreatedByUserId)
            .Distinct()
            // Make sure to include the creator of the subject if not already in posts (rare but possible)
            .Append(TalkSubject.CreatedByUserId)
            .Distinct()
            .ToList();

        if (AuthorIds.Count > 0)
        {
            UserByIdsSpec UserSpec = new(AuthorIds);
            IReadOnlyList<User> Users = await Context.Users.WithSpecification(UserSpec).ToListAsync();
            PostAuthors = Users.ToDictionary(u => u.Id, u => u);
        }

        // Set Creator Name
        if (PostAuthors.TryGetValue(TalkSubject.CreatedByUserId, out var CreatorUser))
        {
            CreatorName = CreatorUser.UserName;
        }

        // Fetch the article to know who this conversation is with (The Recipient)
        // The CanonicalArticleId points to the User Article of the recipient
        ArticleRevisionsByCanonicalIdsSpec ArticleSpec = new(SiteId, Culture, [TalkSubject.CanonicalArticleId], IsCurrent: true);
        ArticleRevision? Article = await Context.ArticleRevisions.WithSpecification(ArticleSpec).FirstOrDefaultAsync();
        
        if (Article is not null)
        {
             RecipientName = Article.Title.TrimStart('@');
        }

        return Page();
    }
}
