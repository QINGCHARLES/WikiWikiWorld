# Image directive layout, frame, caption, and credit options — release 2026-11-26

Deployed at `2026-11-27T00:02:29Z` from commit `66299acb8a0f` (`66299acb8a0fbc51b8b35f3ad74cf119182742bf` on `main`), adding standalone block `<figure class="article-image">` rendering, `Layout`, `Size`, `Frame`, `Caption`, `Credit`, and `Link` options, safe inline Markdown in captions/credits, save warnings in `ArticleSaveWarnings`, and the `.article-image` specificity fix in `src/WikiWikiWorld.Web/wwwroot/css/components/media.css`.

This release changes application code and static CSS only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Image directive release from `66299acb8a0f` | `release-20261127T000139Z-66299acb8a0f` | `sha256:147ce77461cda254dbebb7da7a60dd654c9b6d9aa37d5ab7c258ccbe6ee33f3e` |
| Previous production image | `rollback-before-release-20261127T000139Z-66299acb8a0f` (pinned on host) | `sha256:361c873ba3e38a474547835c9045f607a5021680139562593ed519f060c73da4` |

## Verification

- All 454 .NET tests (`dotnet test tests/WikiWikiWorld.Web.Tests -c Release --no-restore`) and `python scripts/localization.py check` passed before release.
- Pinned the previous running image (`sha256:361c873ba3e38a474547835c9045f607a5021680139562593ed519f060c73da4`) on the production host as `rollback-before-release-20261127T000139Z-66299acb8a0f` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com/playboy`, `en.magazedia.com/`, `magazedia.com/`, `en.cinepedia.wiki/`, and `fanfare.wiki/`, HTTP 404 on a missing slug, and versioned `/css/components/media.css?v=wy5iA_AEHdbDPJUBt1eXOa8JiDMHoH2tjlkjW7t2CFM` serving `.article-image` without the `figure.article-image` selector clash.
- Verified container running with `restarts=0`, then promoted GHCR `latest`.

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T000139Z-66299acb8a0f'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:361c873ba3e38a474547835c9045f607a5021680139562593ed519f060c73da4` before the next routine deploy.
