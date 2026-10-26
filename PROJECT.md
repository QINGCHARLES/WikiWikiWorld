# Project Context: Magazedia

## 🎯 Mission & Goals
- **Objective:** Build the world's largest magazine encyclopedia with a wiki-style platform, in multiple languages, and downloads of magazines where possible.
- **Target Audience:** Magazine enthusiasts, collectors, and historians.

## 🛠 Operational Data (Local Only)
- **Test User:** DevvyTheBot (formerly `llmtest`; the legacy name remains blocked outside Development)
- **Test credentials:** Read `.local/DevvyTheBot.json` in this checkout (Git-ignored). Never copy the password into tracked files or chat. The account is restricted to Development for browser login, API tokens, registration, and session validation.
- **Test URL for English localization:** https://en.localhost:7126/
- **Test URL for multi-language project home page:** https://localhost:7126/
- **Loopback IP URLs:** Development redirects `127.0.0.1` to `en.localhost`, preserving the scheme, port, path, and query. Use the named URL directly for HTTPS certificate validation.
- **Note:** These credentials are blocked from production; use only for local/dev testing. Before entering credentials, ensure that you are using the correct server. Before entering credentials, clear the input fields on the login page to avoid any issues.

## Database maintenance

- Use `deploy/db-update.sh` for host-side schema updates and deliberate data repairs. See [the command, SQL assertion contract, permissions, backups and recovery procedure](doc/database-maintenance.md).
- Prepare and test SQL on disposable data first. Follow production authorization already given; obtain approval only when production changes are not authorized. On failure, diagnose rather than improvising permissions or another execution method. Report the backup location, scripts applied and verification results.
- Schema ownership remains manual SQL. Use application services/API for content edits that require revision creation and category indexing.

## API tokens

- `POST /api/auth/token` exchanges a username and password for a JWT: 24 hours by default, or `ExpiresInDays` (1–365) for a longer one. Each token carries the account's Identity security stamp, and the JWT bearer handler (`OnTokenValidated` in `Program.cs`) rejects it unless the account still exists, is not deleted, and still has that stamp. **To revoke all of an account's tokens, change its security stamp**: a password change does this, as does `UserManager.UpdateSecurityStampAsync`, or a reviewed `UPDATE Users SET SecurityStamp = …` through `deploy/db-update.sh`. Tokens issued before this check existed carry no stamp and are refused. Rotating the signing secret still revokes every token at once.
- `scripts/wiki_api.py` gets, stores and uses tokens under `.local/tokens/` (Git-ignored) without printing them: `token dev` uses DevvyTheBot (24 hours), `token prod --user MaggieTheBot` prompts a person for the password and asks for 180 days (`--days N` to change), `status` shows user and expiry, `request ENV METHOD api/...` calls the API. Write paths without a leading slash (Git Bash rewrites `/api/...`). `WIKI_API_DEV_PORT=7127` targets the preview instance.
- Agents use the production token file but never type production passwords or paste tokens; a person runs `token prod`. Production edits by agents go through the `MaggieTheBot` account so they are attributable and revertible.
- The production signing secret lives only on the host in `/home/deploy/secrets/wikiwikiworld.env` (`JwtSettings__Secret=…`, mode 600) and reaches the container through `--env-file` in `deploy/update.sh`. It is not in any tracked file; the app refuses to start without it. The value previously committed in `appsettings.Production.json` is public and must never be reused. Losing the file only invalidates tokens: generate a new value with `openssl rand -base64 48`.

## URL query-string conventions

- All application-owned query-string parameter names must be lowercase, including generated links, GET forms, redirects, and browser-history updates (for example `?sort=name&pagenumber=3`). This applies to URL parameter names, not C# member names; parameter values retain their required meaning and casing. Preserve externally required integration contracts.
- Existing mixed-case URLs must remain readable when generated URLs move to lowercase. Profile contributions and bookmark links now emit lowercase query parameter names; model binding continues to accept legacy casing.

## Frontend weight and progressive enhancement

- The site must stay very lightweight, especially in JavaScript. Prefer plain HTML and CSS. Use JavaScript judiciously to extend small interactions that need it, such as saving a bookmark in place. Keep native controls (including category disclosures) native.
- Datastar is approved for incremental enhancements, not a broad frontend rewrite. Keep state and rendering on the server using Razor partials; use client signals only for transient interaction state. No frontend build step or additional .NET SDK package is required.
- Datastar v1.0.4 is pinned and self-hosted at `wwwroot/js/vendor/datastar-1.0.4.js` and loaded once, site-wide, by `_Layout.cshtml` (decided 2026-10-24 so every page can use small enhancements; about 13 KB gzip, cached per release). Pages and modules must not load or import it again. Preserve its adjacent license and provenance notes. Do not load a moving CDN version at runtime.
- Bookmarking posts the native form's antiforgery token and patches a Razor partial. The button stays disabled without JavaScript, never navigates, and is disabled while saving. Toggle requests must not automatically retry (both `retry: 'never'` and `retryMaxCount: 0` are required by this pinned version's network-error behavior). An ambiguous failure asks the reader to reload before retrying.
- `HtmlPrettifyMiddleware` bypasses Datastar requests: fragment responses must not acquire full-document wrappers.
- Datastar expressions belong only in trusted application templates. Never interpolate authored text into executable expressions. Because Datastar runs on every page, **any HTML produced by the article Markdown pipeline or `DisplayTitleHelper.RenderHtmlAsync` must sit inside a `data-ignore="true"` boundary**: the article pipeline's generic-attributes syntax (`{data-on:...}`) would otherwise become a live expression. Current boundaries: the article body and title (`Article/View`), the profile article (`Profile/View`), the Links title (`Article/Links`) and the search suggestion fragment. Razor-encoded text and talk posts (no generic attributes) are safe.
- Verify changes in a modern Chromium browser with JavaScript both enabled and disabled, including anonymous and authenticated requests, persistence after reload, network failures, antiforgery rejection, and an unchanged article URL. Other browser support has not yet been formally defined.

## Search

- `Services/ArticleSearchService.cs` serves the `/Search` page and the header typeahead (`/Search?handler=suggestions&q=`, partial `Pages/Shared/_SearchSuggestions.cshtml`, keyboard behaviour in `js/search-dropdown.js`, styles in `css/layout/header.css`). Title matches rank exact, prefix, word start, then substring; within a rank articles precede categories, files and info pages, then shorter titles. Body matches follow (typeahead: 3+ characters, only when the query appears in visible prose). User pages are excluded. `/api/search` is unchanged and unranked.
- Matching is SQLite `LIKE` with escaped `%`, `_` and `\`; case-insensitivity covers ASCII letters only. No search index exists; at the current size a scan is fast. FTS5 is the upgrade path if search becomes slow.
- Without JavaScript the header form submits to `/Search?q=`. The typeahead fetches through Datastar (180 ms debounce, superseded requests cancelled, no retries).

## 🎨 CSS & Responsive Design
- **Header paper texture:** Approved preset (2026-10-21): folds 0.0, wrinkles 0.3, crumples 0.3, drops 0.0. CMYK was rejected and removed. A single static shader specializes disabled folds, drops and roughness stripes as compile-time constants; original images are preserved. See [settings, verification and reversal instructions](doc/header-paper-texture.md). The pinned self-hosted shader loads only for enabled headers.
- **Stylesheet loading:** `SiteStylesheetCatalog` builds an ordered snapshot at startup for the site IDs in `SiteConfiguration:Sites`. Shared defaults live in `src/WikiWikiWorld.Web/wwwroot/css/`; optional overrides mirror their relative paths under `wwwroot/css/sites/<SiteId>/` (for example `sites/1/core/variables.css`). The shared layout emits all defaults first, then existing overrides in the same relative order, with cache-versioned URLs. Sites without overrides use defaults. No cascade layers are introduced. Adding/removing override files or changing configured sites requires an app restart. Keep the catalog's ordered list updated when adding a new shared stylesheet. Files ship with the application; they are not stored in the persistent data directory. The standalone culture selector retains its own styling.
- **Home category pills:** The curated list (title + fixed slug) lives in `IndexModel.Categories`; styles in `css/pages/home.css`. From 32rem container width up, the strip clamps to two wrapped rows and a native `<details>` toggle expands it; no JavaScript. Below 32rem it is always one swipeable row with no toggle, because expanding all pills on a phone buries the page (a deliberate container-query exception to the rule below). `/category:<slug>` pages do not exist yet.
- **Avoid `@media` Breakpoints:** Avoid CSS `@media` width breakpoints whenever possible to keep the site continuously and fluidly responsive across all viewport sizes. Prefer intrinsic sizing and fluid CSS math functions (`clamp()`, `min()`, `max()`, CSS Grid `auto-fit`/`auto-fill`, Flexbox wrapping, and logical properties) so layouts, spacing, typography, and visual effects adapt smoothly without stepped breakpoint jumps.

## Category membership storage

- Schema, EF mapping, save-time indexing, the `backfill-categories` maintenance command, and `{{CategoryArticles}}` are implemented for revision-owned `ArticleCategoryMemberships`. Category slugs are immutable identities, independent of display names. The listing uses indexed membership lookups with 100 links per page. External SQL writers bypass indexing and require a subsequent backfill. See [category list authoring](doc/category-articles.md).
- Existing databases require an explicitly applied additive SQL script; there are no automatic startup migrations. See [schema, application and verification procedure](doc/category-membership-schema.md).

## Article languages

- **Page:** `/{slug}/languages` (the "Other languages" item in the article menu) lists an article's translations; anyone can read it, signed-in users can link, change and remove. Plain POST/redirect forms, no JavaScript. Code: `Pages/Article/Languages.cshtml(.cs)`, `Services/ArticleLanguageService.cs`, `css/pages/languages.css`.
- **Model:** `ArticleCultureLinks` rows sharing an `ArticleCultureLinkGroupId` form one group of translations. Groups are transitive (every member lists every other), hold at most one article per culture (service-enforced), and an article is in at most one active group (unique partial index). Links only join articles of the same `ArticleType` within one site. Removals and moves are soft-deletes recording `DeletedByUserId`; a group left with one member is dissolved.
- **Merging:** linking two articles that are already in different groups merges the groups after a confirmation listing every member. A lone (unlinked) article counts as a group of one, so linking into a group that already has its language goes through the same confirmation, naming the article it displaces. Where both groups have an article in the same language, the editor must choose which stays (never automatic); the two articles being linked always stay. Change refuses a replacement that already has translations, so it never merges silently.
- **Languages offered** are the culture subdomains in `SiteConfiguration:Sites[].Domains` plus `DefaultCulture` (`Helpers/SiteCultureHelper.cs`, shared with the culture selector). Development lists `ja`, `ar` and `xx-test.localhost` so they can be linked and served locally.
- **Schema:** existing databases need `SqliteScripts/AddArticleCultureLinkIndexes.sql`, applied once after a consistent backup (it contains a pre-check query). Applied to the local dev `data/app.db` and to production on 2026-10-24 (release 4e8b83c).

## Contributor profiles

- **Pages:** `/@user/profile` (overview), `/@user/contributions` (paged with `?pagenumber=`, 50 per page), `/@user/bookmarks/{listid?}` (owner lists or public visitor lists), and the owner-only `/@user/messages` and `/@user/settings` share `Pages/Profile/_ProfileHeader.cshtml` (identity bar and tabs) and `css/pages/profile.css`. `js/profile.js` only adds the Share button (native share sheet or copy link; hidden without JavaScript) and centres the current tab on narrow screens.
- **Bookmark lists:** Owners can create empty lists, choose public/private visibility, remove an entry with a five-minute protected undo, delete custom lists, and clear the permanent default list. Existing and new lists default to private. Public lists are read-only for visitors; private names/counts never enter visitor responses. See [behavior, schema upgrade, and verification](doc/bookmark-lists.md).
- **Bookmark pagination:** Datastar patches the selected list and pagination controls at 50 entries per page. Recently saved is the default; name ordering uses invariant Unicode case-insensitive comparison in SQLite, then membership ID. Lowercase `sort` and `pagenumber` encode the view; a successful patch updates browser history. Back/Forward, direct visits, reloads, shared links, and ordinary navigation without JavaScript retain the same state. Failed fetches retain the current URL/results; a failed Back/Forward fetch reloads that URL normally. List-management mutations use antiforgery-protected POST forms and redirects.
- **Settings:** Theme preferences remain device/browser cookies. The profile settings URL checks ownership on GET and POST. Legacy `/settings/theme:` and `/Settings/Theme` redirect signed-in readers to their settings; anonymous readers can still choose a device theme there. Saving returns to settings with confirmation.
- **Data:** `ProfileService` loads everything through the context's site and culture filters. Contribution figures, the calendar and the edit list leave out user pages (`ArticleType.User`), because registration auto-creates one. An article counts as "started" when the user wrote the revision with its earliest timestamp (identifiers are not trusted for ordering). The auto-created placeholder text (`ArticleRevisionService.GetDefaultUserArticleText`) counts as an unwritten profile article. Accounts with the legacy default `DateCreated` (registration did not set it before 2026-10-23) show their first revision as the join date.
- **Profile header:** the profile article's `{{ShortDescription}}` supplies the one-line bio. The decorative cover banner, volume numeral, epigraph, barcode, and headline contribution totals are not displayed.
- **Badges and ranks** are computed per request from the totals in `ProfileBadgeCatalog`; there is no schema. Changing a threshold changes every profile retroactively.
- **Previews (no storage yet):** the Reputation card, the Watchlist tab and the owner-only watchlist alerts card are labelled "Coming soon" or "Preview" and never show invented numbers. Replace them when those features have data.
- **Performance:** finding each article's first revision groups `ArticleRevisions` by `CanonicalArticleId`, which has no index, so the profile header and each contributions page scan the table once. If profiles become slow, the optional additive index is `CREATE INDEX IX_ArticleRevisions_CanonicalArticleId_DateCreated ON ArticleRevisions (CanonicalArticleId, DateCreated);`. It has not been applied; follow the explicit schema procedure if adopted.
- **Verification:** `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~ProfileTests` (calendar, badge and formatting rules, plus the queries against SQLite).

## 🧩 Markdig Extensions

Custom Markdig extensions live in [src/WikiWikiWorld.Web/MarkDigExtensions](src/WikiWikiWorld.Web/MarkDigExtensions). They extend authoring syntax for the wiki and follow shared conventions so authors and parsers stay consistent.

### Shared Syntax Conventions

| Token | Meaning |
| --- | --- |
| `{{` … `}}` | Delimits all custom tags. Always open with `{{` and close with `}}`. The opening character `{` is registered with the parser. |
| `{{Name}}` | **Bare marker.** Used for self-contained directives that take no arguments (e.g. `{{Stub}}`, `{{Footnotes}}`, `{{Categories}}`, `{{Citations}}`). |
| `{{Name VALUE}}` | **Single-value form.** A single space separates the directive name from its value (e.g. `{{Subtitle My subtitle}}`, `{{Eyebrow MAGAZINE}}`, `{{HeaderImage some-slug}}`). |
| `\|#\|` | **Property separator.** Splits multiple `Key=Value` tokens inside a single item. Chosen because it almost never appears in natural prose. Used by `Citation`, `PullQuote`, `Source`, `Image`, `PublicationIssueInfobox`, `CoverCarousel`, `Timeline`, `YouTube`. |
| `\|~\|` | **Item separator.** Splits a payload that contains *multiple repeating items*, where each item itself has properties separated by `\|#\|`. Used by `CoverCarousel` to delimit individual covers, and by `Timeline` to delimit timeline event milestones. Use this whenever a directive needs a list of structured entries rather than a flat property bag. |
| `Key=Value` | **Property assignment.** Each token between `\|#\|` separators is a `Key=Value` pair. Keys are matched case-insensitively. Duplicate keys accumulate into a list (e.g. multiple `author=` entries in a citation, multiple fact entries in an infobox). |
| `file:slug` | **File-reference prefix.** Many directives that point to a file article (`HeaderImage`, `Image`, `CoverCarousel.CoverImage`) accept either a bare slug or a `file:`-prefixed slug; the prefix is stripped during enrichment. |
| `{{Name` … multi-line … `}}` | **Multi-line block.** A few block directives (`CoverGrid`, `PublicationIssueInfobox`) allow content to span multiple lines until the closing `}}` is found. |
| Pattern | Tag name is **PascalCase** (e.g. `ShortDescription`, `DownloadsBox`, `PullQuote`). Property keys inside the payload are also PascalCase (`CoverImage`, `Layout`, `Caption`) except for bibliographic citation keys which are lowercase (`author`, `title`, `journal`, `doi`). |

### Architectural Patterns

- **Three-class pattern.** Each extension defines a `…Block` / `…Inline` (AST node), a `…Parser` (consumes the syntax), a `…Renderer` (writes HTML), and an `…Extension` (`IMarkdownExtension`) that wires them into the pipeline. `OpeningCharacters = ['{']` is the standard parser trigger.
- **Block vs Inline.** Use a `BlockParser` when the tag occupies its own line/paragraph (`HeaderImage`, `Subtitle`, `Stub`, `Footnotes`, `DownloadsBox`, `CoverGrid`, `PublicationIssueInfobox`, `Citations`, `Categories`, `ShortDescription`, `Source`, `Eyebrow`, `YouTube`). Use an `InlineParser` when the tag appears inside prose (`Citation`, `Footnote`, `Category`, `Image`, `PullQuote`).
- **`Enrich` / `EnrichAsync`.** Extensions that need data outside the markdown source expose a static enrichment method on the extension class. Synchronous `Enrich(MarkdownDocument)` is used to extract metadata (Subtitle, ShortDescription, Eyebrow, Source, Stub). Asynchronous `EnrichAsync(MarkdownDocument, WikiWikiWorldDbContext, int SiteId, string Culture, CancellationToken)` is used to batch-fetch DB rows (HeaderImage, Image, DownloadsBox, CoverGrid, CoverCarousel). Enrichment is run from the page model **between** `Markdown.Parse(...)` and `renderer.Render(...)`.
- **Document metadata channel.** Metadata-only extensions write into `Document.SetData(Key, value)` so Razor views can read it via `Document.GetData(Key)` (header image URL, subtitle text, short description, eyebrow label, source citation, stub site name/edit URL). The block renderers themselves emit only `<!-- ... -->` comments.
- **Citation/Footnote two-pass.** Inline parsers create unnumbered AST nodes; `CitationExtension.ReprocessCitations(...)` and `FootnoteExtension.ReprocessFootnotes(..., Pipeline)` walk the document later to assign sequential numbers and populate the corresponding `{{Citations}}` / `{{Footnotes}}` block.
- **External links.** `ApprovedNoFollowLinksExtension` post-processes all `LinkInline` nodes (via `Pipeline.DocumentProcessed`) and applies `rel="noopener"` to every external link, adding `nofollow` unless the domain is in the approved list.

### Extension Reference

#### Metadata-only (no visible HTML; consumed by Razor views via `Document.GetData(...)`)

- **`{{Subtitle Your subtitle text}}`** — Sets the subtitle shown beneath the article title. First occurrence wins. Metadata key: `Subtitle`.
- **`{{ShortDescription One-line description}}`** — Short description used for SEO/meta tags. First occurrence wins. Metadata key: `ShortDescription`.
- **`{{Eyebrow TYPE}}`** — Label shown above the title. `TYPE` is case-insensitive and must be `MAGAZINE`, `MAGAZINEISSUE`, or `MAGAZINEFRANCHISE`. Unknown types are silently consumed. Metadata key: `Eyebrow`.
- **`{{Source Description |#| https://url}}`** — Cites the origin of a file/image. Three variants: text only (`{{Source Original print scan}}`), URL only (`{{Source https://example.com/file.jpg}}`), or text + URL via the `|#|` separator. First occurrence wins. Metadata keys: `SourceText`, `SourceUrl`.
- **`{{HeaderImage slug-of-file-article}}`** or **`{{HeaderImage file:slug}}`** — Sets the page hero image by referencing a file article. `EnrichAsync` batch-resolves the slug → file URL via EF and stores it in metadata key `HeaderImage`. The first resolved image wins. Optional **`|#| Focus=top|center|centre|bottom`** sets which part stays in view when the hero frame crops the image vertically (default `top`; unknown values are ignored). It is stored as `HeaderImageFocus`, emitted as `data-focus` on `img.hero-image`, applied by `object-position` in `components/media.css`, and mirrored as the paper-texture shader's `u_originY` in `js/header-paper-texture.js` so both layers crop identically.

#### Article-structure blocks (emit visible HTML)

- **`{{Stub}}`** — Renders a stub-notice banner at the location of the tag. Authors place it at the end of an incomplete article. Call `StubExtension.Enrich(Document, SiteName, EditUrl)` from the page model so the banner can link to the editor.
- **`{{Footnote Inline footnote body, may contain *markdown* and links.}}`** — Inline superscript reference. Pair with a single **`{{Footnotes}}`** block (typically near the end of the article); `FootnoteExtension.ReprocessFootnotes(Document, Pipeline)` numbers them and parses each footnote body using the shared pipeline.
- **`{{Citation author=Doe, J. |#| title=A Paper |#| journal=Nature |#| year=2024 |#| doi=10.1000/xyz |#| url=https://...}}`** — Inline citation marker rendered as `†N`. Pair with a single **`{{Citations}}`** block; `CitationExtension.ReprocessCitations(Document)` aggregates them into a Wikipedia-style bibliography. Recognised keys: `author` (repeatable), `title`, `journal`, `publisher`, `volume`, `issue`, `pages`, `year`, `doi`, `url`. Identical citation content de-duplicates to the same numbered entry.
- **`{{Category Some Category}}`** — Inline category reference using a Django-based native-script slug derivation that preserves combining marks. **`{{Category Display label|#|explicit-slug}}`** supplies the immutable identity independently of the label. Invalid references stay visible. Pair with **`{{Categories}}`** to render category links. See [syntax, Unicode rules, and required compatibility audit before deployment/backfill](doc/category-slugs.md).
- **`{{PullQuote The quote text |#| Attribution line}}`** — Inline `<blockquote class="pullquote">`. The `|#| Attribution` part is optional (a new line starting with `—` also works). Surrounding double-quotes on the quote text are stripped automatically. `**bold**` words render in the wide Archivo grotesque in caps for emphasis.
- **`{{Image url-slug |#| Layout=full|breakout|max |#| Alt=Alt text |#| Caption=Caption}}`** or bare **`{{Image url-slug}}`** — Embeds a wiki file article as an `<img>`. Inside the `|#|` payload, individual attributes are separated by `|` (not `|#|`) — this is a documented exception. Supported attributes: `Layout` (one of `full`, `breakout`, `max` → CSS class), `Alt`, `Caption`. `EnrichAsync` batch-resolves the slug → file URL.
- **`{{YouTube VIDEO_REFERENCE |#| Title=Accessible video title |#| StartSeconds=90 |#| Caption=Optional plain-text caption}}`** — Renders a local, keyboard-accessible click-to-load placeholder. `VIDEO_REFERENCE` may be an 11-character video ID or an allowlisted YouTube watch, shortened, Shorts, or embed URL. `Title` is required; `StartSeconds` and `Caption` are optional. Pasted `t`/`start` timestamps are recognised, with `StartSeconds` taking precedence. YouTube is not contacted until the reader activates the placeholder, at which point a privacy-enhanced `youtube-nocookie.com` player replaces it.
- **`{{DownloadsBox <SHA256-hash>}}`** — Renders an `<aside class="downloads-box">` with a file's name, size, and download button. The hash matches a `DownloadUrl` row keyed by `HashSha256`. `EnrichAsync` batch-loads the matching `DownloadUrl` rows.

#### Container blocks (multi-line payload until closing `}}`)

- **`{{IssueContents Group=Features |#| Featured=true |~| Title=Feature title |#| Description=Optional copy |#| Credit=Optional attribution |#| Pages=42–49}}`** — Manually authored, non-linking issue table of contents. Only entry `Title` is required; groups are optional and preserve source order. Featured groups span the available width; regular groups flow into intrinsic columns. All values are escaped plain text. Invalid source stays visible. Styled by `components/issue-contents.css`. See [authoring guide and LLM transcription prompt](doc/issue-contents.md). Verification: `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~IssueContents`.

- **`{{IssueCredits Group=Editor-in-Chief |#| Featured=true |~| Name=[Anna Wintour](/anna-wintour) |~| Group=Photography |#| Open=true |~| Role=Cover |#| Name=Example Person}}`** — Author-defined credits groups. Group headers are separate items from role/name entries; repeated `Name=` properties produce separate list items. Featured groups are non-collapsible; other groups default closed with an `Open=true` override. Native `details`/`summary` provides keyboard and touch operation without JavaScript. Styled by `components/issue-credits.css` (dark floated card; featured groups as masthead headlines; two-column masthead when the card is at least 26rem wide via a container query). See [authoring guide](doc/issue-credits.md) and [designer contract](doc/issue-credits-designer-handoff.md). Verification: `dotnet test tests/WikiWikiWorld.Web.Tests -c Release --filter FullyQualifiedName~IssueCredits`. The focused documentation publisher uses a transaction and preserves existing revisions.
- **`{{CoverGrid`** *(newline-separated slugs on subsequent lines)* **`}}`** — Renders a responsive grid of magazine covers. Each line inside the block is one publication-issue article slug. `EnrichAsync` resolves each slug → its `{{CoverCarousel}}` → its first `CoverImage` slug (with a legacy infobox fallback) → the file. Missing entries fall back to `cover-missing.png`.
- **`{{PublicationIssueInfobox CoverDate=2024-03 |#| CoverPrice=$5.99 |#| PublicationFormats=Print, Digital |#| ISSN=1234-5678 |#| ...}}`** — Renders an `<aside class="infobox">` containing publication facts as a `<dl>`. Known keys receive friendly display labels; unknown keys render as-is. Repeated facts accumulate. Legacy `CoverImage` keys are ignored; move covers into a separate `{{CoverCarousel}}` block, separating covers with `|~|`.
- **`{{CoverCarousel CoverImage=slug-a |#| AltText=Front cover |#| Caption=Issue #1 |~| CoverImage=slug-b |#| AltText=Back cover}}`** — Renders a standalone cover carousel as an `<aside class="cover-carousel">` (matching the `<aside>` semantics of `PublicationIssueInfobox`) containing the image(s) and a `<div class="dots">` page-position row — no infobox property list. Uses **two-level separation**: `|~|` splits one cover entry from the next, and `|#|` splits the `CoverImage=` / `AltText=` / `Caption=` properties *within* a single cover. The `file:` slug prefix is accepted on `CoverImage=`. `AltText` is written to the `<img alt>` attribute (falls back to the cover article's title). **`Caption` is currently parsed and stored but not rendered** — it is reserved for a future caption strip beneath each slide. An item without a `CoverImage=` is silently skipped. A single resolved cover renders as `<aside class="cover-carousel single"><img></aside>` with no carousel chrome; multiple covers render with prev/next nav buttons and the dots indicator inside the `<aside>`.
- **`{{Timeline Title=... |#| Date=... |#| Description=... |~| Title=...}}`** — Renders a vertical chronological timeline as an `<ol class="timeline">` with circular hollow node markers and vertical dotted connector lines. Uses **two-level separation**: `|~|` separates repeating milestone events, and `|#|` separates properties within each event. Supported properties: `Title=` (**Required**, event heading), `Date=` (*Optional*, date/timeframe rendered in italics), `Description=` (*Optional*, narrative text supporting Markdown formatting such as bold, italics, and links).

#### Post-processing (not author-facing)

- **`ApprovedNoFollowLinksExtension(IReadOnlyList<string> ApprovedDomains)`** — Registered as a pipeline-level post-processor. For every external `<a>` (URL starting with `http://`, `https://`, or `//`), adds `rel="noopener"`; adds `nofollow` unless the link's host matches an approved domain. Images are skipped.

### Authoring Pattern Examples

```markdown
{{Eyebrow MAGAZINEISSUE}}
{{Subtitle The issue that launched a thousand cosplays}}
{{ShortDescription Wired Magazine, Issue #1, January 1993.}}
{{HeaderImage file:wired-issue-1-cover}}

{{CoverCarousel CoverImage=file:wired-issue-1-cover |~| CoverImage=file:wired-issue-1-back}}

{{PublicationIssueInfobox
CoverDate=1993-01 |#|
CoverPrice=$4.95 |#|
PublicationFormats=Print |#|
ISSN=1059-1028
}}

Body copy here with a footnote{{Footnote See *the* docs for details.}} and a
citation{{Citation author=Smith, J. |#| title=Founding Wired |#| year=1993}}.

{{Timeline
Title=Launch under Rodale Press |#| Date=1985 — 1986 |#| Description=*American Woodworker* was launched by **Rodale Press** (based in Emmaus, PA).
|~|
Title=Acquisition by Reader's Digest |#| Date=October 1998 |#| Description=Rodale sold its American Woodworking Group to **The Reader's Digest Association**.
}}

{{PullQuote "We are at ground zero of a worldwide revolution." |#| Louis Rossetto}}

{{Image wired-1-spread |#| Layout=breakout |#| Caption=The opening spread}}

{{YouTube https://youtu.be/M7lc1UVf-VE |#| Title=YouTube player demonstration |#| StartSeconds=90 |#| Caption=Demonstration of the embedded player}}

{{DownloadsBox a1b2c3d4e5f6...}}

{{Category Technology Magazines}}
{{Category 1993 Publications}}

{{Footnotes}}
{{Citations}}
{{Categories}}

{{Stub}}
```
