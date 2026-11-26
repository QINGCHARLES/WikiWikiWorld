# Multi-color and light/dark DisplayTitle logos — release 2026-11-26

Deployed at `2026-11-26T18:14:12Z` from commit `dc06539d996e` (`dc06539d996e51752c5c6147752a5f7d5a44b957` on `main`), adding unmasked multi-color `DisplayTitle` logo tokens (`[[Fallback|#|file-slug|#|color]]`) and light/dark logo pairs (`[[Fallback|#|light-slug|~|dark-slug]]` or `[[Fallback|#|light-slug, dark-slug]]`), along with file-backlink matching on `/file:{slug}/links` and the live `/wiki:display-title` help page.

This release changes application files only (`f743c6e` table vertical margin + `dc06539` `DisplayTitle` logo support). No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| DisplayTitle logos release from `dc06539d996e` | `release-20261126T181210Z-dc06539d996e` | `sha256:0f4bf8a101890d463a71e9d93249558dd9c450ccf0762ea4520284ecb41cd7a4` |
| Previous production image (`96fd95c045cd`) | `rollback-before-release-20261126T181210Z-dc06539d996e` | `sha256:f1add1903409f6dce849c9626acecb0ae1cab342fe1aa6559365409b6ed00c7d` |

## Verification

- All 442 .NET tests (`dotnet test tests/WikiWikiWorld.Web.Tests -c Release`) and `python scripts/localization.py check` passed before release.
- Confirmed `/home/deploy/update.sh` on the host matched `deploy/update.sh` (`9e96f7d71f3848bbf93b665a071b9ed20bb81064b0c3b9eeb9495db428a93cba`), pinned the rollback tag both in GHCR and on disk, and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com` (`/`, `/Search?q=gq`, `/wiki:display-title`, `/wiki:formatting`), `magazedia.com`, `ja.magazedia.com`, `en.cinepedia.wiki`, and `fanfare.wiki`, HTTP 404 on a missing slug, and versioned `/css/core/typography.css?v=SAGvU1CJo-bBKQjx5yovlx4EDMKSmaSPlmUGG_XG3jc` serving `.title-logo__img` and `.title-logo--themed`.
- Verified container running as UID `1654` with `restarts=0` and zero `fail:`, `crit:`, or unhandled exception log entries, then promoted GHCR `latest` to `sha256:0f4bf8a101890d463a71e9d93249558dd9c450ccf0762ea4520284ecb41cd7a4`.

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T181210Z-dc06539d996e'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:f1add1903409f6dce849c9626acecb0ae1cab342fe1aa6559365409b6ed00c7d` (`rollback-before-release-20261126T181210Z-dc06539d996e`) before the next routine deploy.
