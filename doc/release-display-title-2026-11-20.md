# Display title scope and docs — release 2026-11-20

The owner approved pushing and deploying all outstanding changes ("push and deploy all the outstanding changes").

## Change

- `470bef3`: the HTML sitemap and the upload page ("You are submitting a copy for…") name articles by `Title`.
  Before, they showed a flattened `DisplayTitle`, so `032c\n№ 1\nWinter 2000` read as `032c № 1 Winter 2000`.
  `DisplayTitle` is now used only by the article header and the Links page header, which reuses it.
- `aa75b0d`, `d32489d`: [doc/api.md](api.md#display-titles) and the Magazedia authoring standards in PROJECT.md
  say `DisplayTitle` is plain text for the header only, never Markdown. Line breaks replace ` - ` separators,
  and logo tokens are allowed. Agents had been writing `*Stash*`, which shows the asterisks.
- `7025837`: documentation only (infobox placement, authoring standards, the 2026-11-19 infobox article migration).

No database change. The sitemap's covering index `IX_ArticleRevisions_Sitemap_Html` still includes `DisplayTitle`
as its last column; the query no longer reads it, and the index is still used. Nothing under `deploy/` changed.

Separately, before this release, the production `stash` article's `DisplayTitle` was changed from `*Stash*` to
`Stash` through the API as MaggieTheBot (a normal, revertible revision).

## Source and deployment

- Source: `470bef3a4eae` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261120T040446Z-470bef3a4eae`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:b6d124fab060e75471ea3a32a0f07ba2f3321fd74be79b57fe597eb3908f24b7`
  - Published with the release tag and `latest`.
  - Pulled by tag on the host and run by digest through `/home/deploy/update.sh`, which matches the repository copy.
  - Started `2026-11-20T04:05:23Z`.

## Verification

- Before shipping: all 408 .NET tests passed. On the development server the sitemap's 0–9 bucket listed
  `032c - № 1 - Winter 2000`, and the upload page for that article named it the same way.
- Production, anonymous:
  - 200 for en Magazedia home, `stash`, `american-cinematographer`, the en/ja/ar sitemaps and the en 0–9 bucket,
    Cinepedia, Fanfare and CRTR.
  - The 0–9 sitemap bucket lists `032c - № 1 - Winter 2000`.
  - `stash` has `<h1 …>Stash</h1>`.
  - Zero restarts and no `fail:`, `crit:` or exception log lines.
- Not verified in production: the upload page, which needs a signed-in account.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261120T040446Z-470bef3a4eae`
(`sha256:786b89e5212a…`, the Arabic and Japanese UI release). Pass it to `/home/deploy/update.sh`; no database
restore is needed.
