# Arabic and Japanese UI, RTL follow-ups — release 2026-11-19

The owner held the previous follow-ups back ("Don't ship it until we fix the other issues"), chose to translate the
whole UI, and then approved shipping ("Ship it").

## Change

- `7e3fa4f`: stacked editor avatars and the timeline use logical offsets, padding and borders, so they mirror in RTL.
- `7b778bb`: split-button, stub and file-info corner radii are logical.
- `61591e1`, `8d9e436`, `60bd89b`, `6fa9a3d`, `7a4bf59`: the site UI is localised into Arabic and Japanese:
  - one shared resource set (`Resources/SharedResource*.resx`) keyed by the English text;
  - `UiCultureMiddleware` sets the UI culture from the site culture;
  - CLDR plural forms, translated validation messages and Identity errors;
  - text shown by page scripts, passed in `data-ui-text` / `data-…-text` attributes;
  - labels inside rendered article content (citations, credits, stub, redirects and others).
  - English output is unchanged apart from three fixes: "1 change", "Found N results" and the history author links.
  - The privacy policy stays in English, marked `lang="en"`.
  - `scripts/localization.py check` reports missing translations and case-only key clashes.
  - PROJECT.md now explains how the UI languages work.

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `7a4bf591e5b6` on `main`, fast-forwarded from `claude/silly-curran-d39869` and pushed to `origin/main`
  before publishing.
- Release: `release-20261119T050124Z-7a4bf591e5b6`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:786b89e5212af6a8dd7542e88b7c66f176e644eadfcb483902da7b186484d7b6`
  - Published with the release tag and `latest`.
  - Pulled by tag on the host and run by digest through `/home/deploy/update.sh`.
  - Started `2026-11-19T05:02:11Z`.
- The image contains `/app/ar/` and `/app/ja/` `WikiWikiWorld.Web.resources.dll`, checked with `docker cp` from a created
  container.

## Verification

- Before shipping:
  - All 408 .NET tests passed.
  - The localisation checker reported nothing missing and no clashes.
  - English pages matched the previous build, both signed out and signed in.
  - Arabic and Japanese pages had no leftover English UI text.
  - In the browser: Arabic validation, search suggestion counts, the firehose live status and the header helper.
- Production, anonymous:
  - On en, ja and ar Magazedia, the sitemap, login, firehose and search pages return 200.
  - Titles are translated, e.g. `خريطة الموقع` and `サイトマップ`.
  - `<html>` is `lang="ja"` on Japanese and `lang="ar" dir="rtl"` on Arabic.
  - Arabic login validation attributes are in Arabic.
  - `en.magazedia.com/game-informer` returns 200.
  - On ja and ar, missing articles show a translated 404 page.
  - Zero restarts.
- The only `fail:` log lines came from probing `magazedia.com/032c` on the bare root host, which returned 500.
  - That host has no culture, so `SiteResolverService` throws.
  - The error page then fails because `Error` does not derive from `BasePageModel`, which `_Layout` requires.
  - The `@model BasePageModel` declaration predates this release, but I did not reproduce the 500 on the
    previous image.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261119T050124Z-7a4bf591e5b6`
(`fc94026e48bb…`, the RTL layout release). Pass it to `/home/deploy/update.sh`; no database restore is needed.

## Follow-ups

- Have native speakers review the Arabic and Japanese translations, the upload page's legal wording first.
- The bare-root 500 above: unknown paths on the culture-selector host should get a 404 or a redirect.
- Sitemap letter buckets are Latin A–Z.
- The Terms and DMCA links have no pages.
- The footer tagline says "Magazedia" on every site.
