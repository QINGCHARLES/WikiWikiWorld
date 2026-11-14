# Unified button system, brand lime tokens, centralized colors, and bookmark UI — release 2026-11-14

The owner asked to commit, push and deploy.

## Changes

`e3dc54f` (`Unify button system, brand lime tokens, centralize colors, and refine bookmark UI`):

- Refined the bookmark menu, note modal, inline note editor, and bookmark browser UI (`_Bookmark.cshtml`, `_BookmarkBrowser.cshtml`, `View.cshtml.cs`, `bookmark-notes.js`, `bookmarks.js`, `navigation.css`, `profile.css`, `header.css`).
- Unified button variants and contrast states in `buttons.css` and aligned button usage across `Login.cshtml`, `Register.cshtml`, `DomainRedirects.cshtml`, `CreateEdit.cshtml`, `History.cshtml`, `NotFound.cshtml`, and `Upload.cshtml`.
- Promoted the brand lime palette (`--color-brand-lime`, `--color-brand-lime-hover`, `--color-brand-lime-surface`, `--color-brand-lime-text` derived via OKLCH relative color syntax for WCAG AA contrast on light panels and white, and `--color-brand-ink`), updating pull quotes (`pullquote.css`) and issue contents page numbers (`issue-contents.css`) to `--color-brand-lime-text`.
- Removed live hard-coded color literals outside `core/colors.css` and `core/variables.css`, moving paper, surface, border, status, medal, focus-pill, and canvas preview colors into CSS custom properties.

## Source and deployment

- Source: `e3dc54fbda25` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261114T060635Z-e3dc54fbda25`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:e2ffa2726f2543f83dbba660b2a28de727999c37ba3d34a10217e969f423dad8`
  (`resolute-chiseled-extra`, `Release`), pushed with `release-20261114T060635Z-e3dc54fbda25` and `latest`.
- `/home/deploy/update.sh` replaced container `wikiwikiworld` by digest (`StartedAt=2026-11-14T06:07:47.213502139Z`).
- No database change. Nothing under `deploy/` changed.

## Verification

- Release tests: `dotnet test tests/WikiWikiWorld.Web.Tests -c Release` passed (`395` passed, `0` failed, `0` skipped); browser script tests (`node --test tests/browser/*.test.cjs`) passed (`27` passed, `0` failed).
- Production container reported `RestartCount=0` with clean startup logs.
- Anonymous HTTPS checks returned `200 OK` on `https://en.magazedia.com/` and `https://en.cinepedia.wiki/`, and verified the deployed CSS tokens in `/css/core/variables.css`, `/css/components/pullquote.css`, and `/css/components/issue-contents.css`.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261114T060635Z-e3dc54fbda25`
(`sha256:9acbcf8887696560403488b97defb6d139f89e30a6b0fdaea092448ba70cf858`). Pass it to `/home/deploy/update.sh` to roll back; no database restore is needed.
