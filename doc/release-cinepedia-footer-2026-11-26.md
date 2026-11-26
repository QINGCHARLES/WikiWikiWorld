# Cinepedia footer release and rollback

Stage 1 of the [site customization plan](site-customization-plan.md#implementation-stages) went live on November 26, 2026 at 06:32:30 UTC. The shared Razor layout selects the Cinepedia footer for site 4. This release changes application files only. Rollback replaces the container using the same data, certificate, and secret mounts. It requires no database restore and preserves edits made after deployment.

| Image | Tag in `ghcr.io/qingcharles/wikiwikiworld` | Registry digest |
| --- | --- | --- |
| Footer release from commit `96fd95c045cd` | `release-20261126T063014Z-96fd95c045cd` | `sha256:f1add1903409f6dce849c9626acecb0ae1cab342fe1aa6559365409b6ed00c7d` |
| Previous production image | `rollback-before-release-20261126T063014Z-96fd95c045cd` | `sha256:ce6671cdf21b631397b3744433da4cc0e66dd3a42cdc194dc238e42fe2af386d` |

## Rollback procedure

Use this rollback for a regression in the footer release, such as either site's homepage or search page returning errors, the Cinepedia footer appearing on another site, or repeated container startup failures. If a newer release has since gone live, consult its release record first.

1. Restore the previous image on the production host. The rollback tag was pinned locally and in GHCR before cutover. If the local copy has since been pruned, pull the recorded rollback tag before invoking the script.

   ```sh
   ssh deploy@magazedia.wiki '/home/deploy/update.sh ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261126T063014Z-96fd95c045cd'
   ```

2. Check both English homepages and both `/Search` pages over public HTTPS. Expect HTTP 200 and the original footer with `class="footer-columns"` on all four. Confirm the container is running with zero restarts.

   ```sh
   ssh deploy@magazedia.wiki 'docker inspect -f "{{.Image}} {{.State.Status}} {{.RestartCount}}" wikiwikiworld'
   ```

3. Point GHCR `latest` back to the same digest so the next routine deploy also serves the restored image. From a machine with Docker Buildx and GHCR package write access, run the following and verify the displayed digest matches the previous image in the table.

   ```sh
   docker buildx imagetools create --prefer-index=false --tag ghcr.io/qingcharles/wikiwikiworld:latest ghcr.io/qingcharles/wikiwikiworld@sha256:ce6671cdf21b631397b3744433da4cc0e66dd3a42cdc194dc238e42fe2af386d
   docker buildx imagetools inspect ghcr.io/qingcharles/wikiwikiworld:latest
   ```

## Deployment checks

- Matched the original running image to the prior release digest and verified the host's update script matched the repository copy.
- All 436 .NET tests and the localization check passed before release. The published Linux x64 image contained the Cinepedia CSS and production settings. Development settings were excluded.
- Compared eight public HTTPS pages before and after the switch. Cinepedia's English homepage and search page served the custom footer and its versioned stylesheet. English Magazedia's homepage, search and Cinefex article, Japanese Magazedia's homepage, and Fanfare's homepage retained their original footers. Cinepedia's root served the standalone culture selector. Both English search pages produced identical main markup.
- The new container ran with zero restarts and no failure or critical log entries during verification. GHCR `latest` was promoted after the public checks passed. The previous image was verified on disk and in GHCR. The live rollback procedure was reserved for a failed deployment.
