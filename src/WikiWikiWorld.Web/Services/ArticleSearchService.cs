using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Services;

/// <summary>
/// Searches current article revisions for the header typeahead and the full <c>/Search</c> page. Title matches are
/// ranked exact, prefix, word start, then substring; body matches follow. Queries run through the context's site,
/// culture and soft-delete filters and leave out auto-created user pages (<see cref="ArticleType.User"/>).
/// </summary>
/// <remarks>
/// Matching uses SQLite <c>LIKE</c>, which is case-insensitive for ASCII letters only. User-typed <c>%</c>, <c>_</c>
/// and <c>\</c> are escaped so they match literally.
/// </remarks>
/// <param name="Context">The database context for the current request.</param>
public sealed partial class ArticleSearchService(WikiWikiWorldDbContext Context)
{
	/// <summary>
	/// The most rows the typeahead shows.
	/// </summary>
	public const int MaxSuggestions = 8;

	/// <summary>
	/// The shortest trimmed query the typeahead answers.
	/// </summary>
	public const int MinSuggestionQueryLength = 2;

	/// <summary>
	/// The shortest query for which the typeahead also scans article bodies; two letters match nearly every body.
	/// </summary>
	private const int MinBodySuggestionQueryLength = 3;

	/// <summary>
	/// Queries are truncated to this many characters before matching.
	/// </summary>
	private const int MaxQueryLength = 100;

	/// <summary>
	/// The escape character passed to every <c>LIKE</c> pattern.
	/// </summary>
	private const string LikeEscape = "\\";

	/// <summary>
	/// Returns up to <see cref="MaxSuggestions"/> ranked matches for the header typeahead: titles first, then (for
	/// queries of three or more characters) body matches whose visible prose contains the query.
	/// </summary>
	/// <param name="Query">The raw query typed by the reader.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The ranked suggestions; empty when the trimmed query is shorter than <see cref="MinSuggestionQueryLength"/>.</returns>
	public async Task<IReadOnlyList<ArticleSearchResult>> SuggestAsync(string? Query, CancellationToken CancellationToken)
	{
		string Trimmed = NormalizeQuery(Query);
		if (Trimmed.Length < MinSuggestionQueryLength)
		{
			return [];
		}

		List<ArticleSearchResult> Results = await QueryTitlesAsync(Trimmed, MaxSuggestions, CancellationToken);
		int RemainingSlots = MaxSuggestions - Results.Count;
		if (RemainingSlots == 0 || Trimmed.Length < MinBodySuggestionQueryLength)
		{
			return Results;
		}

		// Over-fetch because a body hit inside directive markup (a slug, a property) has no readable snippet and is dropped.
		List<BodyCandidate> Candidates = await QueryBodiesAsync(Trimmed, RemainingSlots * 3, CancellationToken);
		foreach (BodyCandidate Candidate in Candidates)
		{
			string? Snippet = BuildSnippet(Candidate.Text, Trimmed, LeadingChars: 24, TrailingChars: 90);
			if (Snippet is null)
			{
				continue;
			}

			Results.Add(new ArticleSearchResult(Candidate.Title, Candidate.UrlSlug, Candidate.Type, IsTitleMatch: false, Snippet));
			if (Results.Count == MaxSuggestions)
			{
				break;
			}
		}

		return Results;
	}

	/// <summary>
	/// Returns every ranked title match followed by every body match for the full search page.
	/// </summary>
	/// <param name="Query">The raw query submitted by the reader.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>All matches; body matches carry a plain-text snippet when the query appears in visible prose.</returns>
	public async Task<IReadOnlyList<ArticleSearchResult>> SearchAsync(string? Query, CancellationToken CancellationToken)
	{
		string Trimmed = NormalizeQuery(Query);
		if (Trimmed.Length == 0)
		{
			return [];
		}

		List<ArticleSearchResult> Results = await QueryTitlesAsync(Trimmed, Limit: null, CancellationToken);
		List<BodyCandidate> Candidates = await QueryBodiesAsync(Trimmed, Limit: null, CancellationToken);
		foreach (BodyCandidate Candidate in Candidates)
		{
			string? Snippet = BuildSnippet(Candidate.Text, Trimmed, LeadingChars: 80, TrailingChars: 80);
			Results.Add(new ArticleSearchResult(Candidate.Title, Candidate.UrlSlug, Candidate.Type, IsTitleMatch: false, Snippet));
		}

		return Results;
	}

	/// <summary>
	/// HTML-encodes text and wraps each case-insensitive occurrence of the query in <c>&lt;mark&gt;</c>.
	/// </summary>
	/// <param name="Text">The plain text to display.</param>
	/// <param name="Query">The reader's query; blank queries produce encoded text without marks.</param>
	/// <returns>Encoded HTML safe for <c>Html.Raw</c>.</returns>
	public static string Highlight(string? Text, string? Query)
	{
		string Encoded = WebUtility.HtmlEncode(Text ?? string.Empty);
		string Trimmed = NormalizeQuery(Query);
		if (Trimmed.Length == 0 || Encoded.Length == 0)
		{
			return Encoded;
		}

		return Regex.Replace(
			Encoded,
			Regex.Escape(WebUtility.HtmlEncode(Trimmed)),
			Match => $"<mark>{Match.Value}</mark>",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	}

	/// <summary>
	/// Trims the query and caps its length so a pasted paragraph cannot produce an unbounded pattern.
	/// </summary>
	/// <param name="Query">The raw query.</param>
	/// <returns>The trimmed, length-capped query; empty when blank.</returns>
	private static string NormalizeQuery(string? Query)
	{
		string Trimmed = Query?.Trim() ?? string.Empty;
		return Trimmed.Length > MaxQueryLength ? Trimmed[..MaxQueryLength].TrimEnd() : Trimmed;
	}

	/// <summary>
	/// Escapes <c>LIKE</c> metacharacters, including the escape character itself, so the query matches literally.
	/// </summary>
	/// <param name="Value">The text to embed in a pattern.</param>
	/// <returns>The escaped text, for use with <see cref="LikeEscape"/>.</returns>
	private static string EscapeLike(string Value) =>
		Value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

	/// <summary>
	/// Loads ranked title matches without reading article bodies. Within a rank, articles precede categories, files
	/// and info pages, then shorter titles precede longer ones, then titles sort case-insensitively.
	/// </summary>
	/// <param name="Query">The normalized query.</param>
	/// <param name="Limit">The maximum rows, or <see langword="null"/> for all.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The ranked title matches.</returns>
	private async Task<List<ArticleSearchResult>> QueryTitlesAsync(string Query, int? Limit, CancellationToken CancellationToken)
	{
		string Escaped = EscapeLike(Query);
		string ContainsPattern = $"%{Escaped}%";
		string PrefixPattern = $"{Escaped}%";
		string WordStartPattern = $"% {Escaped}%";
		string BracketedWordStartPattern = $"%({Escaped}%";
		string LowerQuery = Query.ToLowerInvariant();

		IQueryable<ArticleRevision> Matches = Context.ArticleRevisions
			.AsNoTracking()
			.Where(R => R.IsCurrent && R.Type != ArticleType.User && EF.Functions.Like(R.Title, ContainsPattern, LikeEscape))
			.OrderBy(R => R.Title.ToLower() == LowerQuery ? 0
				: EF.Functions.Like(R.Title, PrefixPattern, LikeEscape) ? 1
				: EF.Functions.Like(R.Title, WordStartPattern, LikeEscape) || EF.Functions.Like(R.Title, BracketedWordStartPattern, LikeEscape) ? 2
				: 3)
			.ThenBy(R => R.Type == ArticleType.Article ? 0 : R.Type == ArticleType.Category ? 1 : R.Type == ArticleType.File ? 2 : 3)
			.ThenBy(R => R.Title.Length)
			.ThenBy(R => EF.Functions.Collate(R.Title, "NOCASE"))
			.ThenBy(R => R.Id);

		if (Limit is int Count)
		{
			Matches = Matches.Take(Count);
		}

		return await Matches
			.Select(R => new ArticleSearchResult(R.Title, R.UrlSlug, R.Type, true, null))
			.ToListAsync(CancellationToken);
	}

	/// <summary>
	/// Loads articles whose body, but not title, contains the query, ordered by type, title length and title.
	/// </summary>
	/// <param name="Query">The normalized query.</param>
	/// <param name="Limit">The maximum rows, or <see langword="null"/> for all.</param>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>The candidates with their source text, for snippet extraction.</returns>
	private async Task<List<BodyCandidate>> QueryBodiesAsync(string Query, int? Limit, CancellationToken CancellationToken)
	{
		string ContainsPattern = $"%{EscapeLike(Query)}%";

		IQueryable<ArticleRevision> Matches = Context.ArticleRevisions
			.AsNoTracking()
			.Where(R => R.IsCurrent
				&& R.Type != ArticleType.User
				&& !EF.Functions.Like(R.Title, ContainsPattern, LikeEscape)
				&& EF.Functions.Like(R.Text, ContainsPattern, LikeEscape))
			.OrderBy(R => R.Type == ArticleType.Article ? 0 : R.Type == ArticleType.Category ? 1 : R.Type == ArticleType.File ? 2 : 3)
			.ThenBy(R => R.Title.Length)
			.ThenBy(R => EF.Functions.Collate(R.Title, "NOCASE"))
			.ThenBy(R => R.Id);

		if (Limit is int Count)
		{
			Matches = Matches.Take(Count);
		}

		return await Matches
			.Select(R => new BodyCandidate(R.Title, R.UrlSlug, R.Type, R.Text))
			.ToListAsync(CancellationToken);
	}

	/// <summary>
	/// Converts wiki source to readable prose and cuts a window around the first occurrence of the query.
	/// </summary>
	/// <param name="Text">The article's wiki source.</param>
	/// <param name="Query">The normalized query.</param>
	/// <param name="LeadingChars">Characters of context kept before the match.</param>
	/// <param name="TrailingChars">Characters of context kept after the match.</param>
	/// <returns>The snippet with ellipses at cut edges, or <see langword="null"/> when the query only occurs in markup.</returns>
	private static string? BuildSnippet(string Text, string Query, int LeadingChars, int TrailingChars)
	{
		string Prose = ToPlainProse(Text);
		int Index = Prose.IndexOf(Query, StringComparison.OrdinalIgnoreCase);
		if (Index < 0)
		{
			return null;
		}

		int Start = Math.Max(0, Index - LeadingChars);
		int End = Math.Min(Prose.Length, Index + Query.Length + TrailingChars);

		// Start and end on word boundaries where the window allows it.
		if (Start > 0)
		{
			int Space = Prose.IndexOf(' ', Start, Index - Start);
			if (Space >= 0)
			{
				Start = Space + 1;
			}
		}

		if (End < Prose.Length)
		{
			int Space = Prose.LastIndexOf(' ', End - 1, End - (Index + Query.Length));
			if (Space >= 0)
			{
				End = Space;
			}
		}

		string Snippet = Prose[Start..End].Trim();
		return $"{(Start > 0 ? "…" : string.Empty)}{Snippet}{(End < Prose.Length ? "…" : string.Empty)}";
	}

	/// <summary>
	/// Removes <c>{{…}}</c> directives (including nested and multi-line ones), Markdown link targets, emphasis,
	/// heading and quote markers, and collapses whitespace, leaving the prose a reader would see.
	/// </summary>
	/// <param name="Text">The article's wiki source.</param>
	/// <returns>Single-line plain prose.</returns>
	private static string ToPlainProse(string Text)
	{
		StringBuilder Builder = new(Text.Length);
		int Depth = 0;
		for (int i = 0; i < Text.Length; i++)
		{
			if (i + 1 < Text.Length && Text[i] == '{' && Text[i + 1] == '{')
			{
				Depth++;
				i++;
			}
			else if (Depth > 0 && i + 1 < Text.Length && Text[i] == '}' && Text[i + 1] == '}')
			{
				Depth--;
				i++;
				Builder.Append(' ');
			}
			else if (Depth == 0)
			{
				Builder.Append(Text[i]);
			}
		}

		string Prose = MarkdownImageRegex().Replace(Builder.ToString(), " ");
		Prose = MarkdownLinkRegex().Replace(Prose, "$1");
		Prose = LineMarkerRegex().Replace(Prose, " ");
		Prose = EmphasisRegex().Replace(Prose, string.Empty).Replace('|', ' ');
		return WhitespaceRegex().Replace(Prose, " ").Trim();
	}

	/// <summary>
	/// Matches Markdown images, which carry no readable prose.
	/// </summary>
	[GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownImageRegex();

	/// <summary>
	/// Matches Markdown links, capturing the link text.
	/// </summary>
	[GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
	private static partial Regex MarkdownLinkRegex();

	/// <summary>
	/// Matches heading, quote and list markers at the start of a line, and whole horizontal-rule or table-rule lines.
	/// </summary>
	[GeneratedRegex(@"^[ \t]*(?:#{1,6}|>+|[-*+]|\d+\.)[ \t]+|^[ \t]*[-*_=|: ]{3,}[ \t\r]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex LineMarkerRegex();

	/// <summary>
	/// Matches emphasis and code markers.
	/// </summary>
	[GeneratedRegex(@"\*{1,3}|_{2,3}|`+", RegexOptions.CultureInvariant)]
	private static partial Regex EmphasisRegex();

	/// <summary>
	/// Matches runs of whitespace, including line breaks.
	/// </summary>
	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhitespaceRegex();

	/// <summary>
	/// A body match with the source text needed to build its snippet.
	/// </summary>
	/// <param name="Title">The article title.</param>
	/// <param name="UrlSlug">The article slug.</param>
	/// <param name="Type">The article type.</param>
	/// <param name="Text">The article's wiki source.</param>
	private sealed record BodyCandidate(string Title, string UrlSlug, ArticleType Type, string Text);
}

/// <summary>
/// One search match.
/// </summary>
/// <param name="Title">The article title as matched.</param>
/// <param name="UrlSlug">The article slug, without any type prefix.</param>
/// <param name="Type">The article type, which decides the link path and badge.</param>
/// <param name="IsTitleMatch">Whether the title contains the query.</param>
/// <param name="Snippet">Plain-text context around a body match; <see langword="null"/> for title matches or when the query only occurs in markup.</param>
public sealed record ArticleSearchResult(string Title, string UrlSlug, ArticleType Type, bool IsTitleMatch, string? Snippet)
{
	/// <summary>
	/// Gets the canonical path for the article, including the <c>file:</c> prefix for file articles.
	/// </summary>
	public string Path => ArticleUrlHelper.BuildArticlePath(UrlSlug, Type);
}
