namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// The profile overview at <c>/@user/profile</c>: the shared masthead, the user's profile article, their activity
/// calendar, badges, focus areas, and recent activity.
/// </summary>
/// <param name="ProfileService">Loads the profile header and overview data.</param>
/// <param name="ArticleRenderer">Renders the user's profile article with the full wiki pipeline.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class ViewModel(
	ProfileService ProfileService,
	IArticleRenderer ArticleRenderer,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// Gets or sets the user name from the route.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string Username { get; set; } = string.Empty;

	/// <summary>
	/// Gets the shared header data; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileHeader Header { get; private set; } = null!;

	/// <summary>
	/// Gets the overview data; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileOverview Overview { get; private set; } = null!;

	/// <summary>
	/// Gets the rendered profile article HTML, or <see langword="null"/> when the user has not written one.
	/// </summary>
	public string? ProfileArticleHtml { get; private set; }

	/// <summary>
	/// Loads the profile and renders the profile article.
	/// </summary>
	/// <param name="CancellationToken">The request-aborted token.</param>
	/// <returns>The page, or 404 when no user has the requested name.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		ProfileHeader? LoadedHeader = await ProfileService.GetHeaderAsync(Username, User, SiteId, CancellationToken);
		if (LoadedHeader is null)
		{
			return NotFound();
		}

		Header = LoadedHeader;
		Overview = await ProfileService.GetOverviewAsync(Header, CancellationToken);

		if (Overview.ProfileArticle is { } ProfileArticle)
		{
			RenderedArticle Rendered = await ArticleRenderer.RenderAsync(ProfileArticle, SiteId, Culture, $"{Header.ArticlePath}/edit", CancellationToken);
			ProfileArticleHtml = Rendered.Html;
		}

		MetaDescription = Header.Summary ?? $"{Header.UserName}'s contributor profile: {Header.Stats.EditCount.ToString("N0", CultureInfo.InvariantCulture)} edits, {Header.Stats.ArticlesStartedCount.ToString("N0", CultureInfo.InvariantCulture)} articles started.";

		// Accounts with no edits and no profile article are thin pages; keep them out of search indexes.
		AllowSearchEngineIndexingOfPage = Header.Stats.EditCount > 0 || Header.IsProfileArticleWritten;

		return Page();
	}
}
