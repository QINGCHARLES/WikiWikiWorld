# XML sitemap cache — release 2026-11-25

The owner asked to shorten the XML sitemap cache to 1 hour, then to deploy.

## Change

- `749e086`: `SitemapService.XmlCacheDuration` drops from 6 hours to 1, matching the HTML sitemap. Saving an article
  does not clear these caches, so the duration is how long a new article can be missing from `/sitemap.xml`. It also
  covers `/sitemaps/static.xml` and the chunk boundaries used once the sitemap is split.
  - Found during the article audit: the XML listed 171 articles against 179 in the HTML sitemap. Every missing
    article had been created after the cached copy was built (newest entry 20:02 UTC). Nothing was filtered out.
- `5748af2`: prompt wording only (familiar and distinguishing company suffixes may stay in article prose).

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `749e08646f49` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261125T234338Z-749e08646f49`, published with `latest`. A created container confirmed the image
  has no `appsettings.Development.json`.
- Image ID on the host: `sha256:07c89444c947ffbb9ddf57296dca55571520b5afadfe1aab11acecc4139773b8`. Started
  `2026-11-25T23:44:19Z`.

## Verification

- All 436 tests passed.
- Production:
  - 200 for the en and ja home pages, `/sitemap.xml` and `/beckett-baseball`; a missing page returns 404.
  - `/sitemap.xml` lists 423 URLs (388 before the restart), including `beckett-baseball`, `beads-and-beyond` and
    `buses-worldwide`.
  - Zero restarts, and no `fail:`, `crit:` or exception log lines in the first minutes.

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261125T234338Z-749e08646f49` (the timeline title
release, `sha256:18f447e3…`) to `/home/deploy/update.sh`, then point `latest` at it.
