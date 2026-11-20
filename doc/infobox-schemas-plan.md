# Plan: generic `{{Infobox}}` with per-site schemas

Status: planning. We work through the steps one at a time; each step ends with a check before the next starts.

## Goal

Replace the issue-only `{{PublicationIssueInfobox}}` with one generic `{{Infobox <Type>}}` directive. The fields, their order and their labels for each type come from a JSON schema file per site, so Magazedia, Cinepedia and the other wikis can each define their own infobox types without code changes. Existing articles keep rendering throughout.

## What the code looks like today (checked 2026-11-18)

- `MarkDigExtensions/PublicationIssueInfobox.cs` already renders any `Key=Value` rows as `<aside class="infobox"><dl>`. Only its name and a small English label switch are issue-specific.
- There is one shared Markdown pipeline for every site. Site-specific data is added after parsing by `EnrichAsync(Document, Context, SiteId, Culture)` calls in `Services/ArticleRenderer.cs` (images, covers, redirects). The infobox will follow the same pattern.
- Sites are defined in `appsettings.{Development,Production}.json` under `SiteConfiguration.Sites` (`SiteId`, `SiteName`, `DefaultCulture`, `Domains`). Development has sites 1–4; production also has 5 (Makers).
- There is no localization yet: headings such as "Citations" are hard-coded English. Infobox labels per culture will be the first.
- Usage (production copy from 2026-10-21): 16 of 341 current articles, all English Magazedia. Four (cinefex, 032c, details, fruits) have since been cleaned. The rest use the same copied keys: CoverPrice, CoverDate, PublicationFormats, PrintPublicationDate, ElectronicPublicationDate, ISSN, PrintEAN-13, PrintEAN-2, and UPC on two.

## Steps

### 1. Agree the Magazedia schemas
Draft `InfoboxSchemas/magazedia.json` with two types, `Publication` and `PublicationIssue` (one pair for magazines, zines, journals, newsletters and mooks; the eyebrow says which): each field's key, order, English label, and whether it repeats. Start from the keys in use and the facts the V21 and V2 prompts ask for. Add Japanese and Arabic labels only where we are sure of them; the rest fall back to English.
- **Decision for you:** the field list. Once agents fill these in, names are hard to change.
- **Done when:** you have approved the two field lists.

### 2. Schema loading and startup checks
Add the schema file model, a loader that reads every `InfoboxSchemas/*.json`, and options registration in `Program.cs`.
- Each file states its `SiteId`. Startup fails on a malformed file or two files claiming one site. A `SiteId` missing from `Sites` fails in production and logs a warning elsewhere.
- Make sure the files are copied into the build and publish output (and so into the container image).
- **Done when:** the app starts with the file, refuses a deliberately broken copy, and the published output contains the files.

### 3. Generic parser and renderer, with the alias
Rename the extension to `Infobox`. Recognise `{{Infobox <Type>` and keep `{{PublicationIssueInfobox` as an alias for `{{Infobox PublicationIssue`. Add an `InfoboxExtension.EnrichAsync` step in `ArticleRenderer` that attaches the site's schema and the culture's labels.
- Rendering order: schema fields in schema order, then unknown keys in the order written. Labels: culture label, then English label, then the raw key.
- Parsing, `|#|` handling, nested citations and footnotes, safety filtering and the HTML shape stay as they are; no CSS change.
- **Done when:** the build passes and existing infobox articles render the same as before on the dev server.

### 4. Warnings for unknown keys and types
Report keys a schema does not define, and types that do not exist, without blocking the save.
- **Decision for you:** where warnings appear. Default: in the API response for POST and PUT (so `wiki_api.py` agents see them) and in the editor preview, never on the public page.
- **Done when:** a save with a misspelt key returns a warning and still saves.

### 5. Tests
Update the existing `PublicationIssueInfoboxTests` and `PublicationIssueCoverTests` to the new name, and keep one case proving the old name still works.
- **Decision for you:** your standing rule is no new unit tests unless asked. Default: no new unit tests; the schema loader and labels are checked by rendering real pages in step 6.
- **Done when:** `dotnet test tests/WikiWikiWorld.Web.Tests -c Release` passes.

### 6. Check on the dev server
Render a magazine infobox, an issue infobox, a page using the old name, a Japanese page for its labels, and a page with a bad key.
- **Done when:** all five look right, with screenshots.

### 7. Documentation and prompts
Update `MARKDIG_EXTENSIONS.md`, `PROJECT.md` (directive list, block-parser list, the infobox entry, and roadmap item 5.13), and the `wiki:infobox` help page in `scripts/seed_wiki_docs.py`. Leave the `Load.sql` seed data on the old name, which keeps the alias exercised. Add an infobox section to the V21 and V2 prompts and the agent briefing.
- **Done when:** the docs describe the new directive and the prompts list the agreed fields.

### 8. Commit and hand over for deployment
Commit on a branch with the verification results. Deploying is yours.
- **Done when:** you have deployed and the live site renders the existing infobox articles.

### 9. Update production articles
Recount live articles that use the old name. Then, with the same save/compare/update script used for the cleanup:
- Issue pages (playboy-usa-february-2025, gq-style-usa-summer-2016, gq-style-usa-summer-2017, edge-issue-no-1, cinefex-no-1): switch to `{{Infobox PublicationIssue`, checking that the values belong to that issue.
- Magazine pages with copied issue facts (playgirl-1973, playgirl-1955-1957-magazine, gq-style, sneaker-freaker-russia, sugar-ape): remove the placeholders. Real `{{Infobox Publication}}` boxes come with each V21 rewrite.
- Help pages (wiki:infobox, wiki:formatting): update the text.
- **Done when:** no current article uses the old name except on purpose, and every edited page renders with no `{{`.
- **Done 2026-11-19.** The recount found 30 articles, not 10; see [release-infobox-2026-11-18.md](release-infobox-2026-11-18.md#production-articles-2026-11-19). The help pages had already been updated at deployment.

### 10. Later, not part of this change
- Decide whether the old name should show a "deprecated" warning once nothing uses it.
- Cinepedia `Film` schema and the other sites' schemas.
