# iOS search suggestion release

Deployed commit `18f08c55a272` at 2:33 p.m. CST on 2026-11-27 (`2026-11-27T20:33:57Z`). The header search keeps focus on the input through the suggestion's native click, so iOS Safari can follow the link before the dropdown closes. The release requires no schema change.

| Image | Tag in GHCR | Digest |
| --- | --- | --- |
| iOS search suggestions | `release-20261127T203319Z-18f08c55a272` | `sha256:35819cb08e1a6c1815763d7d6a443d447b4e704a0b04f549bcf2adf864860a9e` |
| Previous production image | `rollback-before-release-20261127T203319Z-18f08c55a272` | `sha256:eb793dff60964ee3db5cd1a50b73efc2e6c32631e0947ce2586f6ac8448f420a` |

## Verification

- All 474 .NET tests and the localization check passed. The browser-script suite reported 17 unrelated failures in the recipient autocomplete and admin article picker.
- The live site's cache-versioned search script matched the committed file. On the live site, WebKit at iPhone 13 mini portrait dimensions and desktop Chromium followed both a suggestion and the Search all link. Escape, outside dismissal, Tab, arrow-key selection and Enter-to-search also passed. The native form submitted with JavaScript disabled in Chromium. Physical iPhone Safari remains unverified.
- Production HTTPS checks passed. The container ran as UID 1654 with zero restarts and no critical errors or unhandled exceptions in the startup logs. GHCR `latest` points to the verified release digest.

## Rollback

The previous image is pinned in GHCR and on the production host. Restore it with the host update script.

```sh
ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T203319Z-18f08c55a272'
```

Verify the container and public HTTPS endpoints, then restore the registry's `latest` tag to the previous digest.
