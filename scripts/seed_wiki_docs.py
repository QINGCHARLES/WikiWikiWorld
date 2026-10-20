import sqlite3
import uuid
from datetime import datetime, timezone
import os
from pathlib import Path

DB_PATH = os.environ.get("DB_PATH", os.path.join(os.path.dirname(__file__), "..", "data", "app.db"))
USER_ID = "7240be61-df81-46f9-8152-6a48b96abc40"  # QINGCHARLES
SITE_ID = 1
CULTURE = "en"
TYPE_INFO = 4  # ArticleType.Info

ARTICLES = [
    {
        "slug": "wiki:issue-credits",
        "title": "Wiki: Issue Credits Directive",
        "display_title": "Wiki: Issue Credits Directive",
        "text": (Path(__file__).resolve().parents[1] / "doc" / "issue-credits.md").read_text(encoding="utf-8"),
    },
    {
        "slug": "wiki:formatting",
        "title": "Wiki: Formatting Guide",
        "display_title": "Wiki: Formatting & Extensions Guide",
        "text": """This guide documents the formatting features and wikitext extensions available on Magazedia. Articles are authored in CommonMark Markdown, enriched with GitHub-style alerts, pipe tables, auto-linked headings, and custom `{{...}}` wikitext directives designed for magazine archiving and documentation.

## Directive Syntax Conventions

Magazedia wikitext directives follow consistent syntax patterns:

| Syntax Pattern | Meaning | Example |
| :--- | :--- | :--- |
| `{{Name}}` | Parameterless marker | `{{Stub}}`, `{{Footnotes}}`, `{{Citations}}`, `{{Categories}}` |
| `{{Name VALUE}}` | Single positional parameter separated by space | `{{Subtitle ...}}`, `{{Eyebrow ...}}`, `{{ShortDescription ...}}`, `{{HeaderImage ...}}` |
| `|#|` | Separates properties within a single item | `{{Citation author=... |#| title=...}}` |
| `|~|` | Separates repeating items in a container block | `{{Timeline Title=... |~| Title=...}}` |
| `Key=Value` | Named property assignment (case-insensitive keys) | `Layout=breakout`, `StartSeconds=30` |
| `file:slug` | References a media/file article | `{{HeaderImage file:wired-issue-1-cover}}` |

> [!NOTE]
> Directive names are case-sensitive. Unless explicitly documented, directive values are plain text; unescaped delimiter strings `|#|`, `|~|`, and `}}` terminate values and cannot be escaped.

---

## Extension Catalog

Explore the dedicated documentation for each extension:

### Header & Metadata Directives
Directives placed near the top of an article to define page metadata and headings:

* [**Eyebrow Label** `{{Eyebrow}}`](/wiki:eyebrow) — Displays a classification badge above the title (`MAGAZINE`, `MAGAZINEISSUE`, `MAGAZINEFRANCHISE`).
* [**Subtitle** `{{Subtitle}}`](/wiki:subtitle) — Adds an explanatory subtitle beneath the main title heading.
* [**Short Description** `{{ShortDescription}}`](/wiki:short-description) — Summary snippet used for search engines and social cards.
* [**Header Hero Image** `{{HeaderImage}}`](/wiki:header-image) — Sets the full-width hero header image referencing a file article.
* [**Source Attribution** `{{Source}}`](/wiki:source) — Records the origin, scanner, or archive URL for media file articles.

### Media & Embeds
Directives that embed interactive or rich media into article content:

* [**Embedded Images** `{{Image}}`](/wiki:images) — Embeds file articles with width options (`full`, `breakout`, `max`), alt text, and captions.
* [**YouTube Video Player** `{{YouTube}}`](/wiki:youtube) — Privacy-preserving click-to-load responsive video player.
* [**Downloads Box** `{{DownloadsBox}}`](/wiki:downloads) — Digital issue download card with file size and SHA-256 hash lookup.

### Publication Displays
Components specifically tailored for magazine archives:

* [**Publication Issue Infobox** `{{PublicationIssueInfobox}}`](/wiki:infobox) — Sidebar infobox detailing publication date, price, ISSN, and barcode.
* [**Cover Carousel** `{{CoverCarousel}}`](/wiki:cover-carousel) — Standalone multi-cover slide carousel.
* [**Cover Grid** `{{CoverGrid}}`](/wiki:cover-grid) — Visual gallery grid linking to individual publication issue articles.

### Content & Layout Directives
Stylistic elements for editorial presentation:

* [**Timelines** `{{Timeline}}`](/wiki:timeline) — Chronological vertical milestone timeline with hollow nodes and connectors.
* [**Issue Credits** `{{IssueCredits}}`](/wiki:issue-credits) — Featured and collapsible contributor groups with roles and linked names.
* [**Pull Quotes** `{{PullQuote}}`](/wiki:pull-quote) — Stylized quote callout with optional attribution.

### Taxonomy, References & Maintenance
Directives for wiki organization and references:

* [**Stub Notice** `{{Stub}}`](/wiki:stub) — Incomplete article alert with a quick link to the editor.
* [**Footnotes** `{{Footnote}}` / `{{Footnotes}}`](/wiki:footnotes) — Inline numeric superscript notes and collected notes list.
* [**Citations & Bibliography** `{{Citation}}` / `{{Citations}}`](/wiki:citations) — Bibliographic references (`†N`) and aggregated bibliography.
* [**Categories** `{{Category}}` / `{{Categories}}`](/wiki:categories) — Article topic tags and category navigation button bar.

---

## Standard Markdown Features

Articles also support full CommonMark syntax with Markdig advanced features:

* **Alert Blocks:** `> [!NOTE]`, `> [!TIP]`, `> [!IMPORTANT]`, `> [!WARNING]`, and `> [!CAUTION]`.
* **Pipe Tables:** Standard Markdown tables with aligned headers and cells.
* **Abbreviations:** Defined with `*[ABBR]: Expanded Name` anywhere in the article.
* **Auto-generated Heading Links:** Heading text automatically generates anchor IDs for linking."""
    },
    {
        "slug": "wiki:eyebrow",
        "title": "Wiki: Eyebrow Directive",
        "display_title": "Wiki: Eyebrow Directive",
        "text": """The `{{Eyebrow}}` directive adds an article classification label above the primary title.

## Syntax

Place the directive on its own line near the top of the article:

```markdown
{{Eyebrow TYPE}}
```

## Supported Types

The `TYPE` parameter is case-insensitive and must match one of the following recognized values:

| Type Value | Rendered Label | Description |
| :--- | :--- | :--- |
| `MAGAZINE` | **Magazine** | Used for publication brand or series articles. |
| `MAGAZINEISSUE` | **Magazine Issue** | Used for specific individual issues (e.g. Month/Year). |
| `MAGAZINEFRANCHISE` | **Magazine Franchise** | Used for broad media franchises spanning multiple publications. |

## Behavior and Rules

* **Block Placement:** Must appear on its own line as a standalone block.
* **First Occurrence Wins:** Only the first valid eyebrow directive in the document is used. Duplicate tags are silently ignored.
* **Unknown Types:** Any unrecognized type string is silently consumed with no HTML output.
* **Metadata Output:** Stores the display label in the document's `Eyebrow` metadata key, which the article template renders directly above the `<h1>` heading.

## Example

```markdown
{{Eyebrow MAGAZINEISSUE}}
{{Subtitle Volume 1, Issue 1}}

# BYTE Magazine September 1975
```

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:subtitle",
        "title": "Wiki: Subtitle Directive",
        "display_title": "Wiki: Subtitle Directive",
        "text": """The `{{Subtitle}}` directive specifies an explanatory subtitle displayed directly beneath the main article heading.

## Syntax

```markdown
{{Subtitle Your subtitle text goes here}}
```

## Rules and Guidelines

* **Single-value Form:** A single space separates `Subtitle` from the text.
* **Whitespace:** Leading and trailing whitespace is automatically trimmed.
* **Plain Text:** Subtitles are rendered as plain text within an HTML `<p class="subtitle">` element inside the article header `<hgroup>`.
* **First Block Wins:** The first `Subtitle` directive is used. Subsequent occurrences are ignored. If the first directive has an empty value, no subtitle is displayed.
* **Single Line:** The directive must be contained on a single line.

## Example

```markdown
{{Eyebrow MAGAZINE}}
{{Subtitle The small systems journal that defined the microcomputer revolution}}

# BYTE
```

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:short-description",
        "title": "Wiki: Short Description Directive",
        "display_title": "Wiki: Short Description Directive",
        "text": """The `{{ShortDescription}}` directive defines a concise one-line summary for the article.

## Syntax

```markdown
{{ShortDescription A concise summary of this article under 160 characters.}}
```

## Purpose and Usage

* **SEO & Social Sharing:** Stored in the `ShortDescription` document metadata and rendered into page `<meta name="description">` and Open Graph tags.
* **Search Results:** Displayed in article cards, search listings, and link previews across the site.
* **No Direct Body Output:** As a metadata directive, it does not print directly into the visible article body text.

## Rules

* **Length:** Keep descriptions concise—ideally between 80 and 160 characters.
* **First Block Wins:** Only the first occurrence in the document is used.
* **Single Line:** Must be written on a single line.

## Example

```markdown
{{ShortDescription September 1975 inaugural issue of BYTE, covering microcomputer kits and early software.}}
```

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:header-image",
        "title": "Wiki: Header Image Directive",
        "display_title": "Wiki: Header Image Directive",
        "text": """The `{{HeaderImage}}` directive specifies the full-width hero image displayed at the top of an article above the title.

## Syntax

Reference an uploaded file article by its slug:

```markdown
{{HeaderImage slug-of-file-article}}
```

or explicitly using the `file:` namespace prefix:

```markdown
{{HeaderImage file:slug-of-file-article}}
```

## Rules and Resolution

* **Target Article:** The slug must correspond to an existing `File` article (`ArticleType.File`) that has an active canonical file revision.
* **Prefix Handling:** The `file:` prefix is optional and is stripped during database lookup.
* **Site and Culture Matching:** The file article must exist in the same site and culture as the current article.
* **First Resolved Image Wins:** The first valid, resolved header image becomes the page hero. If the referenced file cannot be found, no hero image is rendered.
* **Uploads:** To upload a new image to use as a header, visit the [Media Uploader](/wiki:upload).

## Example

```markdown
{{HeaderImage file:byte-volume-1-issue-1-september-1975-cover}}
{{Eyebrow MAGAZINEISSUE}}
{{Subtitle Volume 1, Issue 1}}

# BYTE - September 1975
```

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:source",
        "title": "Wiki: Source Directive",
        "display_title": "Wiki: Source Directive",
        "text": """The `{{Source}}` directive records provenance, scanner attribution, or source repository details for a file article.

## Syntax Forms

Three syntax variations are supported:

### 1. Plain Text Description
```markdown
{{Source Original print scan by QINGCHARLES}}
```

### 2. URL Only
```markdown
{{Source https://archive.org/details/byte-magazine-1975-09}}
```
When a URL starting with `http://` or `https://` is supplied without text, it is used as both the display text and the outbound hyperlink.

### 3. Text and URL Combined
```markdown
{{Source Internet Archive Scan |#| https://archive.org/details/byte-magazine-1975-09}}
```
Use `|#|` to separate the display label from the outbound link.

## Display and Behavior

* **File Info Panel:** Displayed in the file metadata panel beneath the preview on file pages (`/file:<slug>`).
* **Approved Domain Policy:** External URLs linking to approved domains receive standard `rel="noopener"`, while unverified external domains receive `rel="nofollow noopener"`.
* **Metadata Keys:** Stored in `SourceText` and `SourceUrl` document metadata keys.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:images",
        "title": "Wiki: Image Directive",
        "display_title": "Wiki: Image Directive",
        "text": """The `{{Image}}` directive embeds an image from an existing file article into the flow of an article.

## Syntax

```markdown
{{Image file:slug-of-file |#| Layout=full |#| Alt=Descriptive text |#| Caption=Caption text}}
```

or simply by slug:

```markdown
{{Image file:slug-of-file}}
```

## Parameters

| Parameter | Required | Description |
| :--- | :--- | :--- |
| **Slug** (Positional) | Yes | The URL slug of the target file article. Accepts bare slug or `file:` prefix. |
| **`Layout=`** | No | Layout width: `full`, `breakout`, or `max`. Defaults to standard column width. |
| **`Alt=`** | No | Alternate text for screen readers and accessibility. Defaults to the file article title. |
| **`Caption=`** | No | Caption text associated with the image figure. |

## Layout Options

* **`Layout=full`** — Matches the standard width of the reading column.
* **`Layout=breakout`** — Extends slightly wider than the prose text for visual emphasis.
* **`Layout=max`** — Expands to the maximum available container width, ideal for double-page spreads.

## Example

```markdown
{{Image file:byte-1975-circuit-board |#| Layout=breakout |#| Alt=Close-up of wire-wrapped circuit board |#| Caption=Figure 1: Wire-wrapped prototyping board from the construction article.}}
```

> [!TIP]
> If a referenced file article does not exist or has no file attached, the site renders a graceful `missing-image.png` placeholder.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:youtube",
        "title": "Wiki: YouTube Directive",
        "display_title": "Wiki: YouTube Directive",
        "text": """The `{{YouTube}}` directive embeds a responsive, privacy-preserving video player.

## Syntax

```markdown
{{YouTube VIDEO_REF |#| Title=Descriptive title |#| StartSeconds=45 |#| Caption=Optional caption}}
```

## Parameters

| Parameter | Required | Description |
| :--- | :--- | :--- |
| **Video Reference** | Yes | 11-character YouTube video ID or accepted YouTube URL. |
| **`Title=`** | Yes | Plain text title used for the video banner and accessibility label. |
| **`StartSeconds=`** | No | Playback start offset in seconds (e.g. `90`). |
| **`Caption=`** | No | Optional caption rendered in a `<figcaption>` beneath the player. |

## Accepted URL Formats

* **Bare ID:** `M7lc1UVf-VE`
* **Short URL:** `https://youtu.be/M7lc1UVf-VE`
* **Standard Watch URL:** `https://www.youtube.com/watch?v=M7lc1UVf-VE`
* **Shorts URL:** `https://www.youtube.com/shorts/M7lc1UVf-VE`
* **Embed URL:** `https://www.youtube-nocookie.com/embed/M7lc1UVf-VE`

## Privacy & Performance Design

* **Zero Upfront Tracking:** No external iframes, YouTube scripts, or tracking cookies are loaded when the page opens.
* **Click-to-Load:** The video poster and play button render entirely from local markup and CSS. The embedded iframe is generated only when the reader clicks play.
* **No-Cookie Domain:** When loaded, the player uses `www.youtube-nocookie.com`.
* **Accessible Fallback:** Without JavaScript, the component acts as a direct link to the canonical YouTube video.

## Example

```markdown
{{YouTube https://youtu.be/M7lc1UVf-VE |#| Title=Byte Magazine Retrospective |#| StartSeconds=30 |#| Caption=A documentary retrospective on early computing periodicals.}}
```

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:downloads",
        "title": "Wiki: Downloads Box Directive",
        "display_title": "Wiki: Downloads Box Directive",
        "text": """The `{{DownloadsBox}}` directive renders an official digital issue download card (e.g. for complete PDF or CBZ magazine scans).

## Syntax

```markdown
{{DownloadsBox SHA256_HASH}}
```

## Parameters

* **SHA-256 Hash (Positional):** The 64-character hexadecimal SHA-256 hash of the download file registered in the Magazedia downloads database.

## Rendered Output

When a valid hash is supplied and verified in the database, the extension renders a download box containing:
* File icon and verified filename
* Human-readable file size (e.g. `42.5 MB`)
* Download button linking to the verified file URL with `rel="noopener noreferrer"`

> [!NOTE]
> If the hash is unverified or not found in the database, the box renders an informational message: *“Download file not available.”*

## Uploading Publication Downloads

To contribute a magazine download scan or link, visit the [Upload Tool](/wiki:upload).

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:infobox",
        "title": "Wiki: Publication Issue Infobox Directive",
        "display_title": "Wiki: Publication Issue Infobox Directive",
        "text": """The `{{PublicationIssueInfobox}}` directive renders a structured magazine issue sidebar on the right side of the page, displaying publication facts. Use a separate `{{CoverCarousel}}` block for cover images.

## Syntax

```markdown
{{PublicationIssueInfobox
CoverDate=1993-01 |#|
CoverPrice=$4.95 |#|
PublicationFormats=Print |#|
PrintPublicationDate=1993-01-02 |#|
ElectronicPublicationDate=1993-01-02 |#|
PrintEAN-13=9771059102003 |#|
ISSN=1059-1028
}}
```

## Standard Fact Keys

The following canonical keys automatically receive human-friendly display labels:

| Key | Rendered Label | Description |
| :--- | :--- | :--- |
| `CoverDate` | **Cover Date** | Issue date as printed on the cover (e.g. `September 1975`). |
| `CoverPrice` | **Cover Price** | Newsstand cover price (e.g. `$1.50`, `£0.95`). |
| `PublicationFormats` | **Publication Formats** | Format medium (e.g. `Print`, `Digital`). |
| `PrintPublicationDate` | **Print Publication Date** | Physical publication date (ISO or text). |
| `ElectronicPublicationDate` | **Electronic Publication Date** | Digital release date. |
| `ISSN` | **ISSN** | International Standard Serial Number. |
| `PrintEAN-13` | **Print EAN-13** | 13-digit retail barcode. |
| `PrintEAN-2` | **Print EAN-2** | 2-digit supplemental issue barcode. |

## Custom Keys

Any additional `Key=Value` pair is accepted and rendered as a definition row in the infobox, allowing custom facts such as `Editor=John Doe` or `Circulation=120,000`.

## Interactive Cover Carousel

Place cover images in a separate `{{CoverCarousel CoverImage=file:front-cover |~| CoverImage=file:back-cover}}` block. Legacy `CoverImage` properties in an infobox are ignored; existing article text is not automatically migrated.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:cover-carousel",
        "title": "Wiki: Cover Carousel Directive",
        "display_title": "Wiki: Cover Carousel Directive",
        "text": """The `{{CoverCarousel}}` directive renders an interactive cover image gallery without the full fact sheet of an infobox.

## Syntax

```markdown
{{CoverCarousel
CoverImage=file:wired-issue-1-cover |#| AltText=Front cover |#| Caption=Standard newsstand edition
|~|
CoverImage=file:wired-issue-1-variant |#| AltText=Variant cover |#| Caption=Subscriber variant
}}
```

## Structure & Delimiters

* **`|~|` (Item Delimiter):** Separates each slide in the carousel.
* **`|#|` (Property Delimiter):** Separates properties within a single slide item.

## Slide Properties

| Property | Required | Description |
| :--- | :--- | :--- |
| **`CoverImage=`** | Yes | File article slug (e.g. `file:cover-slug`). |
| **`AltText=`** | No | Screen reader description. Defaults to the file article title. |
| **`Caption=`** | No | Caption text reserved for slide descriptions. |

## Single vs. Multiple Covers

* **Single Cover:** Renders cleanly as `<aside class="cover-carousel single">` with no carousel arrows or dots.
* **Multiple Covers:** Renders a full interactive slide carousel with previous/next buttons and indicator dots.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:cover-grid",
        "title": "Wiki: Cover Grid Directive",
        "display_title": "Wiki: Cover Grid Directive",
        "text": """The `{{CoverGrid}}` directive renders a responsive grid of issue covers that link directly to individual issue articles.

## Syntax

List one issue article URL slug per line:

```markdown
{{CoverGrid
wired-issue-1
wired-issue-2
wired-issue-3
wired-issue-4
wired-issue-5
}}
```

## How It Works

1. **Issue Resolution:** The grid looks up each referenced article.
2. **Cover Extraction:** It inspects each issue article's `CoverCarousel` to locate its primary `CoverImage`, falling back to legacy infobox metadata.
3. **Linked Thumbnail:** Each cover is rendered in a responsive CSS grid, linking directly to the respective issue page (`/{culture}/{issue-slug}`).
4. **Fallback Handling:** If an issue article is missing a cover image, a default cover placeholder is displayed.

> [!TIP]
> The Cover Grid is commonly placed on publication franchise pages (e.g. `/wired-franchise`) to showcase chronologically ordered issues in a series.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:timeline",
        "title": "Wiki: Timeline Directive",
        "display_title": "Wiki: Timeline Directive",
        "text": """The `{{Timeline}}` directive creates a stylized vertical milestone timeline with circular nodes and dotted connectors.

## Syntax

```markdown
{{Timeline
Title=Magazine Inception |#| Date=January 1985 |#| Description=Founded by **Example Publishing** in San Francisco.
|~|
Title=Major Format Overhaul |#| Date=Fall 1991 |#| Description=Switched to perfect binding and expanded coverage to consumer hardware.
|~|
Title=Digital Transition |#| Date=2001 |#| Description=Ceased regular print run; archive migrated to digital distribution.
}}
```

## Delimiters

* **`|~|`:** Separates each milestone entry.
* **`|#|`:** Separates properties within a milestone.

## Supported Properties

| Canonical Property | Required | Accepted Aliases | Description |
| :--- | :--- | :--- | :--- |
| **`Title`** | Yes | `Heading`, `Header`, `Name` | The headline of the milestone event. |
| **`Date`** | No | `Time`, `Period`, `Year` | Date, year, or timeframe displayed in italics. |
| **`Description`** | No | `Body`, `Content`, `Text`, `Desc` | Narrative explanation. Supports Markdown formatting (bold, italics, links). |

## Styling

Timelines render as a semantic `<ol class="timeline">` list with CSS styles defined for clean typography, hollow circular markers, and dashed vertical connecting lines.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:pull-quote",
        "title": "Wiki: Pull Quote Directive",
        "display_title": "Wiki: Pull Quote Directive",
        "text": """The `{{PullQuote}}` directive renders a large typographical pull quote with optional author attribution.

## Syntax

```markdown
{{PullQuote "The print magazine is an enduring cultural medium." |#| Jane Doe, Founder}}
```

or without attribution:

```markdown
{{PullQuote "A bold step forward in typography and design."}}
```

## Features

* **Quotation Marks:** Surrounding straight double quotes (`"..."`) are automatically removed and styled using CSS quotes.
* **Attribution:** Any text after the `|#|` separator renders as a styled `<cite>` attribution line beneath the quote.
* **Placement:** Can be placed inline or as a standalone block between paragraphs to highlight key takeaways.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:stub",
        "title": "Wiki: Stub Directive",
        "display_title": "Wiki: Stub Directive",
        "text": """The `{{Stub}}` directive marks an article as an incomplete stub that needs additional information, research, or citations.

## Syntax

Place the directive on its own line, typically at the end of the article:

```markdown
{{Stub}}
```

## Banner Output

The directive generates a prominent callout banner:
* Includes an informational stub icon.
* Identifies the current site by name.
* Links to **`/wiki:stub`** (this guide).
* Generates a direct link to the article editor (`/slug/edit`) inviting contributors to expand the page.

## Best Practices

* Place `{{Stub}}` near the bottom of the article, after the main text and before `{{Footnotes}}`, `{{Citations}}`, and `{{Categories}}`.
* When an article is sufficiently expanded with complete sections, remove the `{{Stub}}` marker.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:footnotes",
        "title": "Wiki: Footnotes Directive",
        "display_title": "Wiki: Footnotes Directive",
        "text": """The `{{Footnote}}` and `{{Footnotes}}` directives provide inline explanatory notes and a collected footnote list.

## Syntax

### 1. Inline Footnote Reference
Insert `{{Footnote ...}}` directly adjacent to the text being annotated:

```markdown
The inaugural run was printed on recycled paper.{{Footnote Confirmed by the production manager in a 1995 interview.}}
```

### 2. Collected Footnotes Block
Place `{{Footnotes}}` where you want the collected notes list to appear (typically under a `## Notes` heading):

```markdown
## Notes

{{Footnotes}}
```

## How It Works

* **Automatic Numbering:** Footnotes are automatically assigned sequential numbers (`¹`, `²`, `³`...) in document order.
* **Bidirectional Navigation:** Clicking the inline marker jumps down to the corresponding note. Clicking the return arrow (`↩`) in the note jumps back to the exact reading position.
* **Rich Markdown Support:** Footnote content supports full Markdown, including italics, bold, code, and external links.

> [!IMPORTANT]
> Always include a single `{{Footnotes}}` block near the end of your article if you have placed inline `{{Footnote}}` markers. Without it, inline references will render but no notes list will be displayed.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:citations",
        "title": "Wiki: Citations Directive",
        "display_title": "Wiki: Citations & Bibliography Directive",
        "text": """The `{{Citation}}` and `{{Citations}}` directives create professional academic / Wikipedia-style inline bibliographic citations (`†N`) and an aggregated, deduplicated reference list.

## Syntax

### 1. Inline Citation
```markdown
Early computing publications emerged from amateur radio circles.{{Citation author=Brand, Stewart |#| title=Spacewar |#| journal=Rolling Stone |#| volume=7 |#| issue=11 |#| pages=50-58 |#| year=1972 |#| url=https://rollingstone.com/spacewar}}
```

### 2. Bibliography List
Place `{{Citations}}` where the collected bibliography should be rendered:

```markdown
## References

{{Citations}}
```

## Recognized Citation Properties

| Property | Repeatable | Description |
| :--- | :--- | :--- |
| **`author`** | Yes | Author name(s). Multiple authors are joined with commas and “and”. |
| **`title`** | No | Article or chapter title (rendered in italics). |
| **`journal`** | No | Magazine or periodical name. |
| **`publisher`**| No | Publishing house (used if `journal` is absent). |
| **`volume`** | No | Volume number (rendered as `vol. X`). |
| **`issue`** | No | Issue number (rendered as `no. Y` when volume is present). |
| **`pages`** | No | Page range (rendered as `pp. 12-24`). |
| **`year`** | No | Publication year. |
| **`doi`** | No | Digital Object Identifier (automatically linked to `https://doi.org/...`). |
| **`url`** | No | Direct outbound link. Links the title or displays the hostname. |

## Deduplication

If the identical citation payload appears multiple times in an article, it is de-duplicated into a single bibliography entry with multiple return back-links.

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    },
    {
        "slug": "wiki:categories",
        "title": "Wiki: Categories Directive",
        "display_title": "Wiki: Categories Directive",
        "text": """The `{{Category}}` and `{{Categories}}` directives manage article categorization and render a bottom category navigation button bar.

## Syntax

### 1. Tagging Categories
Tag an article by placing `{{Category ...}}` directives anywhere in the document:

```markdown
{{Category Technology Magazines}}
{{Category 1970s Publications}}
{{Category American Periodicals}}
```

### 2. Rendering the Category Bar
Place `{{Categories}}` at the very bottom of the article:

```markdown
{{Categories}}
```

## Behavior and Navigation

* **Silent Inline Markers:** `{{Category ...}}` produces no direct inline output where it is written; it registers the article in the specified category.
* **Styled Category Buttons:** `{{Categories}}` aggregates all tags and outputs a list of button badges linking to `/category:<slug>` (e.g. `/category:technology-magazines`).
* **Slug Generation:** Category names are trimmed, lowercased, and converted to kebab-case (spaces replaced by hyphens).

---
*Return to the [Formatting Guide](/wiki:formatting).*"""
    }
]

def main():
    conn = sqlite3.connect(DB_PATH, timeout=10.0)
    cursor = conn.cursor()

    cursor.execute("PRAGMA busy_timeout = 5000;")
    now_utc = datetime.now(timezone.utc).isoformat()

    print(f"Connecting to database: {DB_PATH}")
    print(f"Author User ID: {USER_ID}")

    inserted_count = 0
    for article in ARTICLES:
        slug = article["slug"]
        title = article["title"]
        display_title = article["display_title"]
        text = article["text"]

        # Check if already exists
        cursor.execute("SELECT Id FROM ArticleRevisions WHERE UrlSlug = ? AND IsCurrent = 1", (slug,))
        existing = cursor.fetchone()
        if existing:
            print(f"Article {slug} already exists (Id={existing[0]}), skipping.")
            continue

        canonical_id = str(uuid.uuid4())

        cursor.execute("""
            INSERT INTO ArticleRevisions (
                CanonicalArticleId,
                SiteId,
                Culture,
                Title,
                DisplayTitle,
                UrlSlug,
                IsCurrent,
                Type,
                Text,
                CanonicalFileId,
                RevisionReason,
                CreatedByUserId,
                DateCreated,
                DateDeleted
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            canonical_id,
            SITE_ID,
            CULTURE,
            title,
            display_title,
            slug,
            1,          # IsCurrent
            TYPE_INFO,  # Type = 4 (ArticleType.Info)
            text,
            None,       # CanonicalFileId
            "Initial documentation page creation",
            USER_ID,
            now_utc,
            None        # DateDeleted
        ))
        inserted_count += 1
        print(f"Created: {slug} ('{display_title}')")

    conn.commit()
    conn.close()
    print(f"\nSuccessfully created {inserted_count} articles in app.db.")

if __name__ == "__main__":
    main()
