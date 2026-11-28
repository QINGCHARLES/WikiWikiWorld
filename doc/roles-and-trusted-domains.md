# Roles and trusted link domains

Signed-in accounts without a role are ordinary members. Roles apply across every site and culture. Code checks a
named **permission**, never a role name, and `AppRoles.Grants` in
[`Infrastructure/Permissions.cs`](../src/WikiWikiWorld.Web/Infrastructure/Permissions.cs) decides which roles hold each
permission. Moving a permission to another role is a one-line change there.

| Role | Holds | Notes |
|---|---|---|
| Member (no role) | Edit, upload, talk, message, bookmark, **propose** trusted domains, delete their own `@user` page | |
| Senior Editor | `DeleteArticles` | |
| Moderator | nothing yet | Reserved for conduct tools (hiding talk posts and messages, blocking accounts) |
| Administrator | `UseAdminTools`, `DeleteArticles`, `ReviewTrustedLinkDomains` | |
| Bot | nothing extra | Marks automation and API accounts |

| Permission | Gates |
|---|---|
| `UseAdminTools` | `/Admin`, `/Admin/Users`, `/Admin/DomainRedirects`, `/Admin/MigrateFiles` (MigrateFiles also keeps its single-operator check) |
| `DeleteArticles` | The `/{slug}/delete` confirmation page and `DELETE /api/article/{slug}`, except an account's own `@user` page. The article menu and the editor show their delete controls only to accounts that may delete that article (`PermissionService.MayDeleteArticleAsync`). |
| `ReviewTrustedLinkDomains` | `/Admin/TrustedDomains` (confirm, deny, withdraw), and the pending-count badge on the account menu |

Permissions are checked against the database on every request (`PermissionService`, cached once per request). A role
change applies to the account's next request, for cookie and API-token sign-ins alike. Tokens carry no role claims. A
refused page answers 403, which the site's status-code page shows as its not-found page.

## Giving out roles

There is no role-management page yet. Use the command, which is built into the web app:

```powershell
dotnet src/WikiWikiWorld.Web/bin/Release/net11.0/WikiWikiWorld.Web.dll grant-role data/app.db QINGCHARLES Administrator
```

```powershell
dotnet src/WikiWikiWorld.Web/bin/Release/net11.0/WikiWikiWorld.Web.dll revoke-role data/app.db SomeUser "Senior Editor"
```

The username and role name are matched case-insensitively. The command exits with 0 on success (including "no change"),
1 if the account or role is missing, and 2 for a usage error. It runs before the web host is built, so it needs no
secrets or ports.

On the production host, run it in the deployed release image as the app user, without networking, with the data
directory mounted. Use the image you deployed, or a pinned rollback ID instead of `:latest`:

```bash
docker run --rm --user 1654:1654 --network none \
  -v /opt/wiki/data:/app/data \
  ghcr.io/qingcharles/wikiwikiworld:latest \
  grant-role /app/data/app.db QINGCHARLES Administrator
```

This relies on the SDK container's default entrypoint, which passes the extra arguments to the app. It was run on
2026-11-24 to make QINGCHARLES an Administrator. Each later production role change is the owner's decision and needs
the same approval as any other production change.

## Account administration

Administrators can open **Admin** in the account menu on any site and select **Users** from the overview
or sidebar. The directory starts with the
newest registrations and can filter by username fragment, access status, and role. Administrators can
disable other accounts and enable them again. Disabling preserves the account, roles, and contributions.
It blocks new sign-ins and invalidates existing browser sessions and API tokens on the next request.
Re-enabled users sign in or request a new token. Temporary password lockouts retain their own schedule.

Before starting this build on an existing database, run
[`AddUserDisabledStatus.sql`](../src/WikiWikiWorld.Data/SqliteScripts/AddUserDisabledStatus.sql) through the
[database maintenance runner](database-maintenance.md) with
[`VerifyUserDisabledStatus.sql`](../src/WikiWikiWorld.Data/SqliteScripts/VerifyUserDisabledStatus.sql) and
[`CheckUserDisabledStatusBefore.sql`](../src/WikiWikiWorld.Data/SqliteScripts/CheckUserDisabledStatusBefore.sql).
The additive column defaults existing and new accounts to enabled. Fresh databases get it from `Create.sql`.
Older builds tolerate the added column, but a rollback to one of those builds stops enforcing disabled
status. Account for any disabled users before resuming traffic on an older build.

Applied to development `data/app.db` on 2026-11-27 at 20:46 UTC. The runner verified the schema, integrity,
and foreign keys. Backup: `data/.db-backups/app.db-20261127T204628Z-mvt5314f.db`.

Production release `release-20261127T204843Z-2152ce2c5fba` started on 2026-11-27 at 2:50 p.m. CST
(20:50:44 UTC). The schema update completed first and passed integrity, foreign-key, and schema
checks. Backup: `/opt/wiki/data/.db-backups/app.db-20261127T205033Z-4arpeg3b.db`.
The deployed image and GHCR `latest` both resolve to
`sha256:8e388b143edad864bcdffc3e76c21a6bc600b870249980d6e7b25548d334acc1`.
Public HTTPS probes passed, including the sign-in redirect for anonymous requests to `/Admin/Users`.
Signed-in suspension and re-enabling were verified against a disposable database in Chromium.

The previous image is pinned as
`ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261127T204843Z-2152ce2c5fba`
for `/home/deploy/update.sh`. If rolling back, restore GHCR `latest` to the running digest and account
for any suspended users before resuming traffic.

### Integrated admin workspace release

Production started serving commit `6638c7e4f93f` at 9:19 p.m. CST on 2026-11-27 (03:19:56 UTC on 2026-11-28).
Release `release-20261128T031814Z-6638c7e4f93f` and GHCR `latest` resolve to
`sha256:169d5145ee57aac2443b586ae1edc0ac59446cda79694655bd43ffc32e5d267e`. The release uses the existing database schema.

All 474 .NET tests, 13 article-picker tests, and localization checks passed from the release source.
Production served the expected stylesheet and public HTTPS pages, redirected anonymous admin requests to
sign-in, and ran with zero restarts. Admin roles, account actions, bulk domain confirmation, and the
responsive layout were exercised on a disposable development database before deployment.

For rollback, pass `ghcr.io/qingcharles/wikiwikiworld:rollback-before-release-20261128T031814Z-6638c7e4f93f`
(`sha256:1a9ea142fb2c8be2049e8a62c1b90a211ac1641e44d059e49b10c85c7c706278`) to `/home/deploy/update.sh` and point GHCR `latest` to the running digest after verification.

## Trusted link domains

External links get `rel="nofollow noopener"` unless their host is trusted, in which case they get `rel="noopener"`.
Trusting `example.com` also trusts every subdomain (`LinkDomainHelper.IsApprovedDomain`). Trust is global across sites.

**Workflow.** Any member proposes a domain with a required reason at `/wiki:trusted-domains`, or through `POST /api/trusted-domains` (see [the API](api.md#trusted-domains-api); the article prompts tell MaggieTheBot to propose the official sites it links). A pasted address is
reduced to its host: lowercase, punycode for IDN names, no `www.`. IP addresses, bare top-level domains, domains already
trusted (directly or through a parent) and domains already waiting are refused. Administrators see a count on their
avatar and on the **Admin** account-menu link. **Trusted domains** in the workspace opens the review queue.
Administrators can confirm selected proposals together without notes, or review a single proposal to confirm or
deny it with an optional note. They can withdraw trust later. Each proposal is its own row in `TrustedLinkDomains`, recording who proposed,
reviewed and withdrew it and when. A partial unique index allows one pending-or-trusted row per domain; denied and
withdrawn rows are history, and the domain can be proposed again.

**Live list.** `TrustedLinkDomainList` (singleton) holds the trusted domains in memory, and the cached Markdown
pipelines read it on every render. It is loaded at startup and reloaded after a confirm or withdraw, so a decision applies
to the next page rendered, without a restart or re-save. This assumes a single application instance.

**What to trust.** Official magazine and publisher sites, libraries, archives, museums and government sites. Never
trust hosts where anyone can publish (Medium, Substack, Blogspot, `github.io`, fandom wikis, social networks), because
trust covers every subdomain.

The retired `SiteConfiguration:ApprovedLinkDomains` setting was removed from all three appsettings files. Its seven
domains are seeded as trusted rows with no proposer.

## Schema change and deploy order

Existing databases need [`AddRolesAndTrustedLinkDomains.sql`](../src/WikiWikiWorld.Data/SqliteScripts/AddRolesAndTrustedLinkDomains.sql)
with [`CheckRolesAndTrustedLinkDomainsBefore.sql`](../src/WikiWikiWorld.Data/SqliteScripts/CheckRolesAndTrustedLinkDomainsBefore.sql)
and [`VerifyRolesAndTrustedLinkDomains.sql`](../src/WikiWikiWorld.Data/SqliteScripts/VerifyRolesAndTrustedLinkDomains.sql),
applied through the [maintenance runner](database-maintenance.md). The change is additive: it inserts the four role rows
(assigning no one) and creates and seeds `TrustedLinkDomains`. The running older release ignores both.

1. Apply the SQL **before** deploying this release. The release loads the trusted list at startup and will not start
   without the table.
2. Deploy the release.
3. Grant Administrator to the operator account with `grant-role`. Until then nobody can open `/Admin/*`, and only
   owners can delete their own user pages.

`Create.sql` contains the same objects, so fresh databases match.

**Rollback.** The previous image reads `ApprovedLinkDomains` from its own baked-in appsettings and ignores the new table
and roles, so redeploying it works without touching the database.

### Development record

Applied to development `data/app.db` on 2026-11-23 at 20:27 UTC with `python -B deploy/db_update.py development ...`.
Backup: `data/.db-backups/app.db-20261123T202713Z-plei0fze.db`. The pre-check, verification, integrity and foreign-key
checks passed. Before that, the script was tested on a disposable copy of the dev database, which also confirmed that
the open-domain index refuses a duplicate and the status check refuses unknown values, and on a fresh `Create.sql`
schema (table definitions match). DevvyTheBot and QINGCHARLES are Administrators in development.

Production: applied and deployed on 2026-11-24; see
[the release record](release-roles-trusted-domains-2026-11-24.md).

## Verification

- `dotnet test tests/WikiWikiWorld.Web.Tests -c Release` (existing suite; no new tests were added).
- `python scripts/localization.py check` (the new strings have Arabic and Japanese drafts, which need native review
  like the rest).
- Browser check (development, 2026-11-23):
  - As a member: admin pages refused; a bare TLD, an IP address, a missing reason, a duplicate and a subdomain of a
    trusted domain were all refused; `https://www.LOC.gov/collections/` was stored as `loc.gov`.
  - After `grant-role`, with no new sign-in: admin pages opened and the badge showed the pending count.
  - Confirming `esquire.com` changed existing links on `playgirl-1973` from `nofollow noopener` to `noopener` with no
    restart.
  - Deny and withdraw recorded their notes, and the proposer saw each outcome. Repeating a confirm on an
    already-reviewed row was refused.
  - A member's delete was refused. A Senior Editor saw the Delete link and could delete (the test article was then
    restored), but still could not open admin pages.
