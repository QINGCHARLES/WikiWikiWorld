# Site customization plan: separating the platform from Magazedia

Status: proposal, planning only (no code changes yet). Written 2026-10-22.
Target runtime: .NET 11, after the upgrade lands as its own change.
Database schema changes are out of scope.

## 1. Summary

- Fanfare and CRTR already run in the same container and process as Magazedia
  ([appsettings.Production.json](../src/WikiWikiWorld.Web/appsettings.Production.json)). Today they get
  Magazedia's title, logo, footer, social links, licensed fonts, Typekit kit, Google CSE script, magazine
  category pills, magazine-cover home grid, publication upload page, and the full magazine Markdig set.
- The proposal sorts customization into three tiers by who does the customizing:
  1. **Configuration** (platform operator, restart): a typed, validated per-site profile.
  2. **Code** (platform developers, deploy): site-aware Razor overrides, home sections as view
     components, per-site Markdig directive sets, and feature-gated pages, all chosen per request.
  3. **Content** (wiki admins, no deploy): reserved `site:` articles for the home intro, footer and legal
     pages, with a few widget directives.
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
| 20–21 | Lyon font preloads | Lyon is a commercially licensed face. Check that its web licence covers the Fanfare and CRTR domains. |
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
  user on any site can use them. This matters because the content tier (§5.D) needs a real "site admin"
  concept.
- `ContentDeliveryNetworkSettings.BaseUrl` (`static.magazedia.com`) is global.

Out of scope because they are database work: the `PublicationUploadQueue*` and `DownloadUrl*` tables stay in
the shared schema, and the Publications pack keeps using them through the shared `WikiWikiWorldDbContext`.

## 3. Constraints that shape the design

1. **One process, many sites, chosen per request.** Every mechanism must pick its behaviour from the site
   resolved from the host. Build-time switches (`#if`, conditional project references) cannot work while
   the sites share a container.
2. **Trust boundary.** All sites share one process and one SQLite file, so code written for one wiki can
   read every wiki's data. Code customizations must therefore be written or reviewed by the platform owner.
   Wiki admins get configuration (through the operator) and content only. This rules out uploaded plugin
   DLLs.
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
| Configuration | Operator | Restart (reload for plain text) | Names, links, fonts, feature flags, enabled directives, home section list, eyebrow labels, infobox schemas | §5.A, §5.F |
| Code | Developers | Deploy | Structurally different chrome, sections that query data, directives with database enrichment, site-only pages | §5.B, §5.C, §5.E, §5.G, §5.H |
| Content | Wiki admins | An edit | Home intro, footer prose, legal pages, curated lists, and translations of all of these | §5.D |

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
			},
			"HomeSections": [
				{ "Component": "CategoryPills", "Settings": { "Categories": [ { "Title": "News & Politics", "Slug": "news-politics" } ] } },
				{ "Component": "RecentCovers", "Settings": { "Count": 50 } },
				{ "Component": "QuickLinks" }
			]
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

	/// <summary>Gets or sets the ordered home page sections; empty means the platform default sections.</summary>
	public List<HomeSectionOptions> HomeSections { get; set; } = [];
}
```

Implementation notes:

- **Validation.** Use an `IValidateOptions<SiteConfiguration>` that takes dependencies through DI, plus
  `ValidateOnStart()`. It checks that `SiteId` and `SiteKey` are unique, that keys match `^[a-z0-9-]+$`, and
  that every named feature, directive and home component is registered. A typo should fail at startup, not
  on first request.
- **Per-culture text.** Short strings in configuration are culture-keyed dictionaries. They fall back to the
  site's `DefaultCulture` and then to the first entry. Long prose belongs in the content tier.
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
| `_HomeBody` | The configured home sections (§5.C) |
| `_ArticleHeader`, `_ArticleMeta` | Hero, eyebrow, title and subtitle, then the authors and actions bar (carved out of the 440-line `View.cshtml`) |

An `IViewLocationExpander` resolves overrides from `/Sites/{SiteKey}/Shared/`. Razor looks up partials,
layouts and view-component views (`Components/{Name}/Default`) through the same location formats, so one
prepended location covers all three.

```csharp
/// <summary>
/// Lets a site override any partial, layout or view-component view by placing a file under <c>/Sites/{SiteKey}/Shared/</c>.
/// Platform locations remain the fallback.
/// </summary>
public sealed class SiteViewLocationExpander : IViewLocationExpander
{
	private const string SiteKeyName = "SiteKey";

	/// <summary>
	/// Records the request's site key. Razor includes these values in its view lookup cache key;
	/// without this, the first site to request a partial would have its override served to every site.
	/// </summary>
	/// <param name="Context">The expansion context for the current lookup.</param>
	public void PopulateValues(ViewLocationExpanderContext Context)
	{
		Context.Values[SiteKeyName] = Context.ActionContext.HttpContext.Features.Get<SiteRequestFeature>()?.Site.SiteKey;
	}

	/// <summary>
	/// Prepends the site's folder ahead of the platform locations.
	/// </summary>
	/// <param name="Context">The expansion context carrying the populated site key.</param>
	/// <param name="ViewLocations">The platform location formats.</param>
	/// <returns>The site location followed by the unchanged platform locations.</returns>
	public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext Context, IEnumerable<string> ViewLocations)
	{
		if (!Context.Values.TryGetValue(SiteKeyName, out string? SiteKey) || string.IsNullOrEmpty(SiteKey))
		{
			return ViewLocations;
		}

		return [$"/Sites/{SiteKey}/Shared/{{0}}.cshtml", .. ViewLocations];
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

### 5.C Home page as configured sections (code and configuration tiers)

`Index` becomes a loop over the site's sections. Each section is a view component, which is the Razor Pages
native unit for "a partial with its own data loading". Components can be invoked by name, which is what makes
them configurable.

```cshtml
@for (int i = 0; i < Model.Sections.Count; i++)
{
	HomeSection Section = Model.Sections[i];
	<cache vary-by="@($"{Model.SiteId}:{Model.Culture}:{i}")" expires-after="@TimeSpan.FromMinutes(5)">
		@await Component.InvokeAsync(Section.Component, new { Settings = Section.Settings })
	</cache>
}
```

```csharp
/// <summary>
/// One configured home page section: a view component name plus its raw settings, which the component binds to its own typed record.
/// </summary>
/// <param name="Component">The view component name, validated at startup.</param>
/// <param name="Settings">The section's configuration subtree.</param>
public sealed record HomeSection(string Component, IConfigurationSection Settings);

/// <summary>
/// Renders the most recent cover-bearing articles as a grid. Belongs to the Publications pack.
/// </summary>
/// <param name="Context">The request's site- and culture-scoped database context.</param>
public sealed class RecentCoversViewComponent(WikiWikiWorldDbContext Context) : ViewComponent
{
	/// <summary>
	/// Loads recent covers for the current site and culture; the query filters already scope the context.
	/// </summary>
	/// <param name="Settings">The section settings; <c>Count</c> is clamped to 1–100.</param>
	/// <returns>The rendered section.</returns>
	public async Task<IViewComponentResult> InvokeAsync(IConfigurationSection Settings)
	{
		int Count = Math.Clamp(Settings.GetValue("Count", 50), 1, 100);
		IReadOnlyList<ArticleWithCover> Covers = await RecentCoverQuery.LoadAsync(Context, Count, HttpContext.RequestAborted);
		return View(Covers);
	}
}
```

- **Proposed components.** Core: `SiteHero`, `CategoryPills`, `RecentArticles`, `ContentBlock` (renders a
  `site:` article, §5.D) and `QuickLinks`. Publications pack: `RecentCovers`, which is today's
  `IndexModel.OnGetAsync` moved.
- **Default sections for an unconfigured site:** `SiteHero` followed by `RecentArticles`, which is a sensible
  generic wiki home.
- **Category pills, step by step.**
  1. Move the 46 entries into configuration.
  2. Later, take each pill's title from its category article in the current culture. The articles are
     already scoped by site and culture, so this also resolves "titles are English on every culture for now".
  3. As an alternative for small wikis: the top N categories by membership count, once the
     category-membership save integration lands. This needs no curation.
- **Escape hatch.** Override `_HomeBody` through §5.B for a fully bespoke home page.

### 5.D Content-defined site pages, the MediaWiki model (content tier)

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

The tiers work together. Configuration and Razor own the structure, while content owns the prose and the
curation. For example, Magazedia's `HomeSections` could be
`[ContentBlock(site:home-intro), CategoryPills, RecentCovers]`.

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
  `(Site, Kind)`, where `Kind` is `Article`, `SitePage` (adds the widgets) or `Talk` (unchanged and shared).
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

Many "custom data types" are really infoboxes and labels, which a schema can describe without code:

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
| Templates stored in the database (Liquid or Fluid) for admins | Adds a second template language and an HTML-injection risk on each site's own origin. The content tier with directives gives admins power without raw HTML. |
| Razor components through `DynamicComponent` for sections | Workable from Razor Pages, but adds a second component model. View components are native to this app. .NET 11's `CacheView` targets Blazor static SSR, so do not assume it applies inside Razor Pages. |

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
8. Require an administrator role on the `Admin/*` pages.

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

**Phase 3: home sections (medium).** Build the view components from §5.C and move today's home query into
`RecentCovers`. Index becomes the section loop with `<cache>` keyed by site and culture. Startup validation
rejects unknown component names.

**Phase 4: content tier (medium).** Add the `site:` namespace with role-based protection, the `ContentBlock`
component, legal pages served from content, and the widget directives in the `SitePage` pipeline variant.

**Phase 5: as demand appears.** Declarative infobox schemas with aliases, extraction into Razor class
libraries, a theme level, and the matcher-policy spike if it is ever needed.

Order and value: Phases 0 and 1 immediately stop Fanfare and CRTR from shipping as Magazedia. Phases 2 and 3
are where "other wikis get options" becomes real. Phase 4 is what lets non-developers run a wiki.

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

## 8. Decisions needed

1. **Who customizes?** Only you, through code and deploys, or wiki admins without deploys? This decides
   whether Phase 4 is core or optional.
2. **Hosting.** Will every wiki stay co-hosted in the one container, or might a large wiki get a dedicated
   deployment? The plan supports both, but the answer changes how hard to push on library extraction.
3. **Publications.** A reusable pack (recommended), or Magazedia-only?
4. **Per-site admin roles.** Is the `SiteAdmin:{SiteKey}` role-name convention acceptable as a
   no-schema-change measure?
5. **Restart semantics.** Is a restart acceptable for directive and section changes, as it already is for
   stylesheets, with only plain text reloading live?
6. **Site keys and folders.** Rename the CSS override folder from `css/sites/1/` to a key-based path.
7. **Fonts.** Confirm the Lyon licence scope and whether the Typekit kit can or should be added for other
   domains.

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
  immediately, and the library, theme and matcher-policy work waits for a real second customer.
