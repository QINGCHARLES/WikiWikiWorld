# Hatnote release — failed and rolled back, 2026-11-24

The owner asked to commit, push and deploy `{{Hatnote}}` (`5458ba2`). The release did not start in production and was
rolled back within a minute. **Production is unchanged: it still runs the citation display hotfix.**

## What happened

- Before deploying, `origin/main` matched local `main`, and production ran `release-20261123T210758Z-480b02e9a469`
  (`sha256:ead2a6a3…`). Since `480b02e` is an ancestor of `main`, `main` looked safe to deploy.
- It was not: `main` also holds `5239c17` (roles and trusted link domains), which had been merged into local `main`
  alongside the hotfix (`61799a9`) but never deployed. Its own commit message and
  [roles-and-trusted-domains.md](roles-and-trusted-domains.md) say `AddRolesAndTrustedLinkDomains.sql` must be applied
  to production **before** that code starts. It has been applied to development only.
- `release-20261124T050232Z-5458ba252aae` (`sha256:f499799a…`) was started at `05:03:14Z` and crashed at startup with
  `SQLite Error 1: 'no such table: TrustedLinkDomains'` (exit 139, four restarts).
- At about `05:03:32Z` the previous image was put back with `/home/deploy/update.sh` and the hotfix digest. It runs
  with zero restarts and no error lines; the en, ja and ar Magazedia home pages, an article and Cinepedia return 200.
  The database was not touched by either image.

## Follow-up already done

- The publish had moved GHCR `latest` to the failed release, so a plain `update.sh` (or the editor's "Deploy to
  Hetzner" task) would have taken the site down again. `latest` was pushed back to the running digest
  (`sha256:ead2a6a3…`) from the host.
- The failed image stays on GHCR and on the host as `release-20261124T050232Z-5458ba252aae`; do not run it until the
  SQL has been applied.

## Resolution

Shipped later the same morning, after the SQL was applied: see
[release-roles-trusted-domains-2026-11-24.md](release-roles-trusted-domains-2026-11-24.md).

## Lesson

"Production's commit is an ancestor of `main`" does not make `main` deployable. Before each deploy, list
`git log <deployed>..main` and check each commit's notes for schema scripts or other ordering requirements.

## To ship the hatnote

Either apply `AddRolesAndTrustedLinkDomains.sql` to production (backup, check script, apply, verify script, as
[roles-and-trusted-domains.md](roles-and-trusted-domains.md) describes) and then deploy `main`; or deploy the hatnote
alone on top of the running hotfix. Both wait for the owner's decision.
