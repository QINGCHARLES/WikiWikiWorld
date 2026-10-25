namespace WikiWikiWorld.Web.Pages;

/// <summary>
/// Page model for the full search results page and the header typeahead's suggestion fragment.
/// </summary>
/// <param name="SearchService">Runs the ranked article search.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class SearchModel(
	ArticleSearchService SearchService,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the search query, bound from the lowercase <c>q</c> query-string parameter.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string? Q { get; set; }

	/// <summary>
	/// Gets the ranked results: title matches first, then body matches.
	/// </summary>
	public IReadOnlyList<ArticleSearchResult> SearchResults { get; private set; } = [];

	/// <summary>
	/// Runs the full search for the results page.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token.</param>
	/// <returns>A task that completes when the results are loaded.</returns>
	public async Task OnGetAsync(CancellationToken CancellationToken)
	{
		SearchResults = await SearchService.SearchAsync(Q, CancellationToken);
	}

	/// <summary>
	/// Returns the typeahead fragment (<c>#search-suggestions</c>) for Datastar to patch into the header.
	/// </summary>
	/// <param name="CancellationToken">A cancellation token, cancelled when the reader types again.</param>
	/// <returns>The <c>_SearchSuggestions</c> partial; it renders empty for queries shorter than two characters.</returns>
	public async Task<PartialViewResult> OnGetSuggestionsAsync(CancellationToken CancellationToken)
	{
		SearchResults = await SearchService.SuggestAsync(Q, CancellationToken);
		return Partial("_SearchSuggestions", this);
	}

	/// <summary>
	/// Highlights the search term in text by wrapping it with mark tags.
	/// </summary>
	/// <param name="Text">The plain text to display.</param>
	/// <returns>Encoded HTML with each occurrence of <see cref="Q"/> marked.</returns>
	public string HighlightTerm(string? Text) => ArticleSearchService.Highlight(Text, Q);
}
