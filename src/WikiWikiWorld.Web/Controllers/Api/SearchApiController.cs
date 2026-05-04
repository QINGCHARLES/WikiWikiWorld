using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data;
using WikiWikiWorld.Data.Models;

namespace WikiWikiWorld.Web.Controllers.Api;

/// <summary>
/// API controller for searching articles.
/// </summary>
/// <param name="Context">The database context.</param>
[Route("api/search")]
[ApiController]
[Produces("application/json")]
public sealed class SearchApiController(WikiWikiWorldDbContext Context) : ControllerBase
{
	private const int SnippetPadding = 80;

	/// <summary>
	/// Searches articles by title and content, returning title matches first.
	/// </summary>
	/// <param name="Q">The search query.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A list of search results.</returns>
	[HttpGet]
	[ProducesResponseType<List<SearchResultDto>>(StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> Search([FromQuery] string? Q, CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(Q))
		{
			return BadRequest("Query parameter 'q' is required.");
		}

		string LikePattern = $"%{Q}%";

		// Title matches first (prioritized) - case-insensitive using LIKE
		List<ArticleRevision> TitleMatches = await Context.ArticleRevisions
			.Where(P => P.IsCurrent && EF.Functions.Like(P.Title, LikePattern))
			.AsNoTracking()
			.ToListAsync(CancellationToken);

		// Content matches (excluding title matches) - case-insensitive using LIKE
		List<ArticleRevision> ContentMatches = await Context.ArticleRevisions
			.Where(P => P.IsCurrent && !EF.Functions.Like(P.Title, LikePattern) && EF.Functions.Like(P.Text, LikePattern))
			.AsNoTracking()
			.ToListAsync(CancellationToken);

		List<SearchResultDto> Results = [];

		foreach (ArticleRevision Article in TitleMatches)
		{
			Results.Add(new SearchResultDto(
				Article.Title,
				Article.UrlSlug,
				IsTitleMatch: true,
				Snippet: null));
		}

		foreach (ArticleRevision Article in ContentMatches)
		{
			string Snippet = ExtractSnippet(Article.Text, Q);
			Results.Add(new SearchResultDto(
				Article.Title,
				Article.UrlSlug,
				IsTitleMatch: false,
				Snippet: Snippet));
		}

		return Ok(Results);
	}

	/// <summary>
	/// Extracts a snippet from the content around the search term.
	/// </summary>
	/// <param name="Content">The full content to extract from.</param>
	/// <param name="Query">The search query to find.</param>
	/// <returns>A snippet of text surrounding the search term, or an empty string if not found.</returns>
	private static string ExtractSnippet(string Content, string Query)
	{
		int Index = Content.IndexOf(Query, StringComparison.OrdinalIgnoreCase);
		if (Index < 0)
		{
			return string.Empty;
		}

		int Start = Math.Max(0, Index - SnippetPadding);
		int End = Math.Min(Content.Length, Index + Query.Length + SnippetPadding);

		string Snippet = Content[Start..End];

		if (Start > 0) Snippet = "..." + Snippet;
		if (End < Content.Length) Snippet += "...";

		return Snippet;
	}
}

/// <summary>
/// Represents a search result returned by the API.
/// </summary>
/// <param name="Title">The article title.</param>
/// <param name="UrlSlug">The URL slug for linking.</param>
/// <param name="IsTitleMatch">Whether this matched by title.</param>
/// <param name="Snippet">Content snippet for content matches.</param>
public sealed record SearchResultDto(string Title, string UrlSlug, bool IsTitleMatch, string? Snippet);
