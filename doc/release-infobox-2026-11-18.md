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

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261118T205740Z-9db4c3f208d4`
(`sha256:7ebcbff67be10ea75d4a06659e5e3009f82b6e2221a5687e2a78c5912a89c92f`). Pass it to `/home/deploy/update.sh`;
no database restore is needed. Articles written with `{{Infobox …}}` after this release would show as raw text
on the old image.
