# Stats script on en.magazedia.com — release 2026-11-26

The owner asked to load the stats script only on en.magazedia.com, then asked to deploy.

## Change

- `45fbb50`: `Pages/Shared/_Layout.cshtml` adds
  `<script defer src="https://stats.magazedia.com/core.js" data-website-id="80f32dd9-…">` to `<head>` only when the
  request host is exactly `en.magazedia.com`. Other cultures, the culture-selector root, the other sites and
  development get nothing.

No database change. Nothing under `deploy/` changed. The only other commits since the previous release
(`ac5e242`) touch prompts and docs.

## Source and deployment

- Source: `45fbb504583b` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261126T054004Z-45fbb504583b`, published with `latest`.
- Image ID on the host: `sha256:ce6671cdf21b631397b3744433da4cc0e66dd3a42cdc194dc238e42fe2af386d`. Started
  `2026-11-26T05:40:40Z`.

## Verification

- All 436 tests passed.
- Production: 200 for en, ja, the root and fanfare.wiki home pages. The script tag is on en only, with zero on the
  other three. A missing page returns 404. Zero restarts, and no `fail:`, `crit:` or exception log lines.

## Open problem: the stats host's certificate

`https://stats.magazedia.com/core.js` fails TLS. The server at 163.192.213.175 presents a Let's Encrypt
certificate for `stats.lily-allmylinks.com`, `stats.cityporches.com` and `stats.lily.fans` only, so browsers
will refuse to load the script and nothing will be counted until that server's certificate includes
`stats.magazedia.com`. That is fixed on the stats server, not in this repository.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T054004Z-45fbb504583b` (the prompt guard
release, `sha256:9fa33a3c…`) to `/home/deploy/update.sh`, then point `latest` at it.
