# WikiWikiWorld / Magazedia — Server Rebuild & Disaster Recovery

Companion to [`container-deployment-pattern.md`](container-deployment-pattern.md), which
holds the reusable pattern, the rationale, and the generic scripts. **Read that first.**
This document records only what is specific to this app.

- **Host:** Hetzner, Ubuntu, Docker. 3.7 GiB RAM, 38 GB disk (44% used).
- **Reached as:** `ssh magazedia` (alias for `deploy@magazedia.wiki`)
- **Source of truth for deploys:** `.vscode/tasks.json` in this repo
- **Last verified against the live server:** 2026-08-29
- **Last restore test:** _never — see [§9](#9-open-items)_

---

## 0. Change log

**2026-08-29** — audit and remediation pass.

| Change | Where | State |
| --- | --- | --- |
| Backup covers whole `sitefiles` tree, not two subdirectories | `backup_wiki.sh` | **live** |
| Backup includes certs + certbot state | `backup_wiki.sh` | **live**; job moved to the root crontab, verified |
| Backup includes its own script, `rclone.conf`, crontab | `backup_wiki.sh` | **live** |
| Container logs capped at 3 × 10 MB | `update.sh` | **live**, applies at next deploy |
| Renewal hook made atomic | `01-update-app.sh` | **live**, installed and tested 2026-08-29 |
| Image switched to `noble-chiseled-extra` (ICU) | `.vscode/tasks.json` | **committed**, applies at next build |
| Culture selector shows endonyms | `CultureSelect.cshtml{,.cs}` | **committed**, applies at next deploy |
| EF SQL logging lowered to `Warning` | `appsettings.json` | **committed**, applies at next deploy |
| Three vulnerable packages upgraded; build unblocked | `*.csproj` | **committed** |

Also on 2026-08-29: the ~900 MB of orphaned `wiki_full_backup_*` archives were deleted
from local disk and Drive, leaving 30 local and 60 cloud `wiki_backup_*` archives.

Previous versions of the server scripts are kept on the box as
`/home/deploy/{update,backup_wiki}.sh.bak-2026-08-29`.

Both root-only actions are complete — see [§7](#7-actions-still-requiring-root).

---

## 1. Parameters

| Placeholder | Value |
| --- | --- |
| `<APP>` | `wikiwikiworld` |
| `<IMAGE>` | `ghcr.io/qingcharles/wikiwikiworld:latest` |
| `<STATE_ROOT>` | `/opt/wiki` |
| `<PRIMARY_DOMAIN>` | `magazedia.com` (certbot lineage is named `en.magazedia.wiki`) |
| `<DEPLOY_USER>` | `deploy` |
| `<CLOUD_DEST>` | `gdrive:ServerBackups/Magazedia` |

### Verified container state

```
User          1654
Restart       unless-stopped
Ports         0.0.0.0:80->80, [::]:80->80, 0.0.0.0:443->443, [::]:443->443
Mounts        /opt/wiki/certs -> /app/certs   (rw)
              /opt/wiki/data  -> /app/data    (rw)
```

Two mounts only. The image also exposes `8080/tcp`, which is unused — the .NET 8+
default port, harmless because nothing publishes it.

### Database maintenance command

Use the repository's `deploy/db-update.sh` for reviewed database updates as UID
1654. See [database maintenance](database-maintenance.md) for staging, checks,
backup locations and failure handling. This replaces improvised write containers;
it does not change the scheduled backup or application deployment procedure.

### Verified filesystem

| Path | Contents | Owner |
| --- | --- | --- |
| `/opt/wiki/data/` | `app.db` + `-shm`/`-wal`, `sitefiles/`, `.well-known/`, `dbu.sql` | `1654:1654` |
| `/opt/wiki/certs/` | `cert.pem` (644), `key.pem` (**600**, ECDSA) | `1654:1654` |
| `/home/deploy/` | `update.sh`, `backup_wiki.sh`, `backups/`, rclone config | `deploy:deploy` |

`key.pem` at mode `600` owned by UID 1654 is **not readable by `deploy`**. That single
fact drives the backup design in [§5](#5-backup).

`dbu.sql` is root-owned and dated May 29 — a manual schema-update script left in the
data directory. It is inside `/opt/wiki/data` but outside `sitefiles`, so it is not in
the backup; it is a historical artefact, not live state.

---

## 2. Certificates

`certbot` uses the **webroot** authenticator pointed at the data volume:

```
authenticator = webroot
webroot_path  = /opt/wiki/data
key_type      = ecdsa
```

certbot writes challenges to `/opt/wiki/data/.well-known/acme-challenge/`; the container
sees the same directory at `/app/data/.well-known`, which is the path `Program.cs:349`
serves via `PhysicalFileProvider`, registered before `UseHttpsRedirection()` so port-80
challenges are not 308-redirected.

> Historical note: an earlier `update.sh` mounted `/var/www/certbot` to
> `/app/wwwroot/.well-known/acme-challenge`, a path the app never reads. That mount has
> been removed and the webroot corrected. Do not reintroduce it.

**Live certificate** (`CN=magazedia.com`, valid 2026-08-06 → 2026-11-04), 18 SANs:

```
magazedia.com          www.magazedia.com      en.magazedia.com
ja.magazedia.com       ar.magazedia.com       xx-test.magazedia.com
magazedia.wiki         www.magazedia.wiki     en.magazedia.wiki
ja.magazedia.wiki      ar.magazedia.wiki      xx-test.magazedia.wiki
fanfare.wiki           www.fanfare.wiki       en.fanfare.wiki
crtr.world             www.crtr.world         en.crtr.world
```

Every hostname in `SiteConfiguration.Sites` is covered, plus the `.wiki` redirect
sources and `www.` variants. That coverage matters because TLS completes **before**
`CanonicalDomainMiddleware` can redirect — a hostname missing from this list gets a
certificate warning rather than a redirect.

> **Adding a site or culture domain is a two-part change.** Editing
> `SiteConfiguration.Sites` is not enough — reissue with the extra `-d` and restart, or
> the new hostname fails at the handshake.

Renewal is scheduled by `certbot.timer` (active).

### The deploy hook

`/etc/letsencrypt/renewal-hooks/deploy/01-update-app.sh` copies the renewed lineage into
`/opt/wiki/certs` and restarts the container. The restart is load-bearing: Kestrel reads
the PEM only at startup, so a renewal without one changes nothing the running app can
see. Using certbot's `$RENEWED_LINEAGE` rather than a hardcoded path is correct here,
because the lineage is named `en.magazedia.wiki` while the certificate's CN is
`magazedia.com`.

The original version worked but was not atomic — `cat src > dst` truncates the
destination before writing, and with no `set -e` a failed copy still reached
`docker restart`, bringing the container back with an empty certificate. The failure
mode was "site down" where it should be "site stays on the previous, still-valid
certificate".

Replaced on 2026-08-29 with the version below, now installed root:root mode 755:

```bash
#!/bin/bash
set -euo pipefail
CERT_DIR="/opt/wiki/certs"

echo "Deploying new certs from $RENEWED_LINEAGE to app..."

install -o 1654 -g 1654 -m 644 "$RENEWED_LINEAGE/fullchain.pem" "$CERT_DIR/cert.pem.new"
install -o 1654 -g 1654 -m 600 "$RENEWED_LINEAGE/privkey.pem"   "$CERT_DIR/key.pem.new"

mv -f "$CERT_DIR/cert.pem.new" "$CERT_DIR/cert.pem"
mv -f "$CERT_DIR/key.pem.new"  "$CERT_DIR/key.pem"

echo "Restarting container..."
docker restart wikiwikiworld

echo "Certificate deploy complete."
```

`mv` within a filesystem is atomic, so the container can never observe a half-written
certificate.

### Testing renewal without spending a rate limit

Let's Encrypt caps duplicate certificates at 5 per week, so **do not** reach for
`certbot renew --force-renewal` casually — it issues a real certificate every time.

Test the two halves separately instead, neither of which contacts the production CA:

```bash
sudo certbot renew --dry-run
```

That exercises the webroot challenge against the staging server. It does **not** run
deploy hooks, which is what you want here — with `--run-deploy-hooks` the hook would
receive a `$RENEWED_LINEAGE` pointing at a *staging* certificate, copy that untrusted
certificate into `/opt/wiki/certs`, and restart the container onto it. That would take
the site down.

Then exercise the hook on its own, pointed at the real lineage:

```bash
sudo RENEWED_LINEAGE=/etc/letsencrypt/live/en.magazedia.wiki /etc/letsencrypt/renewal-hooks/deploy/01-update-app.sh
```

This re-copies the certificate already in use — byte-identical, so the only visible
effect is the container restart — and proves the copy, ownership, modes, and restart all
work. No CA call, no rate limit consumed.

Both were run on 2026-08-29. `certbot renew --dry-run` simulated renewal of all 18
domains successfully, confirming the webroot. The hook ran clean, leaving `cert.pem`
`644` and `key.pem` `600`, both `1654:1654`, with no leftover `.new` files — so the
`mv` swap completed atomically. The container restarted and `magazedia.com`,
`en.magazedia.com` and `fanfare.wiki` all returned `200` on the same certificate.

---

## 3. Globalization and the culture selector

The **currently deployed** image is built `noble-chiseled`, which ships without ICU and
therefore sets:

```
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
```

Every culture lookup falls back to the invariant culture, so the selector on
`magazedia.com` renders bare codes — `ar en ja xx-test` — because
`GetCultureDisplayName` catches `CultureNotFoundException` and returns the raw code.

**Two changes, both committed, live at the next build + deploy:**

1. `.vscode/tasks.json` now builds `-p:ContainerFamily=noble-chiseled-extra`, which
   carries ICU and tzdata. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is then unset.
2. `Pages/CultureSelect.cshtml.cs` uses `CultureInfo.NativeName` rather than
   `EnglishName`, so each language names itself.

Verified locally against .NET 10 with ICU:

| Code | Rendered |
| --- | --- |
| `en` | English |
| `ja` | 日本語 |
| `ar` | العربية |
| `xx-test` | `xx-test` |

`xx-test` is well-formed but not a real language: with ICU present it does **not** throw,
resolving instead to a synthesised culture flagged `CultureTypes.UserCustomCulture` whose
name reads `xx (Test)`. The page model checks that flag and falls back to the raw code,
which reads better for a test culture.

`Pages/CultureSelect.cshtml` now emits `hreflang`, `lang`, and `dir="auto"` per link, so
Arabic renders right-to-left beside the left-to-right entries without hardcoding
direction anywhere.

The rest of the codebase was unaffected either way: 16 uses of
`CultureInfo.InvariantCulture` (correct under both modes), with culture otherwise used as
a routing/filter string (`ef_filter___currentCulture`), never as a formatting culture.

---

## 4. Deploy

From VS Code, **⚡ FULL RELEASE** chains publish-to-GHCR and the SSH deploy. Manually:

```bash
dotnet publish src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj /t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository=qingcharles/wikiwikiworld -p:ContainerFamily=noble-chiseled-extra -c Release
```

```bash
ssh magazedia '/home/deploy/update.sh'
```

Package advisories that had been blocking the build were cleared on 2026-08-29 — see
[§4a](#4a-package-advisories-cleared-2026-08-29). `dotnet build WikiWikiWorld.slnx`
succeeds with 0 warnings and 0 errors, audit enabled.

### `/home/deploy/update.sh`

The script is versioned as [`deploy/update.sh`](../deploy/update.sh); copy it to the host
after changing it. It stops with the old container still running if the secrets file is
missing or incomplete, runs under `set -euo pipefail`, passes the secrets with
`--env-file`, and accepts an optional image reference (for example a previous image ID)
to run instead of pulling `:latest`, which also serves as rollback.

Before this the `json-file` driver had no cap, so the log grew unbounded for the life of
the container — with the app logging every EF Core SQL statement at `Information`.
`appsettings.json` now sets `Microsoft.EntityFrameworkCore.Database.Command` to
`Warning`, which removes the bulk of the volume at source.

**Log limits apply only to a newly created container**, so they take effect at the next
`update.sh` run, not immediately.

Still worth doing, not yet done:

- **Publish SHA tags** (`-p:ContainerImageTags='"latest;<sha>"'`) so rollback can name
  a release rather than a local image ID.
- **`docker image prune -f`** so superseded images do not accumulate.

### 4a. Package advisories, cleared 2026-08-29

`TreatWarningsAsErrors` promotes NuGet audit findings (`NU1902`/`NU1903`) to build
errors, so seven advisories were failing the build outright. All three packages had a
clean upgrade path:

| Package | Was | Now | Advisory |
| --- | --- | --- | --- |
| `System.Security.Cryptography.Xml` (direct pin, Data) | 10.0.6 | **10.0.11** | Five .NET DoS advisories (CVE-2026-50527, CVE-2026-50648 and related), `>= 10.0.0`, first patched 10.0.10 |
| `Microsoft.AspNetCore.OpenApi` (direct, Web) | 10.0.7 | **10.0.11** | Pulled `Microsoft.OpenApi` 2.0.0 — GHSA-v5pm-xwqc-g5wc, circular schema references terminate parsing |
| `AngleSharp` (direct, Web) | 1.3.0 | **1.7.2** | GHSA-pgww-w46g-26qg, mXSS via `annotation-xml` integration-point bypass, affects `< 1.5.0` |

Notes on each:

- **`System.Security.Cryptography.Xml` is a deliberate pin**, not an accident — it fixes
  the version flowing through EF Core design-time tooling's transitive `Microsoft.Build`
  dependency. The pin itself had gone stale and become the vulnerability. Keep it at the
  newest `10.0.x` patch rather than removing it.
- **`Microsoft.OpenApi` needed no direct pin.** `Microsoft.AspNetCore.OpenApi` 10.0.11
  declares `Microsoft.OpenApi [2.7.5, 3.0.0)`, and 2.7.5 is exactly the first patched
  2.x release, so bumping the parent resolves it. Verified: the graph now resolves
  `Microsoft.OpenApi 2.7.5`. Avoid jumping to the 3.x line — that is a major version the
  ASP.NET package does not accept.
- **AngleSharp moved four minor versions** but is used only by
  `HtmlPrettifyMiddleware` and `InlineAwarePrettyFormatter`, which re-serialise outgoing
  HTML for readability. It is not the sanitiser, so the blast radius is formatting.
  Verified by running the app and confirming responses still come back correctly
  indented and well-formed.

Confirm at any time with:

```bash
dotnet list package --vulnerable --include-transitive
```

### App-specific notes

- **`.vscode/tasks.json` was never actually tracked.** `.gitignore` excluded `.vscode/`
  and then tried to re-include `tasks.json`, but git cannot re-include a file whose
  parent directory is excluded — it never descends into the directory to see the
  negation. So the file holding the entire build-and-deploy pipeline existed in exactly
  one place: this workstation. Fixed on 2026-08-29 by excluding the contents rather than
  the directory (`.vscode/*`), which leaves `launch.json` and `settings.json` ignored as
  intended. **`tasks.json` is now visible to git but still uncommitted** — `git add` it.
- **Registry credentials.** `tasks.json` carries an empty
  `-p:ContainerRegistryPassword=`. Now that the file really can be committed, leave it
  empty and use `docker login ghcr.io` on the workstation, so a PAT never lands in a
  tracked file.
- **Schema is not migrated automatically.** Nothing calls `Migrate()` or
  `EnsureCreated()`. Schema comes from `src/WikiWikiWorld.Data/SqliteScripts/*.sql`,
  applied by hand. On a rebuild you restore an existing `app.db` rather than creating one.
- **`Microsoft.Extensions.Hosting.Systemd`** in `WikiWikiWorld.Web.csproj` is a leftover
  from a pre-container deployment and does nothing under Docker. Harmless; removable.
- **`ContentDeliveryNetworkSettings.BaseUrl`** (`https://static.magazedia.com`) is
  configured but referenced by no code.
- **`JwtSettings.Secret` was committed** in `appsettings.Production.json` in this public
  repository, so anyone could sign API tokens for any user (confirmed against production
  on 2026-10-25, and rotated the same day; a token signed with the old value is now
  rejected). It now lives only in `/home/deploy/secrets/wikiwikiworld.env` (mode 600,
  directory 700, owned by `deploy`) as `JwtSettings__Secret=…`, passed with `--env-file`.
  The old value stays in git history and older images; never reuse it. On a rebuild,
  recreate the file with a fresh `openssl rand -base64 48` value; existing tokens simply
  stop working. See PROJECT.md "API tokens".
- **Stale leftovers**, safe to remove: empty `/home/deploy/certs` and `/home/deploy/data`
  (owned `1654:1654`), `letsencrypt_backup.tar.gz`, `wikiwikiworld-build/`.

---

## 5. Backup

`/home/deploy/backup_wiki.sh` at 04:00 daily via the **root** crontab (moved from
`deploy` on 2026-08-29 so certificates and certbot state are readable):

```
0 4 * * * /home/deploy/backup_wiki.sh >> /home/deploy/wiki_backup.log 2>&1
```

### What it captures now

| Item | Method | Notes |
| --- | --- | --- |
| `app.db` | `sqlite3 .backup` | Online backup, safe against the live WAL writer |
| `sitefiles/` | `cp -r`, whole tree | Covers future SiteIds automatically |
| `/opt/wiki/certs` | `cp -r`, conditional | Captured since the move to root |
| `/etc/letsencrypt` | `tar`, conditional | Captured since the move to root: accounts, `live/`, `archive/`, renewal config |
| `update.sh`, `backup_wiki.sh` | `cp` | So a rebuilt host has its own scripts |
| `rclone.conf`, `crontab -l` | `cp` / redirect | So a rebuilt host can reach the backups and restore its schedule |

Verified run on 2026-08-29 produced a 64 MB archive containing `data/app.db`,
`data/sitefiles`, `backup_wiki.sh`, `update.sh`, `rclone.conf`, `crontab.txt`.

Previously the file copy was a whitelist of `sitefiles/1/profilepics` and
`sitefiles/1/images`. That lost nothing at the time — only site 1 existed — but
`SiteConfiguration` already defines **SiteId 2 (Fanfare)** and **SiteId 3 (CRTR)**, whose
first upload would have landed outside the backup silently. The script now copies the
whole `sitefiles` tree.

### Why the job runs as root

`key.pem` is mode `600` owned by UID 1654 and `/etc/letsencrypt/archive` is root-only,
so **`deploy` could read neither**. Until 2026-08-29 the job ran as `deploy` and silently
omitted both — the material you most need to restore TLS without re-issuing.

The certbot state matters more than the deployed PEMs: without the account keys, `live/`,
`archive/` and renewal config, a rebuilt host can serve a restored certificate but has
**no renewal lineage**, so nothing renews and HTTPS lapses ~90 days later.

The script runs correctly as either user — it sets `RCLONE_CONFIG` explicitly so root
finds the deploy user's remote, chowns the finished archive back to `deploy`, and
degrades with a visible `NOTE:` rather than aborting if something is unreadable. Those
notes are the pass/fail signal: their absence means everything was captured.

**Verified 2026-08-29** on an archive produced by the root-run job:

| Content | Result |
| --- | --- |
| `data/app.db` | `PRAGMA integrity_check` → `ok`, 413 article revisions |
| `data/sitefiles/` | 138 entries — whole tree |
| `certs/key.pem` | 241 bytes, byte-identical to the live key |
| `letsencrypt.tar.gz` | `accounts/` (prod + staging keys), `live/`, `archive/en.magazedia.wiki/`, renewal config |
| Archive | `deploy:deploy`, mode `600`; cloud copy byte-identical |

> This grants no privilege that is not already held: `deploy` is in the `docker` group,
> which is root-equivalent on any host (`docker run -v /:/host --user 0` reads anything).
> Running the backup as root makes an existing capability explicit rather than adding one.

### Orphaned archives from the 2026-05-06 rename

The archive prefix changed from `wiki_full_backup_` to `wiki_backup_`, but both rotation
rules match only the new prefix, so the old set is frozen permanently:

| Location | Orphaned files | Size |
| --- | --- | --- |
| `/home/deploy/backups` | 29 | 309 MB |
| `gdrive:ServerBackups/Magazedia` | 60 | ~579 MiB |

Dead weight rather than a growing leak — the new-prefix files rotate correctly. Dry runs
are in [§8](#8-verification-sweep).

---

## 6. Restore: reuse certificates, reissue only as a fallback

Let's Encrypt rate-limits issuance, so a rebuild should **not** reissue by reflex. Order
of preference:

1. **Restore `/etc/letsencrypt` from the backup** (once the job runs as root). This
   brings back the account, the archive, and the renewal config — the rebuilt host both
   serves and *renews* without contacting Let's Encrypt at all.
2. **Restore `/opt/wiki/certs` from the backup.** HTTPS works immediately if the
   certificate has not expired, but there is no renewal lineage, so run
   `certbot certonly` once at leisure — before the 90 days are up, not during the outage.
3. **Reissue** only if no usable certificate is in the backup, or it has expired.

Check what you have before deciding:

```bash
openssl x509 -in /opt/wiki/certs/cert.pem -noout -subject -dates
```

Reissue command, if it comes to that — the container must already be running to serve the
challenge from `/opt/wiki/data/.well-known`:

```bash
certbot certonly --webroot -w /opt/wiki/data -d magazedia.com -d www.magazedia.com -d en.magazedia.com -d ja.magazedia.com -d ar.magazedia.com -d xx-test.magazedia.com -d magazedia.wiki -d www.magazedia.wiki -d en.magazedia.wiki -d ja.magazedia.wiki -d ar.magazedia.wiki -d xx-test.magazedia.wiki -d fanfare.wiki -d www.fanfare.wiki -d en.fanfare.wiki -d crtr.world -d www.crtr.world -d en.crtr.world --key-type ecdsa --email <ops-email> --agree-tos --no-eff-email
```

If nothing usable survives, bootstrap with `--standalone` per the pattern doc
§7b — the container cannot start without a certificate, and the webroot challenge is
served *by* the container, so the first issuance has to come from certbot's own
temporary server on port 80. **Then convert the renewal config back to webroot**
(pattern doc §7c), or every future renewal will try to bind port 80, fail against the
running container, and let the certificate lapse.

### Full rebuild

Follow the pattern doc §8 (Provisioning a host from bare metal) start to finish. The
Magazedia-specific values for it:

| Step | Value |
| --- | --- |
| Packages | `docker.io certbot rclone sqlite3 ufw` |
| Firewall | `OpenSSH`, `80/tcp`, `443/tcp` |
| Directories | `/opt/wiki/data`, `/opt/wiki/certs`, `chown -R 1654:1654 /opt/wiki` |
| SSH alias | `magazedia` → `deploy@magazedia.wiki`, key `id_ed25519` |
| Data | newest `wiki_backup_*.tar.gz` from `gdrive:ServerBackups/Magazedia` |
| DNS | all 18 hostnames in §2 — move them **before** requesting certificates |
| Certificates | the order above; lineage is named `en.magazedia.wiki` |
| Registry | `docker login ghcr.io` as `deploy` |
| Backup schedule | `0 4 * * * /home/deploy/backup_wiki.sh >> /home/deploy/wiki_backup.log 2>&1` in the **root** crontab |

Two Magazedia-specific traps on a rebuild:

- **The schema is not created automatically.** Nothing calls `Migrate()`, so a rebuild
  restores an existing `app.db`. Only for a genuinely new database do you run
  `src/WikiWikiWorld.Data/SqliteScripts/Create.sql` then `Load.sql` by hand.
- **The certbot lineage is `en.magazedia.wiki`, not `magazedia.com`.** The renewal
  config, the hook's `$RENEWED_LINEAGE`, and `/etc/letsencrypt/live/...` all use the
  lineage name, while the certificate's CN is `magazedia.com`.

---

## 7. Actions still requiring root

`deploy` has no passwordless `sudo`, so these needed a root shell. **Both were completed
on 2026-08-29**; kept here as the procedure for a rebuild.

**1. ~~Install the atomic renewal hook.~~ Done 2026-08-29.** Installed from
`/home/deploy/01-update-app.sh` to
`/etc/letsencrypt/renewal-hooks/deploy/01-update-app.sh`, root:root, mode 755. Still
worth running the two no-rate-limit tests in
[§2](#testing-renewal-without-spending-a-rate-limit).

**2. ~~Move the backup to the root crontab.~~ Done 2026-08-29.** This is what actually gets
`/opt/wiki/certs` and `/etc/letsencrypt` into the archives: `key.pem` is mode `0600`
owned by UID 1654 and `/etc/letsencrypt/archive` is root-only, so neither is readable as
`deploy`. The script already handles running as either user — it sets `RCLONE_CONFIG`
explicitly and chowns the finished archive back to `deploy`.

Do it as **one** command, so there is never a moment when both crontabs hold the entry
(they would collide on the same minute-stamped snapshot directory and could produce a
partial archive) or neither does:

```bash
crontab -u deploy -l 2>/dev/null | grep -v 'backup_wiki\.sh' | crontab -u deploy - && ( crontab -l 2>/dev/null | grep -v 'backup_wiki\.sh'; echo '0 4 * * * /home/deploy/backup_wiki.sh >> /home/deploy/wiki_backup.log 2>&1' ) | crontab -
```

Verify the move, then prove the contents:

```bash
crontab -u deploy -l | grep backup_wiki || echo "deploy: clean"; crontab -l | grep backup_wiki
```

```bash
/home/deploy/backup_wiki.sh && tar -tzf "$(ls -t /home/deploy/backups/wiki_backup_*.tar.gz | head -1)" | grep -E 'certs/|letsencrypt'
```

Expect `certs/cert.pem`, `certs/key.pem` and `letsencrypt.tar.gz`, and **no `NOTE:`
lines** in the run output. Those notes are the script telling you it silently skipped
something; their absence is the pass condition.

> After the move, the `crontab.txt` captured inside each archive is **root's** crontab,
> not `deploy`'s. A rebuild must reinstall the schedule with `sudo crontab -e`, not
> `crontab -e` as `deploy` — see [§6](#full-rebuild).

---

## 8. Verification sweep

Read-only audit, re-runnable as `deploy`.

```bash
ssh magazedia 'docker ps -a --format "{{.Names}} | {{.Image}} | {{.Status}}"; docker inspect -f "User=[{{.Config.User}}] Restart={{.HostConfig.RestartPolicy.Name}} Log={{.HostConfig.LogConfig.Type}} {{.HostConfig.LogConfig.Config}}" wikiwikiworld; docker inspect -f "{{range .Mounts}}{{.Source}} -> {{.Destination}}{{println}}{{end}}" wikiwikiworld'
```

```bash
ssh magazedia 'ls -lan /opt/wiki /opt/wiki/data /opt/wiki/certs; du -sh /opt/wiki/data/sitefiles'
```

```bash
ssh magazedia 'cat /etc/letsencrypt/renewal/*.conf | grep -E "authenticator|webroot_path|key_type"; systemctl list-timers --all | grep certbot; ls -la /etc/letsencrypt/renewal-hooks/deploy/'
```

```bash
echo | openssl s_client -connect magazedia.com:443 -servername magazedia.com 2>/dev/null | openssl x509 -noout -subject -dates -ext subjectAltName
```

```bash
ssh magazedia 'crontab -l | grep -v "^#"; tail -20 /home/deploy/wiki_backup.log; tar -tzf "$(ls -t /home/deploy/backups/wiki_backup_*.tar.gz | head -1)" | head -20'
```

### Clearing the orphaned archives

Dry runs first — both verified 2026-08-29:

```bash
ssh magazedia 'find /home/deploy/backups -name "wiki_full_backup_*.tar.gz" -printf "%TY-%Tm-%Td  %10s  %p\n" | sort'
```

```bash
ssh magazedia 'rclone delete --dry-run gdrive:ServerBackups/Magazedia --include "wiki_full_backup_*.tar.gz"'
```

Then, once the listings look right — neither is reversible:

```bash
ssh magazedia 'find /home/deploy/backups -name "wiki_full_backup_*.tar.gz" -delete'
```

```bash
ssh magazedia 'rclone delete gdrive:ServerBackups/Magazedia --include "wiki_full_backup_*.tar.gz"'
```

---

## 9. Open items

Ordered by how much damage each does if left alone.

1. **No restore test has ever been done.** Until one is, this document is a plan, not a
   capability.
2. **No rollback target** — only `latest` is tagged.
3. **`update.sh` lacks `set -euo pipefail`**, so a failed pull becomes an outage.
4. **`update.sh` and `backup_wiki.sh` live only on the server.** They are now inside each
   backup archive, but the authoritative copies belong in this repo under `deploy/`.
5. **`.vscode/tasks.json` is still uncommitted** — now un-ignored, so `git add` it and
   the deploy pipeline stops being single-copy.

**Resolved in this pass:** ACME webroot correct; database backed up with SQLite's online
backup; renewal hook verified, then replaced with an atomic version and installed; backup covers all
sites; container logs bounded; ICU restored; culture selector shows endonyms; orphaned
archives cleared; package advisories cleared and the
build unblocked; certificates and certbot state now captured in every backup.
