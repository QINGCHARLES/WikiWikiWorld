# Roles, trusted link domains and Hatnote — release 2026-11-24

The owner asked to push and deploy. This ships the roles and trusted-link-domain work (`5239c17`), together with
`{{Hatnote}}` (`5458ba2`), which had been [rolled back](release-hatnote-2026-11-24.md) because
`AddRolesAndTrustedLinkDomains.sql` had not yet been applied to production.

## Order

1. **Pre-flight.** `git log 480b02e..main` (480b02e was the running hotfix) showed one schema script, the roles one,
   and nothing else with an ordering requirement. `main` already matched `origin/main` (`4c0ef4f`), so nothing was
   pushed. The host's `db-update.sh` and `db_update.py` matched the repository copies (SHA-256).
2. **Schema.** The three scripts were staged at
   `/home/deploy/db-maintenance/roles-trusted-domains-20261124T051733Z` (SHA-256 matching the repository) and applied
   with `db-update.sh production` at 05:17:52Z. The pre-check, verification, integrity and foreign-key checks
   passed. Backup: `/opt/wiki/data/.db-backups/app.db-20261124T051752Z-_coz9wme.db`. The running hotfix ignored the
   new table and role rows.
3. **Deploy.** The image already built for the failed attempt was reused, because the only later commit (`4c0ef4f`)
   is documentation:
   - Tag: `release-20261124T050232Z-5458ba252aae`
   - Digest: `sha256:f499799ad583d90dd3fe7391de4f2cacb602b32baf460abbad5549025171045f`

   It was run through `/home/deploy/update.sh` with that tag and started at `2026-11-24T05:18:02Z`. GHCR `latest`
   was pushed from the host to point at this digest again; the rollback had pointed it at the hotfix.
4. **Bootstrap.** `grant-role /app/data/app.db QINGCHARLES Administrator` was run in the release image, with
   `--user 1654:1654 --network none` and the data directory mounted. No other account holds a role in production.

## Verification (production)

- The container is running with zero restarts, and the logs have no `fail:`, `crit:` or exception lines.
- These pages return 200 to an anonymous visitor: the en, ja and ar Magazedia home pages, `032c`, Cinepedia and
  `sitemap.xml`.
- `/wiki:trusted-domains`, `/Admin/TrustedDomains` and `/Admin/DomainRedirects` redirect anonymous visitors to the
  sign-in page.
- The seeded trust list is live: on `gq-hype`, `www.gq.com` links render with `rel="noopener"`. On `032c`, untrusted
  links such as `magazine.032c.com` render with `rel="nofollow noopener"`.
- **Not verified in production:** signed-in behaviour (the admin badge, proposing, reviewing, and refused deletes).
  This needs the owner's sign-in. It was all exercised in development; see
  [roles-and-trusted-domains.md](roles-and-trusted-domains.md#verification).

## Rollback

Pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261124T050232Z-5458ba252aae` (the citation
hotfix, `sha256:ead2a6a3…`) to `/home/deploy/update.sh`. The database can stay as it is: the old image ignores the
new table and roles, and reads the trusted list from its own appsettings. If you roll back, point `latest` at the
image that is running, or a plain `update.sh` will redeploy this release.

Not in this release: the "Delete article" menu wiring, which is uncommitted on the branch `claude/sharp-bun-6a10a0`.
