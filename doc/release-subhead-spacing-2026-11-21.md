# Subhead leading and tracking — release 2026-11-21

The owner approved pushing and deploying to production ("push and deploy it").

## Change

- `9a25d32`: `css/core/typography.css` gives `h2, h3, h4` a 1.2 line-height and 0.01em letter-spacing (was the reset's 1.1 and the font's native fit). The heading face, Lyon Display Black, is cut tight for very large sizes, so wrapped article subheads looked crushed. Components that set their own heading spacing are unchanged.

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `9a25d326114f` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261121T235742Z-9a25d326114f`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:96dd642e01550df16198a175d512701bf1a9068a57c1cabaf979a433978f3750`
  - Published with `latest` and the release tag on `resolute-chiseled-extra`.
  - Pulled by tag on the host and started by image ID through `/home/deploy/update.sh`.
  - Started `2026-11-21T23:58:25.782520395Z`, running as UID `1654`.

## Verification

- Development: checked in the browser on `/starlog` with a wrapped two-line h2; line-height computed to 1.2 and the subhead no longer looks cramped.
- Production:
  - Anonymous HTTPS checks returned HTTP 200 on `en.magazedia.com/`, `ja.magazedia.com/`, `en.cinepedia.wiki/`, `crtr.world/` and `en.magazedia.com/starlog`.
  - The served `/css/core/typography.css` contains the new `h2, h3, h4` rule.
  - Container `wikiwikiworld` shows `RestartCount` `0` and no `fail:`, `crit:` or exception lines in `docker logs`.
- Not run: the test suite (CSS-only change; the Release publish build succeeded). Other pages whose h2–h4 inherited the old 1.1 leading were not visually checked.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261121T235742Z-9a25d326114f`
(`sha256:49f423af2463907bb82764ca3d6f73f5d565f34768eba121b468ffcf5340ad40`, release `release-20261120T184602Z-083a5f6b227e`). Pass it to `/home/deploy/update.sh`; no database restore is needed.
