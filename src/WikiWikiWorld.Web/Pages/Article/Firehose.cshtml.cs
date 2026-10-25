using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>Serves the revision feed, filters and optional new-change notifications.</summary>
/// <param name="Service">The activity query service.</param>
/// <param name="SiteResolverService">Resolves the current site and language.</param>
public sealed class FirehoseModel(FirehoseService Service, SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	/// <summary>Gets the requested namespace; empty means all types.</summary>
	[BindProperty(SupportsGet = true, Name = "type")]
	public string? Type { get; set; }
	/// <summary>Gets the literal title text used to narrow results.</summary>
	[BindProperty(SupportsGet = true, Name = "q")]
	public string? Search { get; set; }
	/// <summary>Gets an optional public account name.</summary>
	[BindProperty(SupportsGet = true, Name = "editor")]
	public string? Editor { get; set; }
	/// <summary>Gets the selected age window: all, day or week.</summary>
	[BindProperty(SupportsGet = true, Name = "period")]
	public string? Period { get; set; } = "all";
	/// <summary>Gets the exclusive timestamp for an older page.</summary>
	[BindProperty(SupportsGet = true, Name = "before")]
	public DateTimeOffset? Before { get; set; }
	/// <summary>Gets the revision ID that breaks ties at the cursor timestamp.</summary>
	[BindProperty(SupportsGet = true, Name = "beforeid")]
	public int BeforeId { get; set; }
	/// <summary>Gets the bounded page and public contributors.</summary>
	public FirehosePage Feed { get; private set; } = new([], new Dictionary<Guid, FirehoseContributor>(), false);
	/// <summary>Gets measured activity for the selected filters.</summary>
	public FirehoseActivity Activity { get; private set; } = new(0, 0, 0, new int[24], [], 0);
	/// <summary>Gets the UTC instant used for all reporting windows.</summary>
	public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
	/// <summary>Gets the readable language name, preserving unregistered test cultures.</summary>
	public string Language
	{
		get
		{
			try { return CultureInfo.GetCultureInfo(Culture).EnglishName; }
			catch (CultureNotFoundException) { return Culture; }
		}
	}

	/// <summary>Loads the selected revision page and its activity summaries.</summary>
	/// <param name="CancellationToken">Cancels request work.</param>
	/// <returns>The full page or a bad request for malformed filters.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		if (!ValidFilters()) return BadRequest("Invalid Firehose filters.");
		Response.Headers.CacheControl = "no-store";
		IQueryable<ArticleRevision> Query = CreateQuery();
		Feed = await Service.LoadPageAsync(Query, Before, BeforeId, CancellationToken);
		Activity = await Service.LoadActivityAsync(Query, Now, CancellationToken);
		return Page();
	}

	/// <summary>Reports newly added matching revisions without moving the visible feed.</summary>
	/// <param name="After">The snapshot's highest revision ID.</param>
	/// <param name="CancellationToken">Cancels request work.</param>
	/// <returns>A JSON count or a bad request for malformed filters.</returns>
	public async Task<IActionResult> OnGetUpdatesAsync(int After, CancellationToken CancellationToken)
	{
		if (!ValidFilters() || After < 0) return BadRequest();
		Response.Headers.CacheControl = "no-store";
		int Count = await CreateQuery().CountAsync(r => r.Id > After, CancellationToken);
		return new JsonResult(new { count = Count });
	}

	/// <summary>Builds a shareable URL preserving filters and optionally selecting older entries or a namespace.</summary>
	/// <param name="Entry">The last entry, used as the older-page cursor.</param>
	/// <param name="SelectedType">An optional namespace override; empty selects all.</param>
	/// <returns>A local URL with lowercase query parameter names.</returns>
	public string FeedUrl(FirehoseEntry? Entry = null, string? SelectedType = null)
	{
		Dictionary<string, string?> Values = new()
		{
			["type"] = SelectedType ?? Type, ["q"] = Search, ["editor"] = Editor,
			["period"] = Period == "all" ? null : Period
		};
		if (Entry is not null)
		{
			Values["before"] = Entry.DateCreated.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
			Values["beforeid"] = Entry.Id.ToString(CultureInfo.InvariantCulture);
		}
		return QueryHelpers.AddQueryString("/firehose:", Values.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary());
	}

	/// <summary>Checks defined namespaces, supported periods and complete pagination cursors.</summary>
	/// <returns>Whether the bound filters can be executed.</returns>
	private bool ValidFilters() => ModelState.IsValid && (Period is null or "all" or "day" or "week")
		&& (string.IsNullOrEmpty(Type) || (Enum.TryParse(Type, true, out ArticleType Value) && Enum.IsDefined(Value)))
		&& (Before is null ? BeforeId == 0 : BeforeId > 0);

	/// <summary>Applies the shared filter set before pagination.</summary>
	/// <returns>The site/language query.</returns>
	private IQueryable<ArticleRevision> CreateQuery() => Service.Query(SiteId, Culture,
		string.IsNullOrEmpty(Type) ? null : Enum.Parse<ArticleType>(Type, true), Search?.Trim(), Editor?.Trim(),
		Period switch { "day" => Now.AddDays(-1), "week" => Now.AddDays(-7), _ => null });
}
