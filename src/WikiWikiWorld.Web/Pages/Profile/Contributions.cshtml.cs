namespace WikiWikiWorld.Web.Pages.Profile;

/// <summary>
/// The contributions tab at <c>/@user/contributions</c>: the activity calendar and a paged, month-grouped list of the
/// user's revisions, newest first.
/// </summary>
/// <param name="ProfileService">Loads the profile header, calendar, and revisions.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class ContributionsModel(
	ProfileService ProfileService,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>
	/// The number of revisions per page.
	/// </summary>
	public const int PageSize = 50;

	/// <summary>
	/// Gets or sets the user name from the route.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string Username { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the requested one-based page from the <c>PageNumber</c> query value; values below 1 show the first page.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public int PageNumber { get; set; } = 1;

	/// <summary>
	/// Gets the shared header data; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileHeader Header { get; private set; } = null!;

	/// <summary>
	/// Gets the activity calendar; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileActivityCalendar Calendar { get; private set; } = null!;

	/// <summary>
	/// Gets the page of revisions being shown; set by <see cref="OnGetAsync"/> before the page renders.
	/// </summary>
	public ProfileContributionPage Contributions { get; private set; } = null!;

	/// <summary>
	/// Loads the profile header, calendar, and requested page of revisions.
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
		Calendar = await ProfileService.GetCalendarAsync(Header.UserId, CancellationToken);
		Contributions = await ProfileService.GetContributionsAsync(Header.UserId, PageNumber, PageSize, CancellationToken);

		MetaDescription = $"Edit history of {Header.UserName}: {Header.Stats.EditCount.ToString("N0", CultureInfo.InvariantCulture)} edits across {Header.Stats.ArticlesEditedCount.ToString("N0", CultureInfo.InvariantCulture)} articles.";

		// Deep history pages add nothing for search engines beyond the first page.
		AllowSearchEngineIndexingOfPage = Contributions.PageNumber == 1 && Header.Stats.EditCount > 0;

		return Page();
	}
}
