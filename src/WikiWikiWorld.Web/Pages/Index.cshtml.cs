namespace WikiWikiWorld.Web.Pages;

using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Page model for the home page, displaying a grid of recent articles with covers.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class IndexModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    /// <summary>
    /// Gets the list of articles with their cover image information to display.
    /// </summary>
    public List<ArticleWithCover> ArticlesWithCovers { get; private set; } = [];

    /// <summary>
    /// Handles the GET request to populate the article grid.
    /// </summary>
    /// <param name="CancellationToken">A cancellation token.</param>
    public async Task OnGetAsync(CancellationToken CancellationToken)
    {
        // Fetch the latest articles with publication issue infoboxes (ordered by DateCreated DESC)
        LatestArticlesWithPublicationIssueInfoboxSpec LatestSpec = new(50);
        IReadOnlyList<ArticleRevision> Articles = await Context.ArticleRevisions
            .WithSpecification(LatestSpec)
            .AsNoTracking()
            .ToListAsync(CancellationToken);

        // 1. Extract all cover slugs from articles
        List<string> CoverSlugs = Articles
            .Select(a => ExtractCoverImageFromInfobox(a.Text))
            .OfType<string>()
            .Distinct()
            .ToList();

        if (CoverSlugs.Count == 0)
        {
            return;
        }

        // 2. Batch-fetch all cover articles
        IReadOnlyList<ArticleRevision> CoverArticles = await Context.ArticleRevisions
            .AsNoTracking()
            .Where(x => CoverSlugs.Contains(x.UrlSlug) && x.IsCurrent)
            .ToListAsync(CancellationToken);

        // 3. Batch-fetch all files
        List<Guid> FileIds = CoverArticles
            .Where(c => c.CanonicalFileId.HasValue)
            .Select(c => c.CanonicalFileId!.Value)
            .Distinct()
            .ToList();

        IReadOnlyList<FileRevision> Files = await Context.FileRevisions
            .AsNoTracking()
            .Where(f => FileIds.Contains(f.CanonicalFileId) && f.IsCurrent == true)
            .ToListAsync(CancellationToken);

        // 4. Build lookup and assemble results
        Dictionary<string, ArticleRevision> CoverArticleLookup = CoverArticles.ToDictionary(a => a.UrlSlug, StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, FileRevision> FileLookup = Files.ToDictionary(f => f.CanonicalFileId);

        foreach (ArticleRevision Article in Articles)
        {
            string? CoverImageSlug = ExtractCoverImageFromInfobox(Article.Text);
            if (string.IsNullOrEmpty(CoverImageSlug))
            {
                continue;
            }

            if (!CoverArticleLookup.TryGetValue(CoverImageSlug, out ArticleRevision? CoverArticle) || CoverArticle.CanonicalFileId is null)
            {
                continue;
            }

            if (!FileLookup.TryGetValue(CoverArticle.CanonicalFileId.Value, out FileRevision? File))
            {
                continue;
            }

            string ImageUrl = $"/sitefiles/{SiteId}/images/{File.CanonicalFileId}{Path.GetExtension(File.Filename)}";
            ArticlesWithCovers.Add(new ArticleWithCover(
                Article.Title,
                Article.UrlSlug,
                ImageUrl,
                Article.DateCreated
            ));
        }
    }

    /// <summary>
    /// Extracts the cover image slug from a PublicationIssueInfobox in the article text.
    /// </summary>
    /// <param name="Text">The article text containing the infobox.</param>
    /// <returns>The URL slug for the cover image, or null if not found.</returns>
    private static string? ExtractCoverImageFromInfobox(string Text)
    {
        // Find the start of the infobox
        int Start = Text.IndexOf("{{PublicationIssueInfobox", StringComparison.OrdinalIgnoreCase);
        if (Start == -1)
        {
            return null;
        }

        // Find the end of the infobox
        int End = Text.IndexOf("}}", Start, StringComparison.Ordinal);
        if (End == -1)
        {
            return null;
        }

        // Extract the infobox content and remove markers
        string InfoboxContent = Text[Start..(End + 2)]
            .Replace("{{PublicationIssueInfobox", "", StringComparison.OrdinalIgnoreCase)
            .Replace("}}", "", StringComparison.Ordinal);

        const string AttributeSeparator = "|#|";
        string[] Tokens = InfoboxContent.Split(AttributeSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string Token in Tokens)
        {
            string Trimmed = Token.Trim();
            if (string.IsNullOrEmpty(Trimmed))
            {
                continue;
            }

            int EqPos = Trimmed.IndexOf('=');
            if (EqPos <= 0)
            {
                continue;
            }

            string Key = Trimmed[..EqPos].Trim();
            if (Key.Equals("CoverImage", StringComparison.OrdinalIgnoreCase))
            {
                string Value = Trimmed[(EqPos + 1)..].Trim();
                if (Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    Value = Value["file:".Length..];
                }
                return SlugHelper.GenerateSlug(Value);
            }
        }

        return null;
    }
}

/// <summary>
/// Represents an article summary with its cover image for display on the index page.
/// </summary>
/// <param name="Title">The title of the article.</param>
/// <param name="UrlSlug">The URL slug of the article.</param>
/// <param name="ImageUrl">The URL of the cover image.</param>
/// <param name="DateCreated">The creation date of the article.</param>
public sealed record ArticleWithCover(string Title, string UrlSlug, string ImageUrl, DateTimeOffset DateCreated);
