namespace WikiWikiWorld.Web.Pages;

using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

public sealed class IndexModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    public List<ArticleWithCover> ArticlesWithCovers { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken CancellationToken)
    {
        // Fetch the latest articles with publication issue infoboxes (ordered by DateCreated DESC)
        var LatestSpec = new LatestArticlesWithPublicationIssueInfoboxSpec(SiteId, Culture, 50);
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
            .Where(x => x.SiteId == SiteId && x.Culture == Culture && CoverSlugs.Contains(x.UrlSlug) && x.IsCurrent)
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

public sealed record ArticleWithCover(string Title, string UrlSlug, string ImageUrl, DateTimeOffset DateCreated);
