# Mobile title extrusion release

Deployed commit `7d509a97a4fa` at 2:24 p.m. CST on 2026-11-27 (20:24:37 UTC). At viewport widths of 30rem or less, article title text and SVG logos use two one-pixel extrusion layers and a shorter soft shadow. WebKit emulation rounded fractional shadow offsets to zero, so the mobile style keeps whole-pixel layers. The release requires no schema change.

| Image | Tag in GHCR | Digest |
| --- | --- | --- |
| Mobile title extrusion | `release-20261127T202342Z-7d509a97a4fa` | `sha256:eb793dff60964ee3db5cd1a50b73efc2e6c32631e0947ce2586f6ac8448f420a` |
| Previous production image | `rollback-before-release-20261127T202342Z-7d509a97a4fa` | `sha256:38d04e7a8221792be2eaba69654082a066f849a731e5b9b3e21ed7d7d8906229` |

## Verification

- All 474 .NET tests and the localization check passed. The Linux x64 chiseled image included the updated stylesheets.
- On the live site, WebKit rendered the title on `/american-cinematographer` and the SVG logo on `/playboy` with two visible extrusion layers at 375 × 812 and the original four layers at 3440 × 1440. Physical iPhone Safari was unverified.
- Public HTTPS checks passed for Magazedia, Cinepedia, Fanfare and CRTR. A missing article returned HTTP 404. The display-title help page listed cache-versioned copies of both updated stylesheets.
- The container ran as UID 1654 with zero restarts and no critical errors or unhandled exceptions in the startup logs. The registry's `latest` tag was promoted to the verified release digest.

## Rollback

The previous image is pinned in GHCR and on the production host. Restore it with the host update script.

```sh
ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T202342Z-7d509a97a4fa'
```

Verify the container and public HTTPS endpoints, then restore the registry's `latest` tag to the previous digest.
