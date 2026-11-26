# Subhead line-height adjustment — release 2026-11-26

Deployed at `2026-11-26T19:15:10Z` from commit `9fc22a92cfd2` (`9fc22a92cfd2a776b3ec11e8c059bd84ddc9887a` on `main`), increasing `line-height` on `h2, h3, h4` in `src/WikiWikiWorld.Web/wwwroot/css/core/typography.css` from `1.2` to `1.35`.

This release changes static CSS only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Subhead line-height release from `9fc22a92cfd2` | `release-20261126T191430Z-9fc22a92cfd2` | `sha256:14656ac0a7bf51d49459c5733591be8b59b91c7543454c540372bb7e3b3a22f1` |
| Previous production image (`dc06539d996e`) | `rollback-before-release-20261126T191430Z-9fc22a92cfd2` (pinned on host; also `release-20261126T181210Z-dc06539d996e` in GHCR) | `sha256:0f4bf8a101890d463a71e9d93249558dd9c450ccf0762ea4520284ecb41cd7a4` |

## Verification

- All 442 .NET tests (`dotnet test tests/WikiWikiWorld.Web.Tests -c Release`) and `python scripts/localization.py check` passed before release.
- Pinned the previous running image (`sha256:0f4bf8a101890d463a71e9d93249558dd9c450ccf0762ea4520284ecb41cd7a4`) on the production host as `rollback-before-release-20261126T191430Z-9fc22a92cfd2` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com` (`/`, `/Search?q=gq`), `magazedia.com`, `ja.magazedia.com`, `en.cinepedia.wiki`, and `fanfare.wiki`, HTTP 404 on a missing slug, and versioned `/css/core/typography.css?v=xBWZyIavKXwpSzGdIb2k-dMNLcx44rRW70I_3_2sKYU` serving `line-height: 1.35;`.
- Verified container running with `restarts=0`, then promoted GHCR `latest`.

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T191430Z-9fc22a92cfd2'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:0f4bf8a101890d463a71e9d93249558dd9c450ccf0762ea4520284ecb41cd7a4` (`release-20261126T181210Z-dc06539d996e`) before the next routine deploy.
