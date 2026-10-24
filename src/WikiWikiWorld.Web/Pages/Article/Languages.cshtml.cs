using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;
using WikiWikiWorld.Web.Configuration;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>
/// Page model for an article's languages subpage (<c>/{slug}/languages</c>): lists the article's translations in the
/// site's other cultures, and lets signed-in editors link, change and remove them.
/// </summary>
/// <remarks>
/// Every write is a POST that redirects back with a status message. Refusals and merge confirmations re-render the
/// page with the editor's input preserved. Reading is anonymous; writing requires sign-in.
/// </remarks>
/// <param name="Context">The database context.</param>
/// <param name="LanguageService">The language group service.</param>
/// <param name="UserManager">The identity user manager, used to identify the acting editor.</param>
/// <param name="SiteConfigurationOptions">The site configuration, which defines the site's cultures.</param>
/// <param name="SiteResolverService">The site resolver service.</param>
public sealed class LanguagesModel(
	WikiWikiWorldDbContext Context,
	ArticleLanguageService LanguageService,
	UserManager<User> UserManager,
	IOptions<SiteConfiguration> SiteConfigurationOptions,
	SiteResolverService SiteResolverService) : BasePageModel(SiteResolverService)
{
	private SiteInfo? CurrentSite;
	private string RootDomain = string.Empty;

	/// <summary>
	/// Gets or sets the requested article slug, from the route.
	/// </summary>
	[BindProperty(SupportsGet = true)]
	public string UrlSlug { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the confirmation from the last successful write, shown once after the redirect.
	/// </summary>
	[TempData]
	public string? StatusMessage { get; set; }

	/// <summary>
	/// Gets the current revision of the article.
	/// </summary>
	public ArticleRevision? CurrentArticle { get; private set; }

	/// <summary>
	/// Gets the article's other languages, ordered by language name.
	/// </summary>
	public IReadOnlyList<ArticleLanguageEntry> Languages { get; private set; } = [];

	/// <summary>
	/// Gets the site's cultures that the article is not yet linked in, for the Add a language picker.
	/// </summary>
	public IReadOnlyList<string> AvailableCultures { get; private set; } = [];

	/// <summary>
	/// Gets the reason the last write was refused, or <see langword="null"/>.
	/// </summary>
	public string? ErrorMessage { get; private set; }

	/// <summary>
	/// Gets the culture of the row whose Change form produced <see cref="ErrorMessage"/>, so that form reopens;
	/// <see langword="null"/> when the error came from the Add a language form.
	/// </summary>
	public string? ErrorChangeCulture { get; private set; }

	/// <summary>
	/// Gets the pending merge awaiting the editor's confirmation, or <see langword="null"/>.
	/// </summary>
	public ArticleLanguageResult? MergePreview { get; private set; }

	/// <summary>
	/// Gets the culture the editor submitted, to refill the forms after a refusal or for a merge confirmation.
	/// </summary>
	public string PostedCulture { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the article address the editor submitted, to refill the forms after a refusal or for a merge confirmation.
	/// </summary>
	public string PostedSlug { get; private set; } = string.Empty;

	/// <summary>
	/// Gets the canonical path of the article.
	/// </summary>
	public string CanonicalArticlePath => CurrentArticle is null ? string.Empty : ArticleUrlHelper.BuildArticlePath(CurrentArticle);

	/// <summary>
	/// Gets the canonical path of this languages page, which every form posts to.
	/// </summary>
	public string LanguagesPath => CurrentArticle is null ? string.Empty : ArticleUrlHelper.BuildRelativePath(CurrentArticle, "languages");

	/// <summary>
	/// Handles the GET request listing the article's languages.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>The page, a 404 for a missing article, or a redirect to the canonical address.</returns>
	public async Task<IActionResult> OnGetAsync(CancellationToken CancellationToken)
	{
		string RequestedUrlSlug = UrlSlug;
		IActionResult? Failure = await LoadArticleAsync(CancellationToken);
		if (Failure is not null)
			return Failure;

		if (ArticleUrlHelper.RequiresCanonicalRedirect(RequestedUrlSlug, CurrentArticle!.Type))
			return RedirectPermanent(LanguagesPath);

		await LoadLanguagesAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Links the article to an article in another language, or asks the editor to confirm a merge of two groups.
	/// </summary>
	/// <param name="TargetCulture">The selected language.</param>
	/// <param name="TargetSlug">The other article's slug or pasted address.</param>
	/// <param name="ConfirmMerge">Whether the editor has confirmed merging two groups.</param>
	/// <param name="Keep">For a confirmed merge, the article to keep for each clashing culture, keyed by culture code.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect after a write, the page with a refusal or merge confirmation, or a sign-in challenge.</returns>
	public async Task<IActionResult> OnPostLinkAsync(
		string? TargetCulture,
		string? TargetSlug,
		bool ConfirmMerge,
		Dictionary<string, Guid>? Keep,
		CancellationToken CancellationToken)
	{
		return await HandleWriteAsync(TargetCulture, TargetSlug, ChangeCulture: null, (Current, Culture, Slug, UserId) =>
			LanguageService.LinkAsync(Current, Culture, Slug, ConfirmMerge, Keep ?? [], UserId, CancellationToken), CancellationToken);
	}

	/// <summary>
	/// Replaces the article linked for one language.
	/// </summary>
	/// <param name="TargetCulture">The language whose linked article to replace.</param>
	/// <param name="TargetSlug">The replacement article's slug or pasted address.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect after a write, the page with a refusal, or a sign-in challenge.</returns>
	public async Task<IActionResult> OnPostChangeAsync(string? TargetCulture, string? TargetSlug, CancellationToken CancellationToken)
	{
		return await HandleWriteAsync(TargetCulture, TargetSlug, ChangeCulture: TargetCulture ?? string.Empty, (Current, Culture, Slug, UserId) =>
			LanguageService.ChangeAsync(Current, Culture, Slug, UserId, CancellationToken), CancellationToken);
	}

	/// <summary>
	/// Removes an article from the language group.
	/// </summary>
	/// <param name="RemoveArticleId">The canonical identifier of the article to unlink; may be this article.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect after a write, the page with a refusal, or a sign-in challenge.</returns>
	public async Task<IActionResult> OnPostRemoveAsync(Guid RemoveArticleId, CancellationToken CancellationToken)
	{
		if (!User.Identity?.IsAuthenticated ?? true)
			return Challenge();

		IActionResult? Failure = await LoadArticleAsync(CancellationToken);
		if (Failure is not null)
			return Failure;

		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser is null)
			return Challenge();

		ArticleLanguageResult Result = await LanguageService.RemoveAsync(CurrentArticle!, RemoveArticleId, CurrentUser.Id, CancellationToken);
		return await CompleteWriteAsync(Result, ChangeCulture: null, CancellationToken);
	}

	/// <summary>
	/// Builds the absolute address of a linked article on its own culture's host.
	/// </summary>
	/// <param name="Entry">The linked article.</param>
	/// <returns>An absolute URL such as <c>https://ja.localhost:7126/gq-japan</c>.</returns>
	public string BuildArticleUrl(ArticleLanguageEntry Entry)
	{
		ArgumentNullException.ThrowIfNull(Entry);
		return SiteCultureHelper.BuildCultureOrigin(GetSite(), RootDomain, Request.Scheme, Request.Host.Port, Entry.Culture)
			+ ArticleUrlHelper.BuildArticlePath(Entry.UrlSlug, Entry.Type);
	}

	/// <summary>
	/// Runs the shared steps of a link or change: sign-in, article lookup, address parsing, then the write itself.
	/// </summary>
	/// <param name="TargetCulture">The submitted language.</param>
	/// <param name="TargetSlug">The submitted slug or pasted address.</param>
	/// <param name="ChangeCulture">The row's culture for a change, or <see langword="null"/> for a link.</param>
	/// <param name="Write">The service call, given the current revision, the resolved culture and slug, and the editor.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect after a write, the page with a refusal or merge confirmation, or a sign-in challenge.</returns>
	private async Task<IActionResult> HandleWriteAsync(
		string? TargetCulture,
		string? TargetSlug,
		string? ChangeCulture,
		Func<ArticleRevision, string, string, Guid, Task<ArticleLanguageResult>> Write,
		CancellationToken CancellationToken)
	{
		if (!User.Identity?.IsAuthenticated ?? true)
			return Challenge();

		IActionResult? Failure = await LoadArticleAsync(CancellationToken);
		if (Failure is not null)
			return Failure;

		PostedCulture = TargetCulture?.Trim() ?? string.Empty;
		PostedSlug = TargetSlug?.Trim() ?? string.Empty;

		if (!TryResolveTarget(PostedCulture, PostedSlug, out string Culture, out string Slug, out string? ParseError))
			return await CompleteWriteAsync(ArticleLanguageResult.Refused(ParseError!), ChangeCulture, CancellationToken);

		User? CurrentUser = await UserManager.GetUserAsync(User);
		if (CurrentUser is null)
			return Challenge();

		ArticleLanguageResult Result = await Write(CurrentArticle!, Culture, Slug, CurrentUser.Id);
		return await CompleteWriteAsync(Result, ChangeCulture, CancellationToken);
	}

	/// <summary>
	/// Turns a service result into a response: redirect with a status message after a write, otherwise re-render.
	/// </summary>
	/// <param name="Result">The write's result.</param>
	/// <param name="ChangeCulture">The row's culture for a change, or <see langword="null"/>.</param>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A redirect to this page, or the page showing a refusal or merge confirmation.</returns>
	private async Task<IActionResult> CompleteWriteAsync(ArticleLanguageResult Result, string? ChangeCulture, CancellationToken CancellationToken)
	{
		switch (Result.Outcome)
		{
			case ArticleLanguageOutcome.Refused:
				ErrorMessage = Result.Message;
				ErrorChangeCulture = ChangeCulture;
				break;
			case ArticleLanguageOutcome.NeedsMergeConfirmation:
				MergePreview = Result;
				break;
			default:
				StatusMessage = Result.Message;
				return Redirect(LanguagesPath);
		}

		await LoadLanguagesAsync(CancellationToken);
		return Page();
	}

	/// <summary>
	/// Resolves the submitted language and address into a culture and slug. A pasted absolute address on this site
	/// supplies both, and its language must match the selected one when a language was selected.
	/// </summary>
	/// <param name="SelectedCulture">The selected language, possibly empty.</param>
	/// <param name="Input">A slug such as <c>gq-japan</c>, a path such as <c>/gq-japan</c>, or an absolute address.</param>
	/// <param name="Culture">The resolved culture.</param>
	/// <param name="Slug">The resolved slug.</param>
	/// <param name="Error">Why the input could not be resolved, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when both a configured culture and a slug were resolved.</returns>
	private bool TryResolveTarget(string SelectedCulture, string Input, out string Culture, out string Slug, out string? Error)
	{
		Culture = SelectedCulture;
		Slug = Input;
		Error = null;

		if (Uri.TryCreate(Input, UriKind.Absolute, out Uri? Address) && (Address.Scheme == Uri.UriSchemeHttps || Address.Scheme == Uri.UriSchemeHttp))
		{
			string? AddressCulture = GetCultureForHost(Address.Host);
			if (AddressCulture is null)
			{
				Error = "That address isn't on this site.";
				return false;
			}

			if (SelectedCulture.Length > 0 && !SelectedCulture.Equals(AddressCulture, StringComparison.OrdinalIgnoreCase))
			{
				Error = $"That address is a {SiteCultureHelper.GetDisplayName(AddressCulture)} page, but {SiteCultureHelper.GetDisplayName(SelectedCulture)} is selected.";
				return false;
			}

			Culture = AddressCulture;
			Slug = Uri.UnescapeDataString(Address.AbsolutePath);
		}

		Slug = Slug.Trim().Trim('/');
		if (Culture.Length == 0 || Slug.Length == 0)
		{
			Error = "Choose a language and enter the other article's address.";
			return false;
		}

		string RequestedCulture = Culture;
		string? ConfiguredCulture = GetSiteCultures().FirstOrDefault(Code => Code.Equals(RequestedCulture, StringComparison.OrdinalIgnoreCase));
		if (ConfiguredCulture is null)
		{
			Error = $"{RequestedCulture} is not one of this site's languages.";
			return false;
		}

		Culture = ConfiguredCulture;
		return true;
	}

	/// <summary>
	/// Gets the culture a host serves on this site.
	/// </summary>
	/// <param name="Host">A host name from a pasted address.</param>
	/// <returns>The culture, or <see langword="null"/> when the host does not belong to this site.</returns>
	private string? GetCultureForHost(string Host)
	{
		if (Host.Equals(RootDomain, StringComparison.OrdinalIgnoreCase))
			return GetSite().DefaultCulture;

		return Host.EndsWith($".{RootDomain}", StringComparison.OrdinalIgnoreCase)
			? Host[..^(RootDomain.Length + 1)]
			: null;
	}

	/// <summary>
	/// Loads the current article and the site configuration every handler needs.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task whose result is a failure response, or <see langword="null"/> when the article was found.</returns>
	private async Task<IActionResult?> LoadArticleAsync(CancellationToken CancellationToken)
	{
		if (SiteId < 1 || string.IsNullOrWhiteSpace(Culture) || string.IsNullOrWhiteSpace(UrlSlug))
			return BadRequest("Invalid parameters.");

		RootDomain = SiteCultureHelper.GetRootDomain(GetSite(), Request.Host.Host);

		string LookupUrlSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
		CurrentArticle = await Context.ArticleRevisions
			.AsNoTracking()
			.WithSpecification(new ArticleRevisionsBySlugSpec(LookupUrlSlug, IsCurrent: true))
			.FirstOrDefaultAsync(CancellationToken);

		return CurrentArticle is null ? NotFound() : null;
	}

	/// <summary>
	/// Loads the article's linked languages and the languages still available to add.
	/// </summary>
	/// <param name="CancellationToken">The cancellation token.</param>
	/// <returns>A task that completes once <see cref="Languages"/> and <see cref="AvailableCultures"/> are set.</returns>
	private async Task LoadLanguagesAsync(CancellationToken CancellationToken)
	{
		IReadOnlyList<ArticleLanguageEntry> Linked = await LanguageService.GetLanguagesAsync(CurrentArticle!.CanonicalArticleId, CancellationToken);
		Languages = [.. Linked.OrderBy(Entry => SiteCultureHelper.GetDisplayName(Entry.Culture), StringComparer.CurrentCultureIgnoreCase)];

		HashSet<string> Taken = new(Linked.Select(Entry => Entry.Culture), StringComparer.OrdinalIgnoreCase) { CurrentArticle.Culture };
		AvailableCultures = [.. GetSiteCultures()
			.Where(Code => !Taken.Contains(Code))
			.OrderBy(SiteCultureHelper.GetDisplayName, StringComparer.CurrentCultureIgnoreCase)];
	}

	/// <summary>
	/// Gets the current site's configuration.
	/// </summary>
	/// <returns>The site configuration matching <see cref="BasePageModel.SiteId"/>.</returns>
	private SiteInfo GetSite() => CurrentSite ??= SiteConfigurationOptions.Value.Sites.First(Site => Site.SiteId == SiteId);

	/// <summary>
	/// Gets the cultures this site serves.
	/// </summary>
	/// <returns>The configured culture codes.</returns>
	private IReadOnlyList<string> GetSiteCultures() => SiteCultureHelper.GetCultures(GetSite(), RootDomain);
}
