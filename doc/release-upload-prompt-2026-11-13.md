# Issue-only publication upload prompt — release 2026-11-13

The owner asked to commit, push and deploy.

## Change

`353e90a` (`Show the publication upload prompt only on issue articles`). The "Have a copy of this
publication you can legally share?" aside now needs all three of:

- a standard `Article` revision;
- an eyebrow of `MAGAZINEISSUE`, `ZINEISSUE`, `JOURNALISSUE` or `NEWSLETTERISSUE`
  (`EyebrowExtension.IsPublicationIssue`);
- no `{{DownloadsBox ` in the text.

Before this it showed on every standard article without a DownloadsBox, including publication,
film and person pages. `EyebrowExtension.Enrich` now also stores the parsed type under
`EyebrowExtension.TypeDocumentKey`, exposed as `RenderedArticle.Eyebrow`.

## Source and deployment

- Source: `353e90a05155` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261113T201725Z-353e90a05155`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:9acbcf8887696560403488b97defb6d139f89e30a6b0fdaea092448ba70cf858`
  (linux/amd64, user 1654), published with the release tag and `latest` on
  `resolute-chiseled-extra`. The host pulled the release tag before `/home/deploy/update.sh`
  replaced the container by digest. Started `2026-11-13T20:18:20Z`.
- No database change. Nothing under `deploy/` changed, and the host's `update.sh` matches the
  repository copy.

## Verification

- Release build passed with only the existing ImageSharp NU1902/NU1903 advisories; all 395 .NET
  tests passed.
- Development: the aside showed on issue pages without a DownloadsBox and was hidden on a
  publication page and on an issue with a DownloadsBox.
- Production, anonymous: shown on `outlander-issue-01`, `outlander-issue-02`,
  `gq-style-usa-fall-2016` and `vogue-usa-october-2026`; hidden on `outlander`, `vogue-usa`
  (MAGAZINE eyebrow), `gq-style-usa` and `cinefex-no-1` (no eyebrow), `gq-style-usa-summer-2017`
  (DownloadsBox) and Cinepedia's `spider-man-no-way-home` (MOVIE). Zero restarts and no
  `fail:`, `crit:` or exception log lines.
- Not verified: zine, journal and newsletter issue pages, because none exist yet.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261113T201725Z-353e90a05155`
(`sha256:840728d12e03515c507d049b40116b7be65ffb2f2e983c9209e976650bf1c128`,
`release-20261111T172401Z-dae5d261ea47`). Pass it to `/home/deploy/update.sh` to roll back; no
database restore is needed.
