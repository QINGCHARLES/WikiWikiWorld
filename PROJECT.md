# Project Context: Magazedia

## 🎯 Mission & Goals
- **Objective:** Build the world's largest magazine encyclopedia with a wiki-style platform, in multiple languages, and downloads of magazines where possible.
- **Target Audience:** Magazine enthusiasts, collectors, and historians.

## 🛠 Operational Data (Local Only)
- **Test User:** DevvyTheBot (formerly `llmtest`; the legacy name remains blocked outside Development)
- **Test credentials:** Read `.local/DevvyTheBot.json` in this checkout (Git-ignored). Never copy the password into tracked files or chat. The account is restricted to Development for browser login, API tokens, registration, and session validation.
- **Test URL for English localization:** https://en.localhost:7126/
- **Test URL for multi-language project home page:** https://localhost:7126/
- **Note:** These credentials are blocked from production; use only for local/dev testing. Before entering credentials, ensure that you are using the correct server. Before entering credentials, clear the input fields on the login page to avoid any issues.

## Frontend weight and progressive enhancement

- The site must stay very lightweight, especially in JavaScript. Prefer plain HTML and CSS. Use JavaScript judiciously to extend small interactions that need it, such as saving a bookmark in place. Keep native controls (including category disclosures) native.
- Datastar is approved for incremental enhancements, not a broad frontend rewrite. Keep state and rendering on the server using Razor partials; use client signals only for transient interaction state. No frontend build step or additional .NET SDK package is required.
- Datastar v1.0.4 is pinned and self-hosted at `wwwroot/js/vendor/datastar-1.0.4.js`, loaded only by article views. Preserve its adjacent license and provenance notes. Do not load a moving CDN version at runtime.
- Bookmarking posts the native form's antiforgery token and patches a Razor partial. The button stays disabled without JavaScript, never navigates, and is disabled while saving. Toggle requests must not automatically retry (both `retry: 'never'` and `retryMaxCount: 0` are required by this pinned version's network-error behavior). An ambiguous failure asks the reader to reload before retrying.
- `HtmlPrettifyMiddleware` bypasses Datastar requests: fragment responses must not acquire full-document wrappers.
- Datastar expressions belong only in trusted application templates. Wiki-authored title/body HTML is inside `data-ignore="true"` boundaries. Never interpolate authored text into executable expressions.
- Verify changes in a modern Chromium browser with JavaScript both enabled and disabled, including anonymous and authenticated requests, persistence after reload, network failures, antiforgery rejection, and an unchanged article URL. Other browser support has not yet been formally defined.

## 🎨 CSS & Responsive Design
- **Header paper texture:** Approved preset (2026-10-21): folds 0.0, wrinkles 0.3, crumples 0.3, drops 0.0. CMYK was rejected and removed. A single static shader specializes disabled folds, drops and roughness stripes as compile-time constants; original images are preserved. See [settings, verification and reversal instructions](doc/header-paper-texture.md). The pinned self-hosted shader loads only for enabled headers.
- **Stylesheet loading:** `SiteStylesheetCatalog` builds an ordered snapshot at startup for the site IDs in `SiteConfiguration:Sites`. Shared defaults live in `src/WikiWikiWorld.Web/wwwroot/css/`; optional overrides mirror their relative paths under `wwwroot/css/sites/<SiteId>/` (for example `sites/1/core/variables.css`). The shared layout emits all defaults first, then existing overrides in the same relative order, with cache-versioned URLs. Sites without overrides use defaults. No cascade layers are introduced. Adding/removing override files or changing configured sites requires an app restart. Keep the catalog's ordered list updated when adding a new shared stylesheet. Files ship with the application; they are not stored in the persistent data directory. The standalone culture selector retains its own styling.
- **Home category pills:** The curated list (title + fixed slug) lives in `IndexModel.Categories`; styles in `css/pages/home.css`. From 32rem container width up, the strip clamps to two wrapped rows and a native `<details>` toggle expands it; no JavaScript. Below 32rem it is always one swipeable row with no toggle, because expanding all pills on a phone buries the page (a deliberate container-query exception to the rule below). `/category:<slug>` pages do not exist yet.
- **Avoid `@media` Breakpoints:** Avoid CSS `@media` width breakpoints whenever possible to keep the site continuously and fluidly responsive across all viewport sizes. Prefer intrinsic sizing and fluid CSS math functions (`clamp()`, `min()`, `max()`, CSS Grid `auto-fit`/`auto-fill`, Flexbox wrapping, and logical properties) so layouts, spacing, typography, and visual effects adapt smoothly without stepped breakpoint jumps.

## Category membership storage

- Schema, EF mapping, save-time indexing, the `backfill-categories` maintenance command, and `{{CategoryArticles}}` are implemented for revision-owned `ArticleCategoryMemberships`. Category slugs are immutable identities, independent of display names. The listing uses indexed membership lookups with 100 links per page. External SQL writers bypass indexing and require a subsequent backfill. See [category list authoring](doc/category-articles.md).
- Existing databases require an explicitly applied additive SQL script; there are no automatic startup migrations. See [schema, application and verification procedure](doc/category-membership-schema.md).

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
- **`{{HeaderImage slug-of-file-article}}`** or **`{{HeaderImage file:slug}}`** — Sets the page hero image by referencing a file article. `EnrichAsync` batch-resolves the slug → file URL via EF and stores it in metadata key `HeaderImage`. The first resolved image wins.

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
