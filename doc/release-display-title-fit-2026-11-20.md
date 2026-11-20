# Display title fit on narrow screens — release 2026-11-20

The owner approved pushing and deploying the fix ("Push and deploy").

## Change

- `4f09c4c`: article and Links page headers shrink below `--step-6` only when the title's widest unbreakable
  word would not fit the heading's width. `DisplayTitleHelper.BuildFitStyle` estimates that word on the server
  and emits `--title-word-em` and `--title-word-chars` as an inline style on the `h1`. Logo tokens are left out
  of the estimate; CJK characters and slashes count as break points; Arabic letters count as narrower.
  `overflow-wrap: break-word` is back as a last resort for a word that still cannot fit a line. The `hgroup` is
  now an inline-size container so the heading measures against its real width (`cqi`).
- `9191800`: the `cqi` width applies only inside `@supports (width: 1cqi)`; older browsers keep the
  viewport-based estimate instead of an invalid `font-size`.

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `919180040746` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261120T063622Z-919180040746`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:36d26ef6f42cae97635cd4e8916b4983c8151dee86514810100ece783ff86d19`
  - Published with the release tag and `latest`.
  - Pulled by tag on the host and run by digest through `/home/deploy/update.sh`.
  - Started `2026-11-20T06:37:05Z`.

## Verification

- Before shipping: all 408 .NET tests passed. On the development server, American Cinematographer's heading
  was 28.4px at 320px, 31.7 at 360, 32.9 at 375, 64.7 at 768 and 79.5 at 1024, with no overflow. Logo,
  Japanese and Arabic headers kept their size, and header geometry was unchanged on article and upload pages.
- Production, anonymous:
  - 200 for en/ja/ar Magazedia home, `american-cinematographer` and its Links page, `stash`,
    `032c-no-10-winter-2005-2006`, the en sitemap, Cinepedia and CRTR (Fanfare redirects as before).
  - `american-cinematographer` has `style="--title-word-em: 11.74; --title-word-chars: 15"` on its `h1`.
  - At a 320px viewport in Chromium the heading is 28.4px, 286px wide, with no horizontal page overflow.
  - Zero restarts and no `fail:`, `crit:` or exception log lines.
- Not verified: real iOS Safari and Android browsers.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261120T063622Z-919180040746`
(`sha256:b6d124fab060…`, the display-title scope release). Pass it to `/home/deploy/update.sh`; no database
restore is needed.
