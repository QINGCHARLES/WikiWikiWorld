namespace WikiWikiWorld.Web.Pages;

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;
using WikiWikiWorld.Web.Infrastructure;

/// <summary>
/// Page model for the search results page.
/// </summary>
/// <param name="Context">The database context.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class SearchModel(
    WikiWikiWorldDbContext Context,
    SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
    private const int SnippetPadding = 80;

    /// <summary>
    /// Gets or sets the search query.
    /// </summary>
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>
    /// Gets the list of search results.
    /// </summary>
    public List<SearchResult> SearchResults { get; private set; } = [];

    /// <summary>
    /// Handles the GET request to perform the search.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Q))
        {
            return;
        }

        string likePattern = $"%{Q}%";

        // Title matches first (prioritized) - case-insensitive using LIKE
        List<ArticleRevision> titleMatches = await Context.ArticleRevisions
            .Where(p => p.IsCurrent && EF.Functions.Like(p.Title, likePattern))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Content matches (excluding title matches) - case-insensitive using LIKE
        List<ArticleRevision> contentMatches = await Context.ArticleRevisions
            .Where(p => p.IsCurrent && !EF.Functions.Like(p.Title, likePattern) && EF.Functions.Like(p.Text, likePattern))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Build search results with snippets
        foreach (ArticleRevision article in titleMatches)
        {
            SearchResults.Add(new SearchResult(
                article.Title,
                article.UrlSlug,
                IsTitleMatch: true,
                Snippet: null));
        }

        foreach (ArticleRevision article in contentMatches)
        {
            string snippet = ExtractSnippet(article.Text, Q);
            SearchResults.Add(new SearchResult(
                article.Title,
                article.UrlSlug,
                IsTitleMatch: false,
                Snippet: snippet));
        }
    }

    /// <summary>
    /// Extracts a snippet from the content around the search term.
    /// </summary>
    /// <param name="content">The full content to extract from.</param>
    /// <param name="query">The search query to find.</param>
    /// <returns>A snippet of text surrounding the search term, or an empty string if not found.</returns>
    private static string ExtractSnippet(string content, string query)
    {
        int index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        int start = Math.Max(0, index - SnippetPadding);
        int end = Math.Min(content.Length, index + query.Length + SnippetPadding);

        string snippet = content[start..end];

        // Add ellipsis if we're not at the boundaries
        if (start > 0) snippet = "..." + snippet;
        if (end < content.Length) snippet += "...";

        return snippet;
    }

    /// <summary>
    /// Highlights the search term in text by wrapping it with mark tags.
    /// </summary>
    public string HighlightTerm(string text)
    {
        if (string.IsNullOrWhiteSpace(Q) || string.IsNullOrWhiteSpace(text))
        {
            return System.Net.WebUtility.HtmlEncode(text);
        }

        // Escape HTML first, then apply highlighting
        string escapedText = System.Net.WebUtility.HtmlEncode(text);
        string escapedQuery = System.Net.WebUtility.HtmlEncode(Q);

        // Case-insensitive replacement with preserved case
        return Regex.Replace(
            escapedText,
            Regex.Escape(escapedQuery),
            match => $"<mark>{match.Value}</mark>",
            RegexOptions.IgnoreCase);
    }
}

/// <summary>
/// Represents a search result with display information.
/// </summary>
/// <param name="Title">The article title.</param>
/// <param name="UrlSlug">The URL slug for linking.</param>
/// <param name="IsTitleMatch">Whether this matched by title.</param>
/// <param name="Snippet">Content snippet for content matches.</param>
public sealed record SearchResult(string Title, string UrlSlug, bool IsTitleMatch, string? Snippet);
