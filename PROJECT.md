# Project Context: Magazedia

## 🎯 Mission & Goals
- **Objective:** Build the world's largest magazine encyclopedia with a wiki-style platform, in multiple languages, and downloads of magazines where possible.
- **Target Audience:** Magazine enthusiasts, collectors, and historians.

## 🛠 Operational Data (Local Only)
- **Test User:** llmtest
- **Test Pass:** @@@Llm@@@000
- **Test URL for English localization:** https://en.localhost:7126/
- **Test URL for multi-language project home page:** https://localhost:7126/
- **Note:** These credentials are blocked from production; use only for local/dev testing. Before entering credentials, ensure that you are using the correct server. Before entering credentials, clear the input fields on the login page to avoid any issues.

## 🎨 CSS & Responsive Design
- **Avoid `@media` Breakpoints:** Avoid CSS `@media` width breakpoints whenever possible to keep the site continuously and fluidly responsive across all viewport sizes. Prefer intrinsic sizing and fluid CSS math functions (`clamp()`, `min()`, `max()`, CSS Grid `auto-fit`/`auto-fill`, Flexbox wrapping, and logical properties) so layouts, spacing, typography, and visual effects adapt smoothly without stepped breakpoint jumps.

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
| `Key=Value` | **Property assignment.** Each token between `\|#\|` separators is a `Key=Value` pair. Keys are matched case-insensitively. Duplicate keys accumulate into a list (e.g. multiple `author=` entries in a citation, multiple `CoverImage=` entries in an infobox). |
| `file:slug` | **File-reference prefix.** Many directives that point to a file article (`HeaderImage`, `Image`, `PublicationIssueInfobox.CoverImage`) accept either a bare slug or a `file:`-prefixed slug; the prefix is stripped during enrichment. |
| `{{Name` … multi-line … `}}` | **Multi-line block.** A few block directives (`CoverGrid`, `PublicationIssueInfobox`) allow content to span multiple lines until the closing `}}` is found. |
| Pattern | Tag name is **PascalCase** (e.g. `ShortDescription`, `DownloadsBox`, `PullQuote`). Property keys inside the payload are also PascalCase (`CoverImage`, `Layout`, `Caption`) except for bibliographic citation keys which are lowercase (`author`, `title`, `journal`, `doi`). |

### Architectural Patterns

- **Three-class pattern.** Each extension defines a `…Block` / `…Inline` (AST node), a `…Parser` (consumes the syntax), a `…Renderer` (writes HTML), and an `…Extension` (`IMarkdownExtension`) that wires them into the pipeline. `OpeningCharacters = ['{']` is the standard parser trigger.
- **Block vs Inline.** Use a `BlockParser` when the tag occupies its own line/paragraph (`HeaderImage`, `Subtitle`, `Stub`, `Footnotes`, `DownloadsBox`, `CoverGrid`, `PublicationIssueInfobox`, `Citations`, `Categories`, `ShortDescription`, `Source`, `Eyebrow`, `YouTube`). Use an `InlineParser` when the tag appears inside prose (`Citation`, `Footnote`, `Category`, `Image`, `PullQuote`).
- **`Enrich` / `EnrichAsync`.** Extensions that need data outside the markdown source expose a static enrichment method on the extension class. Synchronous `Enrich(MarkdownDocument)` is used to extract metadata (Subtitle, ShortDescription, Eyebrow, Source, Stub). Asynchronous `EnrichAsync(MarkdownDocument, WikiWikiWorldDbContext, int SiteId, string Culture, CancellationToken)` is used to batch-fetch DB rows (HeaderImage, Image, DownloadsBox, CoverGrid, PublicationIssueInfobox). Enrichment is run from the page model **between** `Markdown.Parse(...)` and `renderer.Render(...)`.
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
- **`{{Category Some Category}}`** — Inline marker that tags the article with a category. Renders nothing inline. Pair with a single **`{{Categories}}`** block to render the collected categories as a button list linking to `/category:<slug>`.
- **`{{PullQuote The quote text |#| Attribution line}}`** — Inline `<blockquote class="pullquote">`. The `|#| Attribution` part is optional. Surrounding double-quotes on the quote text are stripped automatically.
- **`{{Image url-slug |#| Layout=full|breakout|max |#| Alt=Alt text |#| Caption=Caption}}`** or bare **`{{Image url-slug}}`** — Embeds a wiki file article as an `<img>`. Inside the `|#|` payload, individual attributes are separated by `|` (not `|#|`) — this is a documented exception. Supported attributes: `Layout` (one of `full`, `breakout`, `max` → CSS class), `Alt`, `Caption`. `EnrichAsync` batch-resolves the slug → file URL.
- **`{{YouTube VIDEO_REFERENCE |#| Title=Accessible video title |#| StartSeconds=90 |#| Caption=Optional plain-text caption}}`** — Renders a local, keyboard-accessible click-to-load placeholder. `VIDEO_REFERENCE` may be an 11-character video ID or an allowlisted YouTube watch, shortened, Shorts, or embed URL. `Title` is required; `StartSeconds` and `Caption` are optional. Pasted `t`/`start` timestamps are recognised, with `StartSeconds` taking precedence. YouTube is not contacted until the reader activates the placeholder, at which point a privacy-enhanced `youtube-nocookie.com` player replaces it.
- **`{{DownloadsBox <SHA256-hash>}}`** — Renders an `<aside class="downloads-box">` with a file's name, size, and download button. The hash matches a `DownloadUrl` row keyed by `HashSha256`. `EnrichAsync` batch-loads the matching `DownloadUrl` rows.

#### Container blocks (multi-line payload until closing `}}`)

- **`{{CoverGrid`** *(newline-separated slugs on subsequent lines)* **`}}`** — Renders a responsive grid of magazine covers. Each line inside the block is one publication-issue article slug. `EnrichAsync` resolves each slug → its `{{PublicationIssueInfobox}}` → its `CoverImage` slug → the file. Missing entries fall back to `cover-missing.png`.
- **`{{PublicationIssueInfobox CoverImage=slug-a |#| CoverImage=slug-b |#| CoverDate=2024-03 |#| CoverPrice=$5.99 |#| PublicationFormats=Print, Digital |#| ISSN=1234-5678 |#| ...}}`** — Renders an `<aside class="infobox">` with a cover-image carousel (multiple `CoverImage=` keys become slides) and a `<dl>` of all other properties. Friendly display names are mapped for known keys (`CoverPrice` → "Cover Price", `PrintEAN-13` → "Print EAN-13", etc.); unknown keys are rendered as-is. Properties are case-insensitive and accumulate when repeated. `EnrichAsync` resolves each `CoverImage=` slug to a file URL.
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

{{PublicationIssueInfobox
CoverImage=file:wired-issue-1-cover |#|
CoverImage=file:wired-issue-1-back |#|
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
