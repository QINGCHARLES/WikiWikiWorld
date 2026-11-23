using Microsoft.EntityFrameworkCore;
using WikiWikiWorld.Data.Extensions;
using WikiWikiWorld.Data.Specifications;

namespace WikiWikiWorld.Web.Pages.Article;

/// <summary>Displays the saved Markdown at a specific public revision URL.</summary>
/// <param name="Context">The database context scoped to the requested site and culture.</param>
/// <param name="SiteResolverService">Resolves the site and culture from the request.</param>
public sealed class SourceModel(WikiWikiWorldDbContext Context, SiteResolverService SiteResolverService)
	: BasePageModel(SiteResolverService)
{
	/// <summary>Gets the title saved with the requested revision.</summary>
	public string ArticleTitle { get; private set; } = string.Empty;

	/// <summary>Gets the original Markdown body, before article rendering.</summary>
	public string Markdown { get; private set; } = string.Empty;

	/// <summary>Gets the canonical URL of the rendered version of this exact revision.</summary>
	public string RevisionPath { get; private set; } = string.Empty;

	/// <summary>Finds the visible revision at the requested slug and timestamp.</summary>
	/// <param name="UrlSlug">The slug as it was saved with the revision, optionally including the file namespace.</param>
	/// <param name="Revision">The revision timestamp from the source route.</param>
	/// <param name="CancellationToken">Cancels the revision lookup when the request ends.</param>
	/// <returns>The source page, a namespace redirect, or a 404 for an unknown revision.</returns>
	public async Task<IActionResult> OnGetAsync([FromRoute] string? UrlSlug, [FromRoute] string? Revision, CancellationToken CancellationToken)
	{
		if (string.IsNullOrWhiteSpace(UrlSlug) || Revision is null || !RevisionDateParser.TryParseRevisionDate(Revision, out DateTimeOffset DateCreated))
			return NotFound();

		string LookupSlug = ArticleUrlHelper.NormalizeLookupSlug(UrlSlug);
		ArticleRevisionBySlugAndDateSpec Spec = new(LookupSlug, DateCreated);
		var SavedRevision = await Context.ArticleRevisions.WithSpecification(Spec)
			.Select(r => new { r.UrlSlug, r.Title, r.Type, r.Text })
			.FirstOrDefaultAsync(CancellationToken);

		if (SavedRevision is null)
			return NotFound();

		RevisionPath = ArticleUrlHelper.BuildRelativePath(SavedRevision.UrlSlug, SavedRevision.Type, $"revision/{Revision}");
		if (ArticleUrlHelper.RequiresCanonicalRedirect(UrlSlug, SavedRevision.Type))
			return RedirectPermanent($"{RevisionPath}/source");

		AllowSearchEngineIndexingOfPage = false;
		ArticleTitle = SavedRevision.Title;
		Markdown = SavedRevision.Text;
		return Page();
	}
}
