# Block HEIC/HEIF image uploads — release 2026-11-20

The owner approved blocking HEIC uploads and pushing/deploying to production ("Okay, push and deploy all to... production").

## Change

- `083a5f6`: blocks HEIC/HEIF uploads across the web editor and API and aligns editor `<input type="file" accept>` and help text with the supported web-baseline formats (`JPG, PNG, GIF, WebP, AVIF, SVG - max 10MB`):
  - `ImageValidationHelper`: removed `.heic` from `AllowedImageExtensions`, `image/heic` from `AllowedMimeTypes`, and `.heic` from `IsImageFileSignature`; added `AcceptedFileTypes`.
  - `CreateEdit.cshtml`: switched both file inputs from `accept="image/*"` to `AcceptedFileTypes` and updated Featured Image / Replace Image help text.
  - `SharedResource.ar.resx` & `SharedResource.ja.resx`: updated Arabic and Japanese translations for the editor help text and removed superseded keys via `scripts/localization.py merge`.
  - `ImageValidationTests`: added unit tests covering HEIC/HEIF rejection, allowlists, and valid baseline formats.
  - `/wiki:header-helper` remains unchanged (it converts HEIC to AVIF locally in the browser and never uploads HEIC).

No database change. Nothing under `deploy/` changed.

## Source and deployment

- Source: `083a5f6b227e` on `main`, pushed to `origin/main` before publishing.
- Release: `release-20261120T184602Z-083a5f6b227e`.
- Production image:
  `ghcr.io/qingcharles/wikiwikiworld@sha256:49f423af2463907bb82764ca3d6f73f5d565f34768eba121b468ffcf5340ad40`
  - Published with `latest` and the release tag on `mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra`.
  - Pulled by tag on the host and started by digest through `/home/deploy/update.sh`.
  - Started `2026-11-20T18:47:20.745153877Z`, running as UID `1654`.

## Verification

- Before shipping:
  - `python scripts/localization.py check` reported `0 missing, 0 empty, 0 unused` across `SharedResource.resx`, `SharedResource.ar.resx`, and `SharedResource.ja.resx`.
  - `dotnet test tests/WikiWikiWorld.Web.Tests -c Release` passed all 425 tests (0 failed, 0 skipped; existing `SixLabors.ImageSharp` NU1902/NU1903 warnings only).
- Production:
  - Anonymous HTTPS checks returned HTTP 200 on `en.magazedia.com/`, `ja.magazedia.com/`, `ar.magazedia.com/`, `en.cinepedia.wiki/`, `crtr.world/`, `en.magazedia.com/wiki:header-helper`, and `en.magazedia.com/sitemap.xml`.
  - Live authenticated API probe (`POST https://en.magazedia.com/api/file/__heic-block-probe` with `probe.heic`, `image/heic`, `ftypheic` bytes) returned HTTP 400 (`"File extension .heic is not allowed. Allowed extensions: .jpg, .jpeg, .png, .gif, .webp, .avif, .svg"`).
  - Container `wikiwikiworld` shows `RestartCount` `0` and no `fail:`, `crit:`, or exception lines in `docker logs`.
- Not verified: interactive signed-in browser rendering of `/create:` and Arabic/Japanese editor pages; Arabic and Japanese UI strings are AI-drafted.

## Rollback

Previous image retained on the host as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261120T184602Z-083a5f6b227e`
(`sha256:36d26ef6f42cae97635cd4e8916b4983c8151dee86514810100ece783ff86d19`, release `release-20261120T063622Z-919180040746`). Pass it to `/home/deploy/update.sh`; no database restore is needed.
