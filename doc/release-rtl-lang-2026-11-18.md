# Document language and right-to-left layout — release 2026-11-18

The owner asked to commit to `main`, push and deploy.

## Change

- `d702fff` (`Set document lang and dir from the request culture`): `_Layout.cshtml` emits `lang` from the
  resolved culture (falling back to `en` on the culture-selector root) and `dir="rtl"` for right-to-left
  cultures via `SiteCultureHelper.IsRightToLeft` (ICU; unknown codes are left to right). English pages keep
  `lang="en"` with no `dir`. CSS made logical where Arabic visibly broke: article asides (infobox, cover
  carousel, issue credits) float to the inline end; the title `hgroup` centres with equal negative inline
  margins; carousel arrows sit on logical sides, mirror their icons and scroll with negated offsets in RTL;
  dropdown menus anchor to the inline end; tables and bulleted lists align to the inline start.
- `1060681` (`Centre the article hero image with inline margins so it mirrors in RTL`): same centring for the
  hero figure.

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `1060681e2a69` on `main`, pushed to `origin/main` before publishing. It builds on `6148289`.
- Release: `release-20261118T212626Z-1060681e2a69`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:fc94026e48bb42607e160060e4ad6687a115f19b63fbee87ba0ea7ea7e7d5a4d`,
  published with the release tag and `latest`, pulled by tag on the host and run by digest through
  `/home/deploy/update.sh`. Started `2026-11-18T21:27:16Z`. The host's `update.sh` matches the repository copy.
- Publishing from Git Bash needs `-t:PublishContainer`; `/t:` is rewritten into a path and MSBuild rejects it.

## Verification

- All 408 .NET tests passed on the rebased source.
- Development (worktree wwwroot on port 7130): ar pages `lang="ar" dir="rtl"` with infobox, carousel and credits
  floating left, title and hero centred, carousel next/prev/dots scrolling, account menu on-screen, no overflow
  at 375px; `infobox-ar-test` floats both infoboxes left. ja `lang="ja"`; xx-test renders. English geometry of
  the title, hero, asides and menus measured identical to the previous rules.
- Production, anonymous: 200 for English, root, Japanese and Arabic Magazedia, Cinepedia, Fanfare, CRTR, `032c`,
  `game-informer` and the sitemap. `<html>` is `lang="en"` on English sites and the root, `lang="ja"` on
  Japanese and `lang="ar" dir="rtl"` on Arabic. The served stylesheets and `carousel.js` contain the new rules.
  `game-informer` hero and title are centred on the text column. Zero restarts and no `fail:`, `crit:` or
  exception log lines.
- Not verified in production: an Arabic article with an infobox or carousel, because none exists there yet.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261118T212626Z-1060681e2a69`
(`sha256:b81211159b4e5f1e3f8042c5d29805ecfe1ed81c97979675ec7be352e4821204`, the infobox release). Pass it to
`/home/deploy/update.sh`; no database restore is needed.

## Follow-ups

- `components/avatar.css` stacked editor avatars and `components/timeline.css` still use physical offsets.
- Split-button corner radii in `navigation.css` are physical.
- Untranslated English UI strings on Arabic pages show trailing punctuation at the start of the line.
