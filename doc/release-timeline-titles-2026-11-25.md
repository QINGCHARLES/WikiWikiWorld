# Timeline title emphasis — release 2026-11-25

The owner asked to make timeline titles show italics, then to deploy.

## Change

- `ab2dff6`: `{{Timeline}}` event titles render `*italic*` and `**bold**` through the shared `InlineEmphasis`
  formatter (moved out of `PullQuote`). Everything else in a title stays HTML-encoded; dates are unchanged.
  The docs and the Magazedia prompt now say to italicize publication names in titles.
- `7564738` (already on `main`, unreleased): history, languages and backlinks pages carry `noindex`.

No database change. Nothing under `deploy/` changed. The live `wiki:timeline` help page still has the old wording;
the seed script only creates missing pages.

## Source and deployment

- Source: `ab2dff6c16a0` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261125T202828Z-ab2dff6c16a0`, published with `latest`. A created container confirmed the image
  has no `appsettings.Development.json`.
- Image ID on the host: `sha256:18f447e39b538bcc4ace686d7a0b0b0f501dba8ad69916b0eb2ca32a2c107471`. Started
  `2026-11-25T20:29:26Z`.

## Verification

- All 436 tests passed. In development, a test article showed italic and bold titles, a lone `*` and HTML left
  literal, and dates unformatted; the test article was then deleted.
- Production:
  - 200 for the en and ja home pages and the sitemap; a missing page returns 404.
  - `/american-outdoor-guide` renders `Rename to <em>American Outdoor Guide</em>`, `<em>Boundless</em> issue line ends`
    and `Launch as <em>Shooter's Journal</em>`.
  - `/american-outdoor-guide/history` carries `<meta name="robots" content="noindex">`.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines in the first minutes.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261125T202828Z-ab2dff6c16a0` (the API status-code
release, `sha256:d9ca453a…`) to `/home/deploy/update.sh`, then point `latest` at it.
