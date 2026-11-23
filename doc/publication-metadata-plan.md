# Publication and issue metadata

**Implementation and rollout plan · 22 November 2026**

Readers sharing a Magazedia link should see the right edition, a useful summary and recognizable artwork. Catalogue tools should be able to tell which magazine or issue the page describes and follow an issue to its publication. Editors should keep maintaining those facts in the article. Generate the metadata in the initial server-rendered HTML.

Stages 1–5 form the first release. Stage 2 can land independently. Stages 3 and 4 both use stage 1 and can be reviewed separately. Stages 6 and 7 are follow-ups.

## Evidence and acceptance examples

The four publication pages below all served an authored meta description when inspected. Their HTML heads lacked a canonical link, Open Graph tags and JSON-LD. The shared layout already emits the document language, page title, optional description and conditional robots directive. The XML sitemap already emits article URLs and revision-based `lastmod`. [Shared layout](../src/WikiWikiWorld.Web/Pages/Shared/_Layout.cshtml) · [Sitemap service](../src/WikiWikiWorld.Web/Services/SitemapService.cs)

| Public example | What the implementation must handle |
| --- | --- |
| [Marie Claire (USA)](https://en.magazedia.com/marie-claire-usa) | Keep the US edition in the title. Use the print ISSN and official website. The infobox gives two generations of publishers and a first issue designated “Sept./Oct. 1994”. Exercise the card fallback for a publication without a header. |
| [Juxtapoz](https://en.magazedia.com/juxtapoz) | The description gives the 1994–2025 run. Keep the seasonal precision of its first and final issue designations. Exercise a second headerless publication with its own title. |
| [Fast Company](https://en.magazedia.com/fast-company) | Use the editorial illustration. Preserve the print ISSN 1085-9241 and online ISSN 1943-2623. Three publisher rows record a succession. The circulation figure is a paid rate base with a year. |
| [George](https://en.magazedia.com/george) | The portrait and existing political-magazine description disambiguate the short title. Preserve the final X in ISSN 1084-662X. Both this header and the Fast Company header currently render with literal `alt="..."` and use AVIF. |
| [GQ Style (USA), Fall 2016](https://en.magazedia.com/gq-style-usa-fall-2016) | The issue has a carousel and cited prices and barcodes. Its infobox supplies “Fall 2016” and lacks a parent-publication field. Keep the issue useful with those gaps. |
| [Cinefex № 1](https://en.magazedia.com/cinefex-no-1) | Exercise an unresolved cover alongside separate header artwork. The infobox gives March 1980 as both cover and print date. Confirm the release evidence before treating that value as an on-sale date. |

Keep small, representative local fixtures for automated tests. Use the live pages for read-only spot checks because editors can change them.

## Stage 1 — Establish page identity and collect render data

- [ ] Start with current public Magazedia publication and issue views. Recognize a valid site-defined infobox, including the legacy issue syntax. For sparse articles, use an unambiguous existing publication or issue eyebrow to select the card. Conflicting types, multiple competing subject boxes or an uncertain serial classification should yield ordinary page metadata until the subject is clear. A one-off mook needs its own classification.
- [ ] Derive the absolute canonical URL from the configured site and culture origin and the current article path, using the same path rules as the sitemap. Drop tracking queries and fragments. A renamed slug or redirect leads to the current destination. Preserve historical revisions’ `noindex`. Limit the new catalogue output to the public article view so that source, edit, history, error, profile and other sites’ pages cannot inherit a magazine record through the shared layout.
- [ ] Extend the existing Markdig render result with the plain title, description, subject facts, resolved links and eligible images needed below. Reuse the parsed document and batched image lookups. Extract text and link targets from inline markup, ignoring citation markers and footnote bodies. Prefer `ShortDescription`, then a usable lead sentence, then a short localized summary from supported facts. Omit an unavailable description. Keep exactly one HTML description.

**Acceptance.** A tracked URL, the clean article and the sitemap agree on the canonical address. Two culture hosts keep separate addresses. A sparse article still has a shareable title. Check a historical revision, a redirect, a wiki page that demonstrates infobox syntax and a non-Magazedia site. [Article view model](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml.cs) · [Article renderer](../src/WikiWikiWorld.Web/Services/ArticleRenderer.cs) · [URL helpers](../src/WikiWikiWorld.Web/Helpers/ArticleUrlHelper.cs)

## Stage 2 — Fix header alternative text and the authoring workflow

- [ ] Add optional plain-text `AltText` to `HeaderImage`, matching the carousel’s spelling. For example, `{{HeaderImage file:header-for-george |#| Focus=center |#| AltText=Portrait of a woman in a powdered wig and colonial coat}}`. Carry the text with the image that actually resolves, including its focus. Preserve existing bare-slug, `file:` and focus-only directives.
- [ ] Replace `alt="..."` in the article view. Prefer the authored description. For a legacy informative image, reuse a genuinely descriptive file caption or title when available, then queue unresolved cases for editorial review. Use `alt=""` for decorative headers. Support an explicit empty author override for that case and distinguish it from a missing value. A filename, generic “Header for George” or placeholder is unsuitable spoken text. The chosen **share image** needs its own useful description even when an on-page header is decorative.
- [ ] Add an optional image-description field to the header helper and update its copied markup, authoring examples and localized UI strings. Test punctuation and directive delimiters in generated markup. Audit existing informative headers, starting with Fast Company and George. Prepare descriptions from the actual pictures, record the affected slugs and apply approved content corrections as normal wiki revisions.

**Acceptance.** Inspect both named headers in the accessibility tree, plus a decorative header, a legacy focus-only header and an unresolved file. The helper’s copied directive round-trips through the parser, preserves the crop and renders editorial input as plain text. [Header parser and enrichment](../src/WikiWikiWorld.Web/MarkDigExtensions/HeaderImage.cs) · [Article template](../src/WikiWikiWorld.Web/Pages/Article/View.cshtml) · [Header helper](header-helper.md) · [Directive reference](../MARKDIG_EXTENSIONS.md) · [W3C alt decision tree](https://www.w3.org/WAI/tutorials/images/decision-tree/)

## Stage 3 — Publish share cards and usable images

- [ ] Use one optional Razor head partial for the canonical link, `og:url`, `og:type=article`, plain `og:title`, `og:site_name`, description, image and image alt. Set the X card type to suit the verified image layout. Use the culture-specific canonical URL for `og:url`. Add `og:image:type`, width and height for the delivered image. Emit an Open Graph locale only where the configured culture has a defensible language/territory mapping.
- [ ] On a **publication**, prefer its resolved editorial header, then an explicitly representative image, then a Magazedia card bearing the publication title. On an **issue**, prefer its resolved front cover, then its editorial header, then the titled fallback. Verify carousel ordering before treating the first image as the front cover. Skip missing-image placeholders, back covers identified as such, cover-grid thumbnails and contributor avatars. Preserve the full portrait cover in a wide card with surrounding space where needed.
- [ ] Serve crawler-accessible JPEGs for AVIF artwork and wide compositions. First exercise the existing image pipeline against the two real AVIF inputs in the deployment container. If it cannot decode them, verify a JPEG prepared during asset preparation or upload before adding any runtime codec. Reuse suitable existing JPEGs. Use revision-aware image URLs, including the title or layout version where the pixels depend on it, so edits refresh previews without regenerating images on every article request.

**Acceptance.** Fetch each absolute image URL anonymously and verify status, decoded dimensions and media type. Check both illustrated and photographic headers, two differently titled fallbacks, a real cover and a broken cover. Review an actual wide preview at phone size for the masthead, issue designation and face crop. Record which sharing client was checked. [Cover resolution](../src/WikiWikiWorld.Web/MarkDigExtensions/CoverCarousel.cs) · [Open Graph protocol](https://ogp.me/)

## Stage 4 — Describe the publication and issue in JSON-LD

Emit one `application/ld+json` graph. Give the `WebPage` the canonical URL as its ID. Its `mainEntity` refers to a distinct subject ID, such as the canonical URL with `#periodical` or `#issue`. Use that same periodical ID when an issue links to its parent.

| Record | First release mapping |
| --- | --- |
| Encyclopedia page | Plain name, canonical URL, page language, description and current revision time as `dateModified`. Attribute the page to the wiki. Only add page `datePublished` if the original publication time of the wiki entry is established. |
| `Periodical` | Edition-specific name, repeatable checksum-valid `issn`, known editorial language as `inLanguage`, and the validated official `Website` link as the subject’s `url`. Add an unambiguous publisher as an organization and a clearly credited editor as a person. Keep dated publisher strings out of organization names. Defer publisher histories where a simple mapping loses their time periods. |
| `PublicationIssue` | Name, actual issue image, and `issueNumber` where the designation is unambiguous. Resolve the authored `Publication` link to the current same-site periodical and emit `isPartOf`. Where an explicit volume and issue can be distinguished, link issue → `PublicationVolume` → periodical. A missing or unresolved parent leaves that link absent. |
| Dates and identifiers | Use an evidenced issue release date at the precision supported by the source and consumer. Keep conflicting print and digital releases edition-specific or omit the ambiguous property. Preserve seasonal/combined cover dates in the visible designation. Keep ISSNs on the serial, including a terminal X. A cover price is historical information. A barcode supplement alone cannot establish an issue number. |

- [ ] Map a small allowlist of stable schema keys from the [Magazedia infobox definitions](../src/WikiWikiWorld.Web/InfoboxSchemas/magazedia.json). Omit unknown, placeholder and ambiguous values from JSON-LD. Keep qualified circulation, founding credits, page counts, prices and barcode details available in the visible infobox pending a faithful field-specific mapping. A generic share card belongs to the webpage and must never masquerade as an actual issue image.
- [ ] Serialize typed values with the default HTML-safe `System.Text.Json` encoder. Let Razor encode ordinary meta attributes. Test quotes, ampersands, non-Latin names, inline markup and literal `</script>` as editorial input. Parse the resulting HTML and JSON to check both the values and the script boundary.

**Acceptance.** Use the Schema.org validator on rendered publication and issue examples. Assert the two Fast Company ISSNs, George’s terminal X, and the distinction between the wiki’s language and the magazine’s language. Test a linked issue, a missing parent, both issue syntaxes, a double issue and a winter designation. Google’s Rich Results Test covers its supported features and is supplementary here. [Page and subject](https://schema.org/mainEntity) · [Periodical](https://schema.org/Periodical) · [PublicationIssue](https://schema.org/PublicationIssue) · [PublicationVolume](https://schema.org/PublicationVolume) · [Schema.org validator](https://validator.schema.org/) · [Google search feature gallery](https://developers.google.com/search/docs/appearance/structured-data/search-gallery)

## Stage 5 — Verify and roll out the first release

- [ ] Add focused regression tests beside the existing [infobox](../tests/WikiWikiWorld.Web.Tests/InfoboxTests.cs), [cover](../tests/WikiWikiWorld.Web.Tests/PublicationIssueCoverTests.cs) and [sitemap](../tests/WikiWikiWorld.Web.Tests/SitemapTests.cs) coverage. Include final HTTP responses with JavaScript disabled, head tag counts, cross-site isolation, a genuinely linked issue, sparse content and prior revisions. Recheck header and carousel rendering in the body.
- [ ] Run `dotnet build WikiWikiWorld.slnx -c Release`, `dotnet test tests/WikiWikiWorld.Web.Tests -c Release`, and `python scripts/localization.py check` after changing helper strings. Verify JPEG generation and fallback assets in the published chiseled image. Document the authoring rules and stage outcomes, including checks that require a publicly reachable URL.
- [ ] Deploy through the existing approved release procedure when authorized. Repeat anonymous page and image checks on the sample set, compare canonical URLs with the live sitemap, and refresh a cached preview on a supported sharing service. Track application rollback separately from wiki revisions and generated assets. Keep the existing AVIF originals and record the revision IDs of approved editorial backfills.

**First-release completion.** A shared link to George identifies the magazine at a glance. A linked issue leads a catalogue consumer to the same periodical identified on its publication page. An article with missing artwork or imprecise dates still produces an accurate head.

## Stage 6 — Link actual translations

- [ ] Build reciprocal `rel="alternate" hreflang` links from current article-language groups. The service returns the *other* members, so include the current page explicitly. Use each member’s real culture host and slug and keep each translation self-canonical. Omit unlinked cultures and internal test cultures from public alternates.
- [ ] Check a linked pair with different slugs, an unlinked article, a renamed translation and a removed link. Both members should emit the same set of language versions. Check the cost of the group lookup on ordinary article views before broadening rollout. [Language service](../src/WikiWikiWorld.Web/Services/ArticleLanguageService.cs) · [Google localized-page guidance](https://developers.google.com/search/docs/specialty/international/localized-versions)

## Stage 7 — Let readers cite the issue or the wiki entry

- [ ] Reuse the verified fields to offer two explicitly labelled actions: **Cite this issue** and **Cite this Magazedia page**. Draft a human-readable citation for each. For the wiki entry, include its revision-specific permalink. For the issue, preserve printed designations, supported dates and the parent publication. Include partial citations when a fact is unknown.
- [ ] Add a small downloadable RIS export and verify a real Zotero import for both choices. Check title, publication, issue, date, edition and URL. Consider mapping individual contents entries or contributor roles after these two citations work with representative issues. [Zotero bibliographic formats](https://www.zotero.org/support/dev/data_formats)

## Rollout notes

The implementation combines same-type infobox blocks when their fields are disjoint. The four publication examples use two such blocks each. Overlapping fields across boxes can indicate competing subjects, so those pages keep their ordinary metadata until the overlap is resolved.

The article now offers **Cite this issue** for an issue and **Cite this Magazedia page** for its encyclopedia entry. The issue's RIS uses the magazine item type and preserves a seasonal or combined cover designation in its notes. The encyclopedia citation uses the exact wiki revision URL.

For publication headers, prepare a wide JPEG using the same vertical focus as the hero. For issue covers, prepare a wide JPEG with the full cover visible. Run the following from the checkout with PowerShell and ImageMagick, using a local copy of the revision image.

```powershell
pwsh -NoProfile -File scripts/build-share-image.ps1 -ImagePath 'data/sitefiles/1/images/<file-id>/200.avif' -Kind Header -Focus Center
pwsh -NoProfile -File scripts/build-share-image.ps1 -ImagePath 'data/sitefiles/1/images/<file-id>/123.jpg' -Kind Cover
```

The script writes a 1200 × 630 JPEG beside the source. The web app looks for `<original-file>.header-social-v1.jpg` or `<original-file>.cover-social-v1.jpg` alongside the corresponding file revision. Place the prepared rendition at that same relative path in the site's asset storage during an authorized rollout. A new upload has a new revision path and gets its own rendition. The app can also use a source JPEG as a share image. Pages without shareable artwork use the shipped Magazedia image, with the publication's title carried by `og:title`.

### First editorial backfill

Apply these descriptions to the existing directives in two ordinary article revisions. Both current headers use center focus. Review the generated JPEGs with the new alt text together.

| Article | Revision image | Proposed `AltText` |
| --- | --- | --- |
| [Fast Company](https://en.magazedia.com/fast-company) | `80ee3095-66f2-4ff7-820f-9b24d921d4b7/200.avif` | Colorful editorial illustration with small scenes of people using computers and working with machines. |
| [George](https://en.magazedia.com/george) | `53da74f2-f2aa-4a3d-80e3-bfac58f2b7f9/202.avif` | Portrait of a woman in a white powdered wig and a black coat with yellow lapels. |

The local `artifacts/publication-metadata/sitefiles/` directory contains the prepared Fast Company and George header cards and the GQ Style Fall 2016 cover card in their deployment paths. For GQ Style, also review adding `Publication=[GQ Style (USA)](/gq-style-usa)` to the issue infobox. That field enables the catalogue link to the periodical.

### Local verification

- Release build and 436 tests passed. The existing ImageSharp package advisories still appear in the build output. The localization scan and header-helper JavaScript syntax check passed.
- Tested HTTP responses from development fixtures based on all four publication examples and two issues. Compared canonical URLs with the sitemap and checked historical `noindex`, repeatable checksum-valid ISSNs, seasonal dates, share image fallbacks and both RIS responses.
- The Schema Markup Validator reported zero flagged properties or types in the rendered Fast Company and GQ Style Fall 2016 JSON-LD. [Schema Markup Validator](https://validator.schema.org/)
- Checked George’s accessible image name and the header helper’s description and decorative options in Chromium. Reviewed the Fast Company and George JPEG crops and the full GQ Style portrait cover at card width.
- Published the chiseled Linux image to a local archive and ran its extracted filesystem in a WSL jail as UID 1654. Verified HTTPS requests, JPEG assets, citation downloads, reciprocal alternates after a rename, alternate removal and site isolation.
- Ran both downloaded RIS files through Zotero’s official RIS translator in a test harness. The issue retained its publication, volume, issue and cover season. The webpage retained its site name, revision date and permalink. [Zotero RIS translator](https://github.com/zotero/translators/blob/master/RIS.js)

### Production release · 23 November 2026

- Deployed source `2d2c5467ed3d` as `release-20261123T061252Z-2d2c5467ed3d` at 06:15 UTC. The host is running `ghcr.io/qingcharles/wikiwikiworld@sha256:16c70a7ddce065fbde22a66c38a1f0aee9899f95d13162ff276c3e393fdcc709`.
- Installed the three JPEGs beside their source revisions and checked their bytes, mode and ownership. Anonymous checks verified the seven publication and issue pages, their canonical links, the sitemap, a historical revision and its source view, and both RIS downloads. The container reported zero restarts or application errors.
- MaggieTheBot added the Fast Company alt text in revision `1082` (from `970`), the George alt text in `1083` (from `1036`), and the GQ Style Fall 2016 parent link in `1084` (from `882`). Each save returned zero warnings. Verified the accessible image names in Chromium and the issue’s `isPartOf` link in the live JSON-LD.
- Bluesky’s preview service returned the expected George portrait, GQ Style issue cover and Juxtapoz fallback card. [Bluesky link preview endpoint](https://github.com/bluesky-social/social-app/blob/main/src/lib/constants.ts)
- Imported both production RIS downloads into an isolated Zotero 10.0.8 library. Zotero recorded the issue as a magazine item with its Fall 2016 cover date note and the Magazedia entry as a webpage with its revision permalink.

The previous application image is retained as `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261123T061252Z-2d2c5467ed3d`. Pass that reference to `/home/deploy/update.sh` for application rollback. The wiki history retains the three previous revisions. Their source snapshots and exact edit payloads are in the local release record under `.local/releases/release-20261123T061252Z-2d2c5467ed3d/`.
