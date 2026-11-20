# Schema-driven infobox — release 2026-11-18

The owner asked to commit to `main`, push and deploy once the work was done. The plan and its steps are in
[infobox-schemas-plan.md](infobox-schemas-plan.md).

## Change

`9db4c3f` (`Add schema-driven {{Infobox <Type>}} with per-site JSON schemas`):

- `{{Infobox <Type>}}` replaces the issue-only `{{PublicationIssueInfobox}}`, which stays as an alias for
  `{{Infobox PublicationIssue}}`. Each site's types, field order and labels per culture live in
  `src/WikiWikiWorld.Web/InfoboxSchemas/<site>.json`, identified by `SiteId` and validated at startup by
  `InfoboxSchemaCatalog`. Magazedia (`magazedia.json`) defines `Publication` and `PublicationIssue`.
- Existing infoboxes render the same rows and values, now in schema order: Cover Date first, ISSN before the
  barcodes. The old hard-coded English labels match the schema's English labels.
- Problems (unclosed, missing or undefined type, unknown key, repeated non-repeatable field) never block a
  save. **API contract change:** a successful `POST` or `PUT /api/article/{UrlSlug}` now returns
  `{"Message": "...", "Warnings": [...]}` instead of a plain string. The web editor shows warnings once on
  the saved article to the person who saved it.

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `9db4c3f208d4` on `main`, pushed to `origin/main` before publishing. It builds on `dd41a9d`.
- Release: `release-20261118T205740Z-9db4c3f208d4`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:b81211159b4e5f1e3f8042c5d29805ecfe1ed81c97979675ec7be352e4821204`,
  published with the release tag and `latest`, pulled by tag on the host and run by digest through
  `/home/deploy/update.sh`. Started `2026-11-18T20:58:28Z`. The host's `update.sh` matches the repository copy.
- Before the swap, `docker cp` from a created (not started) container confirmed `/app/InfoboxSchemas/magazedia.json`
  is in the image.

## Verification

- Release build passed with only the existing ImageSharp NU1902/NU1903 advisories; all 408 .NET tests passed.
- Development: startup with the schema file; refusal on a duplicate `SiteId` and on a misspelt property; a
  warning and skip for a `SiteId` not configured in development; the file present in `dotnet publish` output.
  Pages checked: a Publication and a PublicationIssue infobox in English, Japanese and Arabic labels (with
  English fallbacks), the legacy name, a misspelt key, and API and editor warnings. All rows of 12 existing
  infobox articles whose dev and production text matched were identical to production apart from order.
- Production, anonymous: 200 for English, root, Japanese and Arabic Magazedia, Cinepedia, Fanfare, CRTR, `032c`,
  `wiki:infobox` and the sitemap. `edge-issue-no-1`, `playboy-usa-february-2025`, `gq-style-usa-fall-2016` and
  `cinefex-no-1` render their infobox rows in schema order with no leftover `{{`. Zero restarts and no `fail:`,
  `crit:` or exception log lines.
- Not verified in production: an API save returning warnings, and the editor notice, because no production
  article had been edited yet.

## Production articles (2026-11-19)

Step 9 of the plan. The search API found 30 current Magazedia articles using `{{PublicationIssueInfobox`
(none on the Japanese, Arabic or Cinepedia sites). Each was saved before editing, re-read just before its `PUT`
and skipped if it had changed (none had), and edited in its own line endings. Every save returned `"Warnings": []`,
the saved text matched what was sent, and the rendered page has no `{{` or raw `##`.

- **Renamed to `{{Infobox PublicationIssue`, values untouched (20):** `vogue-usa-october-2026` and the 19 GQ Style
  (USA) issue pages from Spring/Summer 2014 to Fall/Winter 2021. Their citations stay inside the values.
- **Renamed, placeholder rows dropped (3):** `edge-issue-no-1`, `cinefex-no-1` and `playboy-usa-february-2025` lost
  their `Unknown` rows. Other values were kept as written.
- **Placeholder block removed (6):** `playgirl-1973`, `playgirl-1955-1957-magazine`, `playgirl-1955-1957`,
  `gq-style`, `sneaker-freaker-russia` and `sugar-ape` carried issue facts copied onto a publication article
  (`Unknown`, `Fictional`, the cover date as a print date).
- **Changed to `{{Infobox Publication` (1):** `gq-style-usa`, a publication article whose only row is its cited ISSN.

Afterwards a search for the old name finds only `wiki:infobox`, which documents the alias. The 20
`{{Infobox Publication}}` boxes agents had written since the release use only schema keys, with no repeated
non-repeating key and no citations inside.

Left for an editor:

- `edge-issue-no-1` and `cinefex-no-1` give `PrintPublicationDate` equal to `CoverDate`, which is probably copied.
  Edge's £3.00 price and ISSN 1350-1593 are not supported by its text.
- `playgirl-1955-1957-magazine`, `sneaker-freaker-russia` and `gq-style` show `cover-of-gq-style-usa-summer-2017`
  in their CoverCarousel, which is the wrong cover.
- `playgirl-1955-1957` and `playgirl-1955-1957-magazine` look like duplicate articles.
- `gq-style-usa` puts its infobox before the first paragraph.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261118T205740Z-9db4c3f208d4`
(`sha256:7ebcbff67be10ea75d4a06659e5e3009f82b6e2221a5687e2a78c5912a89c92f`). Pass it to `/home/deploy/update.sh`;
no database restore is needed. Articles written with `{{Infobox …}}` after this release would show as raw text
on the old image.
