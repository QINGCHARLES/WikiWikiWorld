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
| `UseAdminTools` | `/Admin/DomainRedirects`, `/Admin/MigrateFiles` (MigrateFiles also keeps its single-operator check) |
| `DeleteArticles` | `Article/Delete` and `DELETE /api/article/{slug}`, except an account's own `@user` page. The article menu shows "Delete article" only to holders. |
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

## Trusted link domains

External links get `rel="nofollow noopener"` unless their host is trusted, in which case they get `rel="noopener"`.
Trusting `example.com` also trusts every subdomain (`LinkDomainHelper.IsApprovedDomain`). Trust is global across sites.

**Workflow.** Any member proposes a domain with a required reason at `/wiki:trusted-domains`. A pasted address is
reduced to its host: lowercase, punycode for IDN names, no `www.`. IP addresses, bare top-level domains, domains already
trusted (directly or through a parent) and domains already waiting are refused. Administrators see a count on their
avatar and an account-menu link to `/Admin/TrustedDomains`, where they confirm or deny, with an optional note shown to
the proposer, or withdraw trust later. Each proposal is its own row in `TrustedLinkDomains`, recording who proposed,
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
