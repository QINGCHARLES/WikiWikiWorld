# Site customization plan: separating the platform from Magazedia

Status: proposal, planning only (no code changes yet). Written 2026-10-22; first decisions recorded the
same day (§8).
Target runtime: .NET 11, after the upgrade lands as its own change.
Database schema changes are out of scope.

## 1. Summary

- Fanfare and CRTR already run in the same container and process as Magazedia
  ([appsettings.Production.json](../src/WikiWikiWorld.Web/appsettings.Production.json)). Today they get
  Magazedia's title, logo, footer, social links, licensed fonts, Typekit kit, Google CSE script, magazine
  category pills, magazine-cover home grid, publication upload page, and the full magazine Markdig set.
- Only developers will customize the wikis (decision 1), so the proposal uses two tiers:
  1. **Configuration** (restart): a typed, validated per-site profile for simple values such as names,
     logos, links, fonts, feature flags and which directives are on.
  2. **Code** (deploy): site-aware Razor overrides (including each site's home page), per-site Markdig
     directive sets, and feature-gated pages, all chosen per request.

  A third tier for non-developer admins, driven by wiki content, is described in §5.D but not planned.
- A big wiki may later move to its own, possibly sharded, deployment (decision 2). The design keeps that a
  configuration change: the same image, with that deployment's configuration listing only its sites (§5.J).
- Magazine functionality moves into a reusable **Publications pack**, not into "Magazedia". A future
  comics, books or records wiki will want covers, issues and credits too.
- Use folders inside `WikiWikiWorld.Web` first. Extract Razor class libraries only when a second site needs
  code-level customization.
- .NET 11 changes little here. ASP.NET Core 11 adds nothing for Razor Pages. Everything below uses stable
  .NET 7–10 APIs.

## 2. What is hardcoded today

### Layout ([_Layout.cshtml](../src/WikiWikiWorld.Web/Pages/Shared/_Layout.cshtml))

| Line | What | Note |
| --- | --- | --- |
| 17 | `<html lang="en">` | Ignores `ja`/`ar` cultures, with no `dir="rtl"` for Arabic. This is a platform bug, not branding. |
| 20–21 | Lyon font preloads | Branding. The Lyon licence covers multiple sites at current traffic (decision 5), but other sites should only load Lyon if they adopt Magazedia's look. |
| 27 | `<title>… - Magazedia` | Branding. |
| 28 | Typekit kit `kwb1ind` | Adobe Fonts web projects are domain-restricted, so Fanfare and CRTR make a blocking stylesheet request for fonts that will not load. |
| 65 | Google CSE script `cx=d2fb19070cd0c4f3d` | Loads on every page of every site, but nothing in the app references it (no `gcse` element). |
| 69 | Inline Magazedia logo SVG | Branding. |
| 122–216 | Tagline, eight social links, CC BY-SA licence, "© 2021– Magazedia" | Branding. |
| 196–197 | `/dmca:` and `/terms:` links | No `Dmca` or `Terms` pages exist. The routes at `Program.cs:263,265` point at missing pages. |

### Home page ([Index.cshtml.cs](../src/WikiWikiWorld.Web/Pages/Index.cshtml.cs), [Index.cshtml](../src/WikiWikiWorld.Web/Pages/Index.cshtml))

- `Index.cshtml.cs:27`: 46 magazine categories in a static list.
- `Index.cshtml.cs:89`: `LatestArticlesWithPublicationIssueInfoboxSpec` makes the home page a magazine-cover
  grid. A non-magazine wiki sees "No articles with cover images found."
- `Index.cshtml:49` has `<h1>Magazedia</h1>`, and line 7 has an inline `<style>` block.

### Other pages

- `CultureSelect.cshtml`: title and heading "Magazedia".
- `Privacy.cshtml`: Magazedia policy text and `privacy@magazedia.com`.
- `Settings/Theme.cshtml`: "Choose how Magazedia should look".
- `Account/Login.cshtml` and `Register.cshtml`: "upload covers".
- `Wiki/Upload` (publication submission) and the article call-to-action (`View.cshtml:224`,
  `View.cshtml.cs:215`).
- The header paper texture is a global switch in `header-paper-texture.js`, which is a Magazedia design
  choice.

### Markdown

- [MarkdownPipelineFactory](../src/WikiWikiWorld.Web/Services/MarkdownPipelineFactory.cs) builds one global
  pipeline with all 19 extensions for every site.
- [ArticleRenderer](../src/WikiWikiWorld.Web/Services/ArticleRenderer.cs) hardcodes a nine-call enrichment
  sequence.
- `EyebrowType` is an enum of `MAGAZINE`, `MAGAZINEISSUE` and `MAGAZINEFRANCHISE`.
- `ApprovedLinkDomains` is global and lists magazine publishers.
- Eleven extension files each implement their own `|#|` / `|~|` payload splitting, across about 5,000 lines
  and 22 parsers. A new wiki writing its own directive would have to copy that code a twelfth time.

### CSS

Mostly fine, as you thought. `SiteStylesheetCatalog` already supports per-site overrides. There are three
caveats:

- `issue-contents.css:131` and `issue-credits.css:85` hardcode `'Lyon Custom Headings'` instead of using a
  variable, so a site that overrides `core/variables.css` still gets Lyon in those components.
- An override can only replace an existing shared path. A site cannot add a stylesheet of its own.
- Pack CSS (`issue-credits.css` and the rest) loads on every site.

### Configuration and infrastructure

- `SiteResolverService.cs:133` maps any `*.localhost` host to the `localhost` site before it checks
  configured domains. That means Fanfare and CRTR cannot be exercised locally, which would block verifying
  everything in this plan.
- `Admin/DomainRedirects` and `Admin/MigrateFiles` are protected by `[Authorize]` only, so any registered
  user on any site can use them. `Article/Delete` is the same, so any signed-in user can soft-delete any
  article. Roles exist, but nothing checks them (§8, question 4).
- User accounts and roles are global. The `User` and `Role` tables have no `SiteId`, so one login, and any
  role, applies on every wiki.
- `ContentDeliveryNetworkSettings.BaseUrl` (`static.magazedia.com`) is global.

Out of scope because they are database work: the `PublicationUploadQueue*` and `DownloadUrl*` tables stay in
the shared schema, and the Publications pack keeps using them through the shared `WikiWikiWorldDbContext`.

## 3. Constraints that shape the design

1. **One process, many sites, chosen per request.** Every mechanism must pick its behaviour from the site
   resolved from the host. Build-time switches (`#if`, conditional project references) cannot work while
   the sites share a container.
2. **Trust boundary.** All sites share one process and one SQLite file, so code written for one wiki can
   read every wiki's data. Code customizations must therefore be written or reviewed by the platform owner,
   which matches decision 1. This rules out uploaded plugin DLLs.
3. **No schema changes.** Customization state lives in configuration files, in code, or in ordinary article
   revisions.
4. **A lightweight front end.** Server-rendered Razor and native HTML and CSS, with no build step. The things
   a site can customize are partials, view components and CSS, not client frameworks.
5. **Every cache must include the site.** Razor's view lookup cache, Markdig pipelines, `<cache>` fragments
   and output caching all need the site in their key, or one site's view leaks into another. This is the most
   likely correctness bug in any implementation, and each phase below has a test for it.

## 4. Customization tiers

| Tier | Who | A change needs | Good for | Mechanisms |
| --- | --- | --- | --- | --- |
| Configuration | Developers | Restart (reload for plain text) | Names, links, fonts, feature flags, enabled directives, eyebrow labels, infobox schemas | §5.A, §5.F |
| Code | Developers | Deploy | Home pages, structurally different chrome, legal pages and translated prose, directives with database enrichment, site-only pages | §5.B, §5.C, §5.E, §5.G, §5.H |
| Content (not planned) | Wiki admins | An edit | Home intro, footer prose, legal pages, curated lists, and translations of all of these | §5.D |

**Packs versus sites.** A *pack* is reusable domain functionality. The Publications pack contains covers,
issues, credits, contents, downloads and the upload queue. A *site* is one wiki's branding and composition.
Magazedia is the core plus the Publications pack plus Magazedia branding. A configuration-only site, which is
what Fanfare is today, needs no code at all.

## 5. Building blocks

### 5.A Site profile (configuration tier)

Extend `SiteInfo` so the platform has defaults for everything and each site overrides only what it needs.
Change `Sites` from an array to a dictionary keyed by a new `SiteKey`. .NET configuration merges arrays by
index, so two per-site files that each define `Sites:0` would silently overwrite each other. Merging by key
avoids that.

```json
"SiteConfiguration": {
	"Sites": {
		"magazedia": {
			"SiteId": 1,
			"SiteName": "Magazedia",
			"RootDomainIsCultureSelectorOnly": true,
			"Domains": [ "magazedia.com", "en.magazedia.com", "ja.magazedia.com", "ar.magazedia.com" ],
			"Branding": {
				"Tagline": { "en": "The world's largest encyclopedia of magazines, fanzines, journals, mooks and newsletters" },
				"LogoPath": "/sites/magazedia/logo.svg",
				"CopyrightStartYear": 2021,
				"ContentLicense": { "Name": "CC BY-SA 4.0", "Url": "https://creativecommons.org/licenses/by-sa/4.0/" },
				"FontPreloads": [ "/fonts/LyonTextWeb-RegularNoTwo.woff2", "/fonts/LyonDisplayWeb-Black.woff2" ],
				"ExternalStylesheets": [ "https://use.typekit.net/kwb1ind.css" ],
				"SocialLinks": [ { "Network": "x", "Url": "https://x.com/magazedia" } ]
			},
			"Features": [ "PublicationUploads", "HeaderPaperTexture" ],
			"MarkdownDirectives": [ "CoverCarousel", "CoverGrid", "PublicationIssueInfobox", "IssueContents", "IssueCredits", "DownloadsBox" ],
			"ApprovedLinkDomains": [ "gq.com", "wired.com" ],
			"EyebrowTypes": {
				"MAGAZINE": { "en": "Magazine" },
				"MAGAZINEISSUE": { "en": "Magazine Issue" },
				"MAGAZINEFRANCHISE": { "en": "Magazine Franchise" }
			}
		},
		"fanfare": {
			"SiteId": 2,
			"SiteName": "Fanfare",
			"DefaultCulture": "en",
			"Domains": [ "fanfare.wiki" ]
		}
	}
}
```

Fanfare stays minimal, and the defaults give it a clean generic wiki. Larger profiles can live in an optional
`Sites/{SiteKey}/site.json` that is added to configuration, so `appsettings` does not grow without limit.

```csharp
/// <summary>
/// Represents configuration information for a specific site.
/// </summary>
public sealed class SiteInfo
{
	// Existing: SiteId, SiteName, DefaultCulture, RootDomainIsCultureSelectorOnly, Domains.

	/// <summary>
	/// Gets or sets the lowercase key that names this site's override folders and keyed services, for example "magazedia".
	/// Exists only in configuration; database rows remain scoped by <see cref="SiteId"/>. Populated from the dictionary key.
	/// </summary>
	public string SiteKey { get; set; } = string.Empty;

	/// <summary>Gets or sets the name, logo, footer links and head assets; unset values fall back to platform defaults.</summary>
	public SiteBrandingOptions Branding { get; set; } = new();

	/// <summary>Gets or sets the optional feature names enabled for this site, for example "PublicationUploads".</summary>
	public List<string> Features { get; set; } = [];

	/// <summary>Gets or sets the optional directive names enabled in addition to the core set, for example "CoverGrid".</summary>
	public List<string> MarkdownDirectives { get; set; } = [];

	/// <summary>Gets or sets external domains whose links on this site do not receive rel="nofollow".</summary>
	public List<string> ApprovedLinkDomains { get; set; } = [];

	/// <summary>Gets or sets eyebrow codes mapped to culture-keyed display labels.</summary>
	public Dictionary<string, Dictionary<string, string>> EyebrowTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

Implementation notes:

- **Validation.** Use an `IValidateOptions<SiteConfiguration>` that takes dependencies through DI, plus
  `ValidateOnStart()`. It checks that `SiteId` and `SiteKey` are unique, that keys match `^[a-z0-9-]+$`, and
  that every named feature and directive is registered. A typo should fail at startup, not on first request.
- **Per-culture text.** Short strings in configuration are culture-keyed dictionaries. They fall back to the
  site's `DefaultCulture` and then to the first entry. Long prose, such as legal pages, belongs in per-site,
  per-culture partials (§5.B).
- **Configuration or Razor?** With only developers customizing, keep configuration for simple values that
  let a new wiki launch with no Razor at all. Anything with structure, such as the home page, is clearer and
  compile-checked as a Razor override.
- **Resolve the site once per request.** A middleware after `CanonicalDomainMiddleware` stores
  `SiteRequestFeature(SiteInfo Site, string Culture)` in `HttpContext.Features`. `BasePageModel.Site`, the
  layout, the view expander and filters all read it. Today the site is resolved separately in
  `BasePageModel`, `SiteContextService`, `robots.txt` and the sitemap. Keep the existing rules: redirect
  unknown hosts before constructing the tenant `DbContext`, and leave the culture empty on culture-selector
  root domains.

### 5.B Slot partials and a site-aware view location expander (code tier)

Split the layout into named slots. Each slot has a platform default driven by the profile, and any site can
override it:

| Slot | Default renders |
| --- | --- |
| `_SiteHead` | Font preloads, external stylesheets, favicons and analytics from the profile |
| `_SiteBrand` | The logo from `Branding.LogoPath`, or the site name as a wordmark |
| `_SiteFooter` | Tagline, social links (from an icon sprite), licence, copyright and legal links |
| `_HomeBody` | The platform default home page (§5.C) |
| `_ArticleHeader`, `_ArticleMeta` | Hero, eyebrow, title and subtitle, then the authors and actions bar (carved out of the 440-line `View.cshtml`) |
| `_Privacy`, `_Terms`, `_Dmca` | Generic placeholder text. Each site supplies its own, which also fixes the two missing pages. |

An `IViewLocationExpander` resolves overrides from `/Sites/{SiteKey}/Shared/`, trying a culture-specific
file first (for example `_Privacy.ja.cshtml`). Razor looks up partials, layouts and view-component views
(`Components/{Name}/Default`) through the same location formats, so the prepended locations cover all
three. The app does not set `CurrentUICulture` today, so this uses the site culture directly instead of
Razor's built-in `LanguageViewLocationExpander`. Setting the request culture would also change date and
number formatting on `ja` and `ar` pages, which is a separate decision.

```csharp
/// <summary>
/// Lets a site override any partial, layout or view-component view by placing a file under <c>/Sites/{SiteKey}/Shared/</c>,
/// optionally per culture as <c>{Name}.{Culture}.cshtml</c>. Platform locations remain the fallback.
/// </summary>
public sealed class SiteViewLocationExpander : IViewLocationExpander
{
	private const string SiteKeyName = "SiteKey";
	private const string CultureName = "Culture";

	/// <summary>
	/// Records the request's site key and culture. Razor includes these values in its view lookup cache key;
	/// without them, the first site or culture to request a partial would have its override served to all of them.
	/// </summary>
	/// <param name="Context">The expansion context for the current lookup.</param>
	public void PopulateValues(ViewLocationExpanderContext Context)
	{
		SiteRequestFeature? Request = Context.ActionContext.HttpContext.Features.Get<SiteRequestFeature>();
		Context.Values[SiteKeyName] = Request?.Site.SiteKey;
		Context.Values[CultureName] = Request?.Culture;
	}

	/// <summary>
	/// Prepends the site's culture-specific and culture-neutral locations ahead of the platform locations.
	/// </summary>
	/// <param name="Context">The expansion context carrying the populated site key and culture.</param>
	/// <param name="ViewLocations">The platform location formats.</param>
	/// <returns>The site locations followed by the unchanged platform locations.</returns>
	public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext Context, IEnumerable<string> ViewLocations)
	{
		if (!Context.Values.TryGetValue(SiteKeyName, out string? SiteKey) || string.IsNullOrEmpty(SiteKey))
		{
			return ViewLocations;
		}

		string SiteFolder = $"/Sites/{SiteKey}/Shared/";
		if (!Context.Values.TryGetValue(CultureName, out string? Culture) || string.IsNullOrEmpty(Culture))
		{
			return [$"{SiteFolder}{{0}}.cshtml", .. ViewLocations];
		}

		return [$"{SiteFolder}{{0}}.{Culture}.cshtml", $"{SiteFolder}{{0}}.cshtml", .. ViewLocations];
	}
}
```

- **Folder layout.** `src/WikiWikiWorld.Web/Sites/magazedia/Shared/_SiteFooter.cshtml`. The Razor SDK
  compiles `.cshtml` files anywhere in the project. Make the folder name match `SiteKey` exactly, rather than
  relying on case-insensitive lookup on Linux.
- **Prefer slots over layout forks.** A forked `_Layout` silently misses platform fixes such as the
  `lang`/`dir` fix or script ordering. Overriding the whole `_Layout` through the same expander stays
  available as an escape hatch.
- **Optional theme level.** Add a `Theme` key so the search order becomes `Sites/{key}` → `Themes/{theme}` →
  platform. Several wikis could then share an "editorial" or "reference" look and differ only in branding.
  This is cheap to add once the expander exists.
- **Stylesheets.** Key `SiteStylesheetCatalog` by `SiteKey` and allow additional site-only sheets. Let packs
  contribute their CSS, so `issue-credits.css` loads only where Publications is enabled. Replace the two
  hardcoded Lyon references with a variable.

### 5.C Home page composed in Razor from view components (code tier)

`Index.cshtml` shrinks to `<partial name="_HomeBody" />`. The platform's default `_HomeBody` renders a
generic wiki home, and each site that wants something different overrides it through §5.B. The building
blocks are view components, which are the Razor Pages native unit for "a partial with its own data loading".

Because only developers customize (decision 1), the home page is composed in Razor rather than listed in
JSON. View-component tag helpers are checked by the compiler, so a misspelt section fails the build instead
of needing startup validation. The first draft of this plan used JSON-configured sections, and that version
needed extra code to pass settings through and validate component names.

```cshtml
@* Sites/magazedia/Shared/_HomeBody.cshtml *@
<cache vary-by="@($"{Model.SiteId}:{Model.Culture}")" expires-after="@TimeSpan.FromMinutes(5)">
	<vc:category-pills categories="@MagazediaHome.Categories" />
	<vc:recent-covers count="50" />
</cache>
<vc:quick-links />
```

```cshtml
@* Pages/Shared/_HomeBody.cshtml: platform default for any site without an override *@
<vc:site-hero />
<cache vary-by="@($"{Model.SiteId}:{Model.Culture}")" expires-after="@TimeSpan.FromMinutes(5)">
	<vc:recent-articles count="20" />
</cache>
```

```csharp
/// <summary>
/// Renders the most recent cover-bearing articles as a grid. Belongs to the Publications pack.
/// </summary>
/// <param name="Context">The request's site- and culture-scoped database context.</param>
public sealed class RecentCoversViewComponent(WikiWikiWorldDbContext Context) : ViewComponent
{
	/// <summary>
	/// Loads recent covers for the current site and culture; the query filters already scope the context.
	/// </summary>
	/// <param name="Count">The number of covers to show, clamped to 1–100.</param>
	/// <returns>The rendered section.</returns>
	public async Task<IViewComponentResult> InvokeAsync(int Count)
	{
		IReadOnlyList<ArticleWithCover> Covers = await RecentCoverQuery.LoadAsync(Context, Math.Clamp(Count, 1, 100), HttpContext.RequestAborted);
		return View(Covers);
	}
}
```

- **Proposed components.** Core: `SiteHero`, `CategoryPills`, `RecentArticles` and `QuickLinks`.
  Publications pack: `RecentCovers`, which is today's `IndexModel.OnGetAsync` moved into a view component.
- **Tag helpers.** `vc:` tag helpers need `@addTagHelper *, WikiWikiWorld.Web` in `_ViewImports.cshtml`, and
  later the pack's assembly name once code is extracted (§5.H).
- **Category pills.**
  1. Move the 46 entries into Magazedia's folder, where they stay a code-reviewed list.
  2. Later, take each pill's title from its category article in the current culture. The articles are
     already scoped by site and culture, so this also resolves "titles are English on every culture for now".
  3. As an alternative for small wikis: the top N categories by membership count, once the
     category-membership save integration lands. This needs no curation.
- **The `<cache>` key.** Each `<cache>` tag helper's key includes its position in the source file, so the
  same cached block in two sites' partials never collides. `vary-by` must still include site and culture,
  because the platform default partial is shared by every site that does not override it.

### 5.D Content-defined site pages, the MediaWiki model (content tier, not planned)

> **Not planned (decision 1).** Non-developers will not customize the wikis, so this tier is not needed.
> Legal pages and translated prose become per-site, per-culture partials (§5.B) instead. This section is
> kept as a record in case that changes. If it is revived, protection (below) must exist before any
> `site:` page is editable.

Reserve a `site:` namespace, following the existing `category:`, `file:` and `wiki:` conventions. Articles are
already scoped by site and culture, so translation comes for free, and so do revision history and revert.

- **Slugs:** `site:home-intro`, `site:footer`, `site:about`, `site:privacy`, `site:terms` and `site:dmca`.
  `/privacy-policy:`, `/terms:` and `/dmca:` render the matching `site:` article when it exists, and a platform
  default otherwise. This also fixes the two missing pages and the Magazedia-specific privacy text.
- **Protection without a schema change.** Add a write-path check in `ArticleRevisionService`, which already
  handles edits from both the API and Razor Pages. Edits to `site:` articles require a global `Administrator`
  role or a per-site role named by convention, `SiteAdmin:{SiteKey}`. Identity roles are plain global strings,
  so the convention needs no schema change. Apply the same role requirement to the `Admin/*` pages.
- **Widget directives**, enabled only in the `site:` pipeline variant (§5.E): `{{RecentArticles Count=12}}`,
  `{{CategoryPills news-politics |#| general-interest}}`, `{{RecentCovers Count=50}}` (Publications) and
  `{{FeaturedArticle some-slug}}`. Counts are clamped, and the rendered HTML is cached by revision ID with a
  short time-to-live for live data.
- **Trade-off.** Admins can change the home page and legal pages without a deploy, in every culture. In
  exchange, layout precision is lower, widget syntax becomes another public contract to keep stable, and
  protection must exist before this is enabled.

If revived, a `ContentBlock` view component rendering a `site:` article would slot into any `_HomeBody`, so
Razor would own the structure while content owned the prose.

### 5.E Per-site Markdown pipelines and a directive registry (code and configuration tiers)

Wrap each extension in a small descriptor that the pipeline factory and renderer iterate, instead of naming
each extension directly.

```csharp
/// <summary>
/// A named Markdig directive bundle that a site can enable, plus the post-parse work it needs.
/// </summary>
public interface IWikiDirective
{
	/// <summary>Gets the configuration name, for example "CoverGrid".</summary>
	string Name { get; }

	/// <summary>Gets a value indicating whether every site receives this directive without opting in.</summary>
	bool IsCore { get; }

	/// <summary>Gets when <see cref="EnrichAsync"/> runs relative to other directives.</summary>
	EnrichmentPhase Phase { get; }

	/// <summary>Adds this directive's parsers and renderers to one site's pipeline builder.</summary>
	/// <param name="Builder">The pipeline being built for <paramref name="Site"/>.</param>
	/// <param name="Site">The site whose settings, such as approved link domains, apply.</param>
	void Configure(MarkdownPipelineBuilder Builder, SiteInfo Site);

	/// <summary>Resolves data the markdown cannot carry, such as database rows or labels, after parsing and before rendering.</summary>
	/// <param name="Context">The parsed document and request scope.</param>
	/// <param name="CancellationToken">Propagates request cancellation.</param>
	/// <returns>A task that completes when the document has been enriched.</returns>
	ValueTask EnrichAsync(ArticleRenderContext Context, CancellationToken CancellationToken) => ValueTask.CompletedTask;
}

/// <summary>Orders enrichment: metadata extraction, then data resolution, then passes that walk the finished tree.</summary>
public enum EnrichmentPhase
{
	/// <summary>Synchronous metadata extraction such as subtitle, eyebrow and source.</summary>
	Metadata,

	/// <summary>Database lookups such as images, covers and downloads.</summary>
	Resolve,

	/// <summary>Whole-document passes that number or aggregate nodes, such as footnotes and citations.</summary>
	Finalize
}

/// <summary>Adapts the existing cover grid extension to the registry without changing its parser.</summary>
public sealed class CoverGridDirective : IWikiDirective
{
	/// <inheritdoc/>
	public string Name => "CoverGrid";

	/// <inheritdoc/>
	public bool IsCore => false;

	/// <inheritdoc/>
	public EnrichmentPhase Phase => EnrichmentPhase.Resolve;

	/// <inheritdoc/>
	public void Configure(MarkdownPipelineBuilder Builder, SiteInfo Site) => Builder.Use(new CoverGridExtension());

	/// <inheritdoc/>
	public ValueTask EnrichAsync(ArticleRenderContext Context, CancellationToken CancellationToken)
		=> new(CoverGridExtension.EnrichAsync(Context.Document, Context.Db, Context.Site.SiteId, Context.Culture, CancellationToken));
}
```

The renderer's hardcoded sequence becomes a loop over the site's enabled directives, sorted by phase and then
by registration order:

```csharp
// Sequential by design: enrichers share the request's DbContext, which must not run overlapping operations.
foreach (IWikiDirective Directive in SitePipeline.Directives)
{
	await Directive.EnrichAsync(RenderContext, CancellationToken);
}
```

- **Pipelines.** Build them once per site at startup and cache them in a `FrozenDictionary<int, SitePipeline>`.
  This matches the stylesheet catalog's "restart to change" behaviour. Variants are keyed by
  `(Site, Kind)`, where `Kind` is `Article` or `Talk` (unchanged and shared). A `SitePage` variant with
  widget directives would be added only if §5.D is revived.
  Keep the current `Use(...)` order, because Markdig tries block parsers that share the `{` opening character
  in registration order. The golden test in Phase 2 catches any drift.
- **Per-site link domains fall out naturally.** `CitationExtension` and `IssueCreditsExtension` already take
  approved domains in their constructors, so building per site passes each site its own list.
- **Classification.**

  | Group | Directives |
  | --- | --- |
  | Core (every site) | ShortDescription, Subtitle, Eyebrow (labels from configuration), HeaderImage, Image, Source, Category, Footnote, Citation, PullQuote, Timeline, YouTube, Stub, ApprovedNoFollowLinks |
  | Publications pack | CoverCarousel, CoverGrid, PublicationIssueInfobox, IssueContents, IssueCredits, DownloadsBox, together with `ArticleCoverReference` and `LatestArticlesWithPublicationIssueInfoboxSpec` |

  `DownloadsBox` is debatable, since file downloads are generic, but it is tied to the publication download
  tables.
- **`DirectivePayload` helper.** Extract the shared grammar into one helper: `|~|` separates items, `|#|`
  separates properties, `Key=Value` keys are case-insensitive, and repeated keys accumulate. With that, a
  wiki's own directive is about 50 lines of parser and renderer. Migrate the eleven existing copies
  gradually, using the existing tests to confirm behaviour is unchanged.
- **Disabled or unknown directives.** Register a catch-all block parser last. It matches `{{Identifier` at
  the start of a line when no enabled parser claimed it, and renders the escaped source in a
  `directive-unsupported` wrapper. This follows the project's existing "invalid source stays visible"
  convention from `IssueContents`. Editors see typos and cross-wiki pastes instead of silent loss.
- **Typed metadata keys.** Replace the `as string` casts on `Document.GetData` with
  `DocumentKey<T>(string Name)` so pack-specific metadata can be added without growing `RenderedArticle`.
  Eyebrow, subtitle, header image and short description stay on `RenderedArticle`, because every wiki uses
  them.

### 5.F Declarative directives (configuration tier)

Many "custom data types" are really infoboxes and labels, which a schema can describe without code. Only
developers will write these schemas (decision 1), but a schema is still far less code to write and review
than a new parser for each data type:

- **Eyebrow types** come from the site's `EyebrowTypes`, with labels per culture. Fanfare could define
  `CHARACTER` and `EPISODE`. Unknown codes are consumed silently, as they are today.
- **Infobox schemas** add a generic `{{Infobox Character |#| Species=Human |#| FirstAppearance=...}}`, driven
  by a per-site schema with a title, ordered field labels and field kinds. The field kinds are `Text`,
  `Markdown`, `Date`, `ArticleLink` and `File`, where `File` reuses the `Image` enricher. Aliases keep existing
  syntax working, so `PublicationIssueInfobox` becomes an alias for the `PublicationIssue` schema.
- **Limits.** This is display only. Querying structured fields across articles, such as "every issue from
  1993", needs storage, which is out of scope. Write code directives (§5.E) only for directives with real
  behaviour: database enrichment, interactivity, or computed layout.

### 5.G Site-only pages, and replacing a whole page

**Feature gates (recommended).** Publication uploads become the `PublicationUploads` feature:

```csharp
/// <summary>
/// Marks a page as available only on sites that enable a feature; other sites receive 404.
/// </summary>
/// <param name="Feature">The feature name that must appear in the site's <c>Features</c> list.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SiteFeatureAttribute(string Feature) : Attribute, IAsyncPageFilter
{
	/// <summary>Gets the required feature name.</summary>
	public string Feature { get; } = Feature;

	/// <inheritdoc/>
	public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext Context) => Task.CompletedTask;

	/// <inheritdoc/>
	public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext Context, PageHandlerExecutionDelegate Next)
	{
		SiteInfo? Site = Context.HttpContext.Features.Get<SiteRequestFeature>()?.Site;
		if (Site is null || !Site.Features.Contains(Feature, StringComparer.OrdinalIgnoreCase))
		{
			Context.Result = new NotFoundResult();
			return Task.CompletedTask;
		}

		return Next();
	}
}
```

The same check hides the publication call-to-action in `View.cshtml`, the related links, and the sitemap and
`robots.txt` entries.

**Same route, different page per site (deferred).** A custom `MatcherPolicy` could invalidate route
candidates whose site metadata does not match, so a Magazedia `Index` could win `/` on Magazedia while the
platform `Index` serves other sites. This needs a spike first: Razor Pages compiles pages lazily and swaps in
compiled endpoints inside its own matcher policy, so site metadata would have to be attached through a page
route model convention. Do not build this until a case appears that slot overrides (§5.B) cannot handle.
Forked pages drift from platform fixes for the same reason forked layouts do.

### 5.H Packaging: folders first, Razor class libraries later

Phase 1 to 4 layout, all inside `WikiWikiWorld.Web`:

```
src/WikiWikiWorld.Web/
	Packs/Publications/      Directives, Components, Pages (Upload), Queries, registration
	Sites/magazedia/Shared/  _SiteFooter.cshtml, _SiteBrand.cshtml, Components/... overrides
	wwwroot/sites/magazedia/ logo.svg, css overrides, Lyon fonts
```

Registration uses C# 14 extension blocks:

```csharp
/// <summary>Service registration for the Publications pack.</summary>
public static class PublicationsPackRegistration
{
	extension(IServiceCollection Services)
	{
		/// <summary>Registers the cover, issue and download directives; view components and pages are discovered automatically.</summary>
		/// <returns>The same collection, for chaining.</returns>
		public IServiceCollection AddPublicationsPack()
		{
			Services.AddSingleton<IWikiDirective, CoverCarouselDirective>();
			Services.AddSingleton<IWikiDirective, CoverGridDirective>();
			Services.AddSingleton<IWikiDirective, PublicationIssueInfoboxDirective>();
			Services.AddSingleton<IWikiDirective, IssueContentsDirective>();
			Services.AddSingleton<IWikiDirective, IssueCreditsDirective>();
			Services.AddSingleton<IWikiDirective, DownloadsBoxDirective>();
			return Services;
		}
	}
}
```

When a site needs a different implementation of a service, use keyed DI with a platform fallback rather than
`if (SiteId == 1)` branches. Use it only when there are genuinely several implementations:

```csharp
/// <summary>Resolves per-site service overrides.</summary>
public static class SiteServiceProviderExtensions
{
	extension(IServiceProvider Services)
	{
		/// <summary>Returns the site's keyed registration of <typeparamref name="TService"/>, or the platform registration when the site has none.</summary>
		/// <typeparam name="TService">The service contract.</typeparam>
		/// <param name="SiteKey">The current site's key.</param>
		/// <returns>The resolved service.</returns>
		public TService GetSiteService<TService>(string SiteKey) where TService : notnull
			=> Services.GetKeyedService<TService>(SiteKey) ?? Services.GetRequiredService<TService>();
	}
}
```

**When to extract libraries.** Extract when a second site needs code, or when you want an image without some
packs. The target shape is:

- `WikiWikiWorld.Platform`, a Razor class library holding `SiteInfo`, `SiteRequestFeature`, the directive
  contracts, `DirectivePayload`, `BasePageModel` and the shared partials.
- `WikiWikiWorld.Packs.Publications`, a Razor class library.
- `WikiWikiWorld.Sites.Magazedia`, a Razor class library.
- `WikiWikiWorld.Web`, the host.

The contracts do not change between the folder phase and the library phase, so extraction is mechanical. It
is still real work, because `BasePageModel` and the site services have to leave the web project to avoid a
circular reference. Assets from a Razor class library are served from `_content/{Assembly}/`, so the
stylesheet catalog must probe there.

### 5.I Considered and not recommended for now

| Option | Why not now |
| --- | --- |
| Runtime plugin DLLs (`AssemblyLoadContext` from `/app/data/plugins`) | Breaks the trust boundary in §3, since one wiki's code could read every wiki's data. MVC application parts cannot be unloaded, and plugins would drift out of step with versions of the platform contracts. |
| Orchard Core | Real per-tenant containers, themes and modules, but adopting it means migrating the platform. This plan borrows its ideas instead: theme fallback (§5.B), zones, and per-tenant features (§5.G). |
| Finbuckle.MultiTenant | Mature tenant resolution, per-tenant options and per-tenant auth cookies. Worth adopting if per-tenant authentication or data-protection isolation becomes a requirement. Until then the existing resolver and the profile are enough. |
| A dedicated image per wiki (build-time composition) | Contradicts today's one-container topology. The module design still allows it later for a large customer, because a host would simply register only that site's packs. |
| Templates stored in the database (Liquid or Fluid) for admins | Adds a second template language and an HTML-injection risk on each site's own origin. Moot after decision 1: only developers customize, and they have Razor. |
| Razor components through `DynamicComponent` for sections | Workable from Razor Pages, but adds a second component model. View components are native to this app. .NET 11's `CacheView` targets Blazor static SSR, so do not assume it applies inside Razor Pages. |

### 5.J A dedicated or sharded deployment for a big wiki

A big wiki may move to its own, possibly sharded, deployment (decision 2). The customization design should
make that a configuration change, not a rebuild:

- **One image, different configuration.** Every deployment runs the same image. A deployment's
  configuration lists only the sites it serves. Other sites' code ships in the image but never runs, because
  nothing executes for a site that is not configured. Extracting Razor class libraries therefore stays
  optional even then.
- **Keep the `SiteId`.** Every row carries `SiteId`, and uploads live under `data/sitefiles/{SiteId}`. A wiki
  that moves keeps its ID, so its data moves unchanged.
- **Keep deployment settings out of the site profile.** `DomainRedirects` (including `UnknownHostTarget`),
  `ContentDeliveryNetworkSettings`, Kestrel certificates and the data path belong to a deployment, not to a
  site.
- **No in-process cross-site features.** Nothing should assume every wiki lives in the same process or
  database, such as an "all our wikis" page. The plan already resolves everything from configuration and
  the request host.
- **What "sharded" changes depends on its meaning (§8, question 8).**
  - *Split by culture* (for example English on one deployment, Japanese on another): data is already scoped
    by site and culture. The culture-selector root page builds its links from `Domains`, which today also
    means "domains this deployment serves". A shard would need to link to cultures it does not serve, so
    that list would split into served domains and linked culture domains.
  - *Several copies behind a load balancer:*
    - In-memory caches (`<cache>`, `IMemoryCache`, the startup-built catalogs and pipelines) are per
      instance. That is fine for read caches with a time-to-live, but nothing may rely on in-memory state
      being the same on every instance.
    - The Data Protection key ring must be shared across instances, or sign-ins and antiforgery tokens fail
      when a request lands on a different node.
    - A load balancer replaces "Kestrel terminates TLS directly", so forwarded-header handling becomes
      necessary.
    - SQLite's single-writer model is the bigger question, but that is database work and out of scope.

## 6. Recommended path

Upgrade to .NET 11 first, as its own change, so failures stay attributable.

**Phase 0: quick wins (small, no new abstractions).**

1. Make site resolution use the longest matching configured domain before the `*.localhost` fallback. Add
   development domains such as `fanfare.localhost` and `crtr.localhost`. This comes first, because every
   later phase must be checked on more than one site.
2. Add minimal `Branding` options: title suffix, headings, logo, tagline, social links, licence, copyright
   year, font preloads and external stylesheets. The layout reads them, and the defaults use `SiteName`.
3. Remove the unused Google CSE script, or make it a per-site setting if a search integration is planned.
4. Set `lang` and `dir` from the request culture.
5. Move home categories to configuration and hide the pills when the list is empty. When no covers exist,
   fall back to recent articles.
6. Gate `Wiki/Upload` and the publication call-to-action behind `PublicationUploads`, and make the
   Login/Register text generic.
7. Make `ApprovedLinkDomains` per site, merging in the global list during the transition.
8. Require an administrator role on the `Admin/*` pages and on article deletion (§8, question 4).

Verify Phase 0 with `dotnet build WikiWikiWorld.slnx` and
`dotnet test tests/WikiWikiWorld.Web.Tests -c Release`. Then run the app and compare Magazedia's home and
article HTML before and after: only the intended differences should appear. Load Fanfare and CRTR locally
and confirm no Magazedia branding, fonts or scripts remain.

**Phase 1: site profile and slots (medium).**
- Key `Sites` by `SiteKey`, add optional per-site JSON files, validate options, and add `SiteRequestFeature`.
- Add the `SiteViewLocationExpander` and the slot partials. Magazedia's overrides go under
  `Sites/magazedia/Shared`.
- Key the stylesheet catalog by `SiteKey`, support site-only sheets and pack CSS, and replace the hardcoded
  Lyon references.
- Add per-site, per-culture legal partials behind `/privacy-policy:`, `/terms:` and `/dmca:`, fixing the
  two missing pages.
- Tests:
  - Two sites requesting the same partial name get different views. This guards the view cache key.
  - Profile validation rejects duplicate IDs and keys, and unknown names.
  - The layout renders each site's name.

  Full-page tests would need `Microsoft.AspNetCore.Mvc.Testing`, a test-only dependency that needs your
  approval.

**Phase 2: directive registry and per-site pipelines (medium to large; highest regression risk).**
- Add `DirectivePayload` with parity tests, `IWikiDirective` adapters for all 19 extensions, per-site
  pipelines, the renderer loop, the unsupported-directive fallback, configured eyebrow labels, and per-site
  link domains.
- **Golden test:** render every current revision from a disposable copy of the production database with
  both the old and new renderer. Magazedia output must be byte-identical. Also check that Fanfare's pipeline
  rejects `{{CoverGrid}}` visibly.

**Phase 3: home composition (medium).** Build the view components from §5.C and move today's home query
into `RecentCovers`. Write the platform default `_HomeBody` and Magazedia's override. `Index.cshtml` becomes
a single partial call.

**Phase 4: not planned.** The content tier (§5.D) existed for non-developer admins (decision 1).

**Phase 5: as demand appears.** Declarative infobox schemas with aliases, extraction into Razor class
libraries, a theme level, and the matcher-policy spike if it is ever needed.

Order and value: Phases 0 and 1 immediately stop Fanfare and CRTR from shipping as Magazedia. Phases 2 and 3
are where "other wikis get options" becomes real. Nothing here blocks a later dedicated deployment (§5.J).

## 7. .NET 11 relevance

- ASP.NET Core 11's release notes list no Razor Pages, Razor view engine or multi-tenancy features. The
  release is focused on Blazor, Minimal APIs, SignalR and OpenAPI. Nothing in this plan waits for .NET 11,
  and nothing in it should be affected by the upgrade.
- C# 15 union types and closed hierarchies required `LangVersion preview` during the previews, and
  `AGENTS.md` rules out preview language features without an agreed need. If they ship as stable at GA, a
  closed hierarchy for section kinds or enrichment phases would give exhaustive switches. That would be a
  nicety, not a structural change, so do not design around them.
- C# 15 collection expression arguments (passing a comparer inside `[...]`) would tidy registry dictionaries.
  Confirm they are stable at GA before using them.
- Everything the plan depends on already exists: view location expanders, view components, the `<cache>`
  tag helper, output caching (.NET 7), keyed DI (.NET 8), options validation, frozen collections, and C# 14
  extension blocks.

## 8. Decisions

### Answered 2026-10-22

1. **Who customizes:** developers only. The content tier (§5.D) is not planned. Home pages are composed in
   Razor (§5.C), and legal pages and translated prose are per-site, per-culture partials (§5.B).
2. **Hosting:** a big wiki may later get its own, possibly sharded, deployment. See §5.J.
5. **Lyon:** the licence covers multiple sites at current traffic.

### Still open

3. **Where does the magazine code live?** This is the code for:
   - the directives CoverCarousel, CoverGrid, PublicationIssueInfobox, IssueContents, IssueCredits and
     DownloadsBox;
   - the "Submit a Publication" page and the "Have a copy of this publication?" prompt;
   - the recent-covers home grid.

   The tables behind them stay where they are either way. The two options:
   - *Magazedia-only.* The code goes in `Sites/magazedia/`, and only Magazedia can switch it on. Magazine
     wording can stay hardcoded. This is the simplest option.
   - *A shared Publications pack.* The code goes in `Packs/Publications/`, and any site can switch on
     individual pieces in its profile. Wording that says "magazine" or "publication" has to become a label
     each site sets.

   The real question is whether any wiki you host, now or plausibly later, has covers, issues or credits:
   comics, records, books, games or podcasts, for example. If yes, or if you are unsure, use the pack, but
   generalize wording only when a second site actually turns a piece on. The choice is cheap to reverse,
   because the directive registry (§5.E) already lets each piece be switched on separately; moving a folder
   later is mechanical. A finer split also works: covers and credits are broadly useful, while the upload
   queue and its copyright exemptions are archive-specific and could stay Magazedia's.
4. **Who may use admin tools, and is that per wiki?** Today, user accounts and roles are global, so one login
   works on all three wikis and a role would apply on all of them. Nothing checks roles yet:
   `Admin/DomainRedirects`, `Admin/MigrateFiles` and `Article/Delete` only require a login. Phase 0 locks
   these down with a role whatever the answer.

   The question is whether you will ever give someone elevated rights on one wiki but not the others, such
   as a Fanfare moderator who should not moderate Magazedia:
   - *No:* one global `Administrator` role is enough. This is the recommendation for now.
   - *Yes:* doing it properly needs a site column on the user-to-role link, which is a schema change. The
     workaround with no schema change puts the site in the role name, for example `Moderator:fanfare`, and
     checks `IsInRole("Administrator") || IsInRole($"Moderator:{SiteKey}")`.

   A dedicated deployment (decision 2) has its own database, so its users and roles would be separate
   anyway.
6. **Restart semantics.** Is a restart acceptable for directive and feature changes, as it already is for
   stylesheets, with only plain text reloading live?
7. **Site keys and folders.** Rename the CSS override folder from `css/sites/1/` to a key-based path.
8. **What "sharded" means.** Split by culture, or several copies behind a load balancer (§5.J)? This does
   not change Phases 0–3, but it decides how much of §5.J applies.
9. **Typekit.** Adobe Fonts kits only work on the domains listed in the kit. Should Fanfare and CRTR stop
   loading `kwb1ind` (the default in this plan), or will they share Magazedia's type?

## 9. Testing the assumptions

- **"The CSS should be OK."** Largely true, because the override mechanism exists and works. Three things
  undercut it: fonts leak through preloads, Typekit and two hardcoded `font-family` values; sites cannot add
  their own sheets; and pack CSS loads everywhere. The fixes are small and belong in Phase 1.
- **"Page designs might need custom Razor pages."** The evidence points the other way. Almost every
  Magazedia-specific item is layout chrome, the home page body, or one feature page. Slots, sections and
  feature gates cover all of them without forking a page, and page forks carry the highest maintenance cost
  in this design. Keep whole-page replacement as a last resort.
- **"Wikis will want custom Markdig extensions."** Likely, but many will be infobox-shaped and better served
  by schemas (§5.F) than by code. Code directives should be reserved for real behaviour, and the payload
  helper keeps those cheap.
- **Risk of over-engineering.** Building a full module platform for three sites, when two may have little
  content, would be speculative. The phases are ordered so the cheap work (Phases 0 and 1) pays off
  immediately, and the library, theme and matcher-policy work waits for a real second customer. Decision 1
  removed the largest optional piece, the content tier, and simplified the home page from JSON sections to
  Razor composition.
