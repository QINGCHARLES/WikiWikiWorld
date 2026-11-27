# Home-page header fallback and two-column cards — release 2026-11-27

Deployed at `2026-11-27T07:08:05Z` from commit `8f53e4eeeb2d` (`8f53e4eeeb2d9b83dbc0da8a3b13bc7665ecc984` on `main`), updating the home page to include standard articles that have no resolved `{{CoverCarousel}}` cover but have a resolved `{{HeaderImage}}` (preserving `Focus=top|center|bottom`), and styling header-backed entries as two-column landscape tiles (`cover-item--header`) aligned to the row height of adjacent `0.7:1` portrait covers.

This release changes application code, Razor markup, static CSS, documentation, and tests only. No database schema change is required, and rollback preserves all edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Home header fallback release from `8f53e4eeeb2d` | `release-20261127T070551Z-8f53e4eeeb2d` | `sha256:ac28731b6c36720d6174f519eaaaabd17da8e8cd1f034554d75f6506216e74c7` |
| Previous production image (`40f576741621`) | `rollback-before-release-20261127T070551Z-8f53e4eeeb2d` (pinned on host) | `sha256:c6b6f35723ed9d1ea61bc1a17f9c753aba16d1aa96a9234bab202a78c6f74528` |

## Verification

- All 474 .NET tests (`dotnet test -c Release`), `python scripts/localization.py check`, and Chromium layout checks across desktop (`1440×900`), two-column (`700×900`), and mobile (`375×812`) viewports passed before release.
- Pinned the previous running image (`sha256:c6b6f35723ed9d1ea61bc1a17f9c753aba16d1aa96a9234bab202a78c6f74528`) on the production host as `rollback-before-release-20261127T070551Z-8f53e4eeeb2d` and pulled the immutable release tag before stopping the previous container.
- Verified HTTP 200 over public HTTPS for `en.magazedia.com/`, `magazedia.com/`, `ja.magazedia.com/`, `ar.magazedia.com/`, `en.cinepedia.wiki/`, `fanfare.wiki/`, `crtr.world/`, and `en.magazedia.com/playboy`, HTTP 404 on a missing slug, live `class="home-cover-grid"` and `cover-item--header` entries (including `/playboy`, `/vogue`, `/gq`, `/game-informer`, and `/george`) on `en.magazedia.com/`, and versioned `/css/pages/home.css?v=fNYl5c11O2ZsXaRhdiWlJ1h8fyEKYo9EqACrq4LdzFY`.
- Verified container running as UID `1654` with `Restarts=0` and clean logs (zero `fail:`, `crit:`, or exception lines), then promoted GHCR `latest` (`sha256:ac28731b6c36720d6174f519eaaaabd17da8e8cd1f034554d75f6506216e74c7`).

## Rollback procedure

1. Restore the previous image on the production host:

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T070551Z-8f53e4eeeb2d'
   ```

2. Confirm the container is running with zero restarts:

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect --format "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to `sha256:c6b6f35723ed9d1ea61bc1a17f9c753aba16d1aa96a9234bab202a78c6f74528` (`release-20261127T033139Z-40f576741621`) before the next routine deploy.
