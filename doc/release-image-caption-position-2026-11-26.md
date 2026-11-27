# Image directive CaptionPosition=top|bottom — release 2026-11-26

Deployed at `2026-11-27T00:54:57Z` from commit `2c166bca5a4f` (`2c166bca5a4f6fc667a70c6f6b7a6a24747af3d6` on `main`), adding `CaptionPosition=bottom|top` to `{{Image ...}}` so captions can render above the image (with flush header-band styling on framed figures) while credits remain anchored at the bottom.

This release changes application code, static CSS, documentation, and tests only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| CaptionPosition release from `2c166bca5a4f` | `release-20261127T005408Z-2c166bca5a4f` | `sha256:794e906dfc2ce50aff1efbe4231596e7cd12b84a2aaeed9295e35f9deac0bdb7` |
| Previous production image (`66299acb8a0f`) | `rollback-before-release-20261127T005408Z-2c166bca5a4f` (pinned on host) | `sha256:147ce77461cda254dbebb7da7a60dd654c9b6d9aa37d5ab7c258ccbe6ee33f3e` |

## Verification

- All 455 .NET tests (`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --no-restore`) and `python scripts/localization.py check` passed before release.
- Pinned the previous running image (`sha256:147ce77461cda254dbebb7da7a60dd654c9b6d9aa37d5ab7c258ccbe6ee33f3e`) on the production host as `rollback-before-release-20261127T005408Z-2c166bca5a4f` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com/playboy`, `en.magazedia.com/`, `magazedia.com/`, `en.cinepedia.wiki/`, and `fanfare.wiki/`, HTTP 404 on a missing slug, and versioned `/css/components/media.css` serving `.article-image__caption--top` and `.article-image__footer`.
- Verified container running with `restarts=0`, then promoted GHCR `latest`.

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T005408Z-2c166bca5a4f'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:147ce77461cda254dbebb7da7a60dd654c9b6d9aa37d5ab7c258ccbe6ee33f3e` before the next routine deploy.
