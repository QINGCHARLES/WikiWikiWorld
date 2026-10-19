# Container Deployment Pattern — .NET Web App on a Single Host

A reusable pattern for deploying an ASP.NET Core app to one Linux box as a chiseled
container, with Kestrel terminating TLS directly (no reverse proxy), SQLite and user
uploads on a bind-mounted volume, Let's Encrypt via certbot, and off-site backups.

This document is **app-agnostic**. Every project-specific value is a placeholder in
[Parameters](#1-parameters); each app keeps its own filled-in companion document
recording its actual values, plus anything that deviates from this pattern.

**Companion documents using this pattern**

| App | Document |
| --- | --- |
| WikiWikiWorld / Magazedia | `doc/server-rebuild.md` |

---

## 1. Parameters

Fill these in per app. Every command below refers to them.

| Placeholder | Meaning | Example |
| --- | --- | --- |
| `<APP>` | Container + service name | `wikiwikiworld` |
| `<IMAGE>` | Full image reference | `ghcr.io/qingcharles/wikiwikiworld` |
| `<STATE_ROOT>` | Host directory holding all persistent state | `/opt/wiki` |
| `<PRIMARY_DOMAIN>` | Certificate common name | `magazedia.com` |
| `<ALL_DOMAINS>` | Every hostname the single cert must cover | `magazedia.com`, `en.magazedia.com`, … |
| `<DEPLOY_USER>` | Unprivileged host user owning scripts | `deploy` |
| `<CLOUD_DEST>` | rclone remote + path for off-site backups | `gdrive:ServerBackups/Magazedia` |

Two constants are **not** parameters — they are fixed by the .NET container images:

- **`/app`** — the image `WORKDIR`. All in-container paths hang off it.
- **UID/GID `1654`** — the non-root user chiseled images run as. Every host directory
  bind-mounted read-write must be owned by `1654:1654`.

---

## 2. Architecture

```
Internet :80/:443
        │
        ▼
┌───────────────────────────────────────────────┐
│ container <APP>  (chiseled, runs as UID 1654) │
│   Kestrel terminates TLS itself — no nginx    │
│                                               │
│   /app          app binaries (in image)       │
│   /app/data     ◀── bind ── <STATE_ROOT>/data │
│   /app/certs    ◀── bind ── <STATE_ROOT>/certs│
└───────────────────────────────────────────────┘
        ▲                      ▲
        │                      │
   certbot writes         nightly backup
   challenge + certs      reads (host side)
```

Everything mutable lives under `<STATE_ROOT>` on the host. The container is disposable:
destroy and recreate it freely, and never store anything inside it.

### Why no reverse proxy

One app, one host, one certificate. Kestrel binds `:80` and `:443` directly, which
removes nginx as a component to install, configure, patch, and document. The trade-off:
no proxy-level rate limiting, no zero-downtime cutover, and only one TLS certificate for
the whole host. If you later add a second app to the box, add the proxy then.

Because the app is the edge server, it must **not** enable `UseForwardedHeaders` —
there is no trusted proxy in front of it, so honouring `X-Forwarded-For` would let any
client spoof its own IP.

### Ports below 1024 as a non-root user

Docker sets `net.ipv4.ip_unprivileged_port_start=0` inside containers, so UID 1654 can
bind `:80`/`:443` without extra capabilities. This is a **Docker-specific** default —
under Podman, Kubernetes, or a hardened runtime you would need `NET_BIND_SERVICE` or a
higher port plus port-forwarding.

---

## 3. Building and publishing the image

No Dockerfile. The .NET SDK builds and pushes the image directly:

```bash
dotnet publish src/<APP>/<APP>.csproj /t:PublishContainer -p:ContainerRegistry=ghcr.io -p:ContainerRepository=<owner>/<APP> -p:ContainerFamily=noble-chiseled -p:ContainerImageTags='"latest;<GIT_SHA>"' -c Release
```

Authenticate once per workstation with `docker login ghcr.io` (or export
`GITHUB_TOKEN`). **Never put the registry PAT in a tracked file** such as
`.vscode/tasks.json` — pass it via the environment or rely on the stored docker login.

### Tag with the commit SHA, not just `latest`

`ContainerImageTags` publishes both. `latest` is what routine deploys pull; the SHA tag
is what makes rollback possible:

```bash
/home/<DEPLOY_USER>/update.sh <PREVIOUS_SHA>
```

Without an immutable tag, "deploy the previous build" means "rebuild from an older
commit and hope it is reproducible."

### Choosing the chiseled variant

| Variant | Contains | Use when |
| --- | --- | --- |
| `noble-chiseled` | Runtime only. **No ICU, no tzdata.** | The app sets `InvariantGlobalization=true` and never formats culture-aware dates, currency, or sorts. |
| `noble-chiseled-extra` | Adds ICU + tzdata | The app uses `CultureInfo`, formats currency (`ToString("C")`), parses culture-sensitive input, or converts time zones. |

**`noble-chiseled` sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`** — verified on a
running container, 2026-08-29. Confirm for any image with:

```bash
docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' <APP> | grep -i globalization
```

Getting this wrong fails at **runtime, in production only**, because a Windows dev
machine always has full ICU. Two failure shapes:

- **Throws.** `CultureInfo.GetCultureInfo("en-US")` raises `CultureNotFoundException`
  under invariant mode. Any request path that formats currency (`ToString("C")`),
  parses culture-sensitive input, or names a specific culture goes to a 500.
- **Degrades silently.** Where the call is wrapped in a `try`/`catch`, or where only
  formatting differs, you get invariant output — `¤` instead of `$`, raw culture codes
  instead of language names, ordinal instead of linguistic sort order. Nothing errors;
  the output is just wrong, and only a human notices.

An app that takes payment or renders currency should treat `-extra` as mandatory. An app
that only uses culture codes as routing keys can stay on the base image.

Chiseled images also have **no shell and no package manager**. That is the point —
minimal attack surface — but it means:

- `docker exec <APP> sh` will not work. Debug via `docker logs` and the app's own
  `/health` endpoint.
- Any maintenance script (backup, vacuum, log rotation) runs **on the host** against the
  bind-mounted files, never inside the container.
- There are **no fonts**. Libraries that rasterise text or measure glyphs need
  `-extra` plus an installed font package, or a fully managed alternative.

---

## 4. Host filesystem layout

| Path | Purpose | Owner | Mode |
| --- | --- | --- | --- |
| `<STATE_ROOT>/data/` | Database + user uploads. **The irreplaceable data.** | `1654:1654` | `750` |
| `<STATE_ROOT>/certs/` | `cert.pem` + `key.pem` as Kestrel reads them | `1654:1654` | `750` |
| `/home/<DEPLOY_USER>/` | `update.sh`, `backup.sh`, rclone config | `<DEPLOY_USER>` | `750` |
| `/home/<DEPLOY_USER>/backups/` | Local backup staging before upload | `<DEPLOY_USER>` | `700` |

Keep the ACME challenge webroot **inside** `<STATE_ROOT>/data` rather than as a separate
mount — see [§5](#5-tls-with-certbot). One bind mount for state means one thing to back
up and one thing to `chown`.

---

## 5. TLS with certbot

Three things must line up, and they are the most common source of a broken rebuild.

### 5.1 The challenge path must be the path the app actually serves

The app serves `/.well-known` from a directory it chooses — typically under its data
path, deliberately registered **before** `UseHttpsRedirection()` so that plain-HTTP
challenge requests on port 80 are answered instead of being 308-redirected to HTTPS.

Point certbot's webroot at the host directory backing that same path. If the app serves
`/.well-known` from `/app/data/.well-known`, and `<STATE_ROOT>/data` is mounted at
`/app/data`, then the webroot is simply:

```bash
certbot certonly --webroot -w <STATE_ROOT>/data -d <PRIMARY_DOMAIN>
```

certbot creates `<webroot>/.well-known/acme-challenge/` itself. **No extra bind mount is
needed**, and adding one that points somewhere the app does not read produces challenges
that 404 — renewals then fail silently until the certificate expires 90 days later.

> Verify the mapping against the app's source before trusting it. Grep the startup file
> for `.well-known` and confirm which directory the `PhysicalFileProvider` uses and that
> it is registered before `UseHttpsRedirection()`.

### 5.2 One certificate must cover every hostname

Kestrel is configured with a single `cert.pem`, so it must be a multi-SAN certificate
listing **all** of `<ALL_DOMAINS>`:

```bash
certbot certonly --webroot -w <STATE_ROOT>/data -d example.com -d www.example.com -d en.example.com --email <ops-email> --agree-tos --no-eff-email
```

**Adding a domain to the app later requires re-issuing this certificate with the new
`-d` and restarting the container.** Nothing in the app will remind you.

### 5.3 Renewal must copy the certs and restart the container

certbot writes to `/etc/letsencrypt/live/<PRIMARY_DOMAIN>/`. Kestrel reads
`/app/certs/cert.pem`. Nothing bridges those two automatically, and Kestrel loads the
PEM **once at startup** — replacing the file on disk does not reload it.

Create `/etc/letsencrypt/renewal-hooks/deploy/<APP>.sh`, mode `0755`, owned by root:

```bash
#!/bin/bash
set -euo pipefail
LIVE="/etc/letsencrypt/live/<PRIMARY_DOMAIN>"
CERTS="<STATE_ROOT>/certs"

install -o 1654 -g 1654 -m 640 "$LIVE/fullchain.pem" "$CERTS/cert.pem"
install -o 1654 -g 1654 -m 640 "$LIVE/privkey.pem"   "$CERTS/key.pem"

docker restart <APP>
```

Test it without waiting 60 days:

```bash
certbot renew --dry-run
```

```bash
certbot renew --force-renewal
```

The first proves the challenge path works; the second proves the hook and restart work.
A renewal that succeeds but never restarts the container is indistinguishable from a
working setup for two months, then takes the site down.

---

## 6. Deployment script

`/home/<DEPLOY_USER>/update.sh`, mode `0755`. Takes an optional tag so it doubles as the
rollback tool.

```bash
#!/bin/bash
set -euo pipefail

TAG="${1:-latest}"
IMAGE="<IMAGE>:$TAG"
NAME="<APP>"
STATE="<STATE_ROOT>"

echo "Pulling $IMAGE ..."
docker pull "$IMAGE"

echo "Replacing container ..."
docker stop "$NAME" 2>/dev/null || true
docker rm   "$NAME" 2>/dev/null || true

docker run -d \
  --name "$NAME" \
  --restart unless-stopped \
  -p 80:80 \
  -p 443:443 \
  -v "$STATE/data:/app/data" \
  -v "$STATE/certs:/app/certs" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  "$IMAGE"

# Reclaim disk from superseded images; without this the host fills up over months.
docker image prune -f

echo "Deployed $IMAGE."
```

Notes:

- `set -euo pipefail` matters. Without it a failed `docker pull` still tears down the
  running container and then fails to start a new one — turning a no-op into an outage.
- `--restart unless-stopped` brings the app back after a host reboot.
- Deploys have a **few seconds of downtime** by design. Zero-downtime needs a proxy.
- `ASPNETCORE_ENVIRONMENT=Production` is explicit rather than relying on an image
  default, so the same script works against any image.

### Secrets

`appsettings.Production.json` is compiled **into the image**. Anything secret in it
(signing keys, SMTP passwords, payment credentials) is therefore baked into every layer
pushed to the registry, and rotating it requires a rebuild and redeploy.

Prefer `-e` environment variables in `update.sh`, or a `--env-file` on the host readable
only by `<DEPLOY_USER>`, for anything that must be rotated independently of a release.
Keep only non-secret defaults in the committed JSON.

---

## 7. Backups

The container is disposable and the image is in a registry. `<STATE_ROOT>/data` is the
only thing whose loss is permanent.

### Do not `cp` a live SQLite database

The app runs in WAL mode with an open connection. A plain `cp` of the database races
against in-flight writes and can capture a torn page, or a database whose committed
transactions live in a `-wal` file you did not copy — producing an archive that restores
to a corrupt database. You discover this during a recovery, which is the worst possible
moment.

Use SQLite's own online backup, which is safe against a live writer:

```bash
sqlite3 "<STATE_ROOT>/data/app.db" ".backup '/path/to/snapshot/app.db'"
```

Install it on the host with `apt install -y sqlite3`. It runs host-side against the
bind-mounted file — the chiseled container has no shell and needs no involvement.

### Run the backup as root, not as the deploy user

The container writes its state as UID 1654, and a TLS private key is mode `0600` — so
`<DEPLOY_USER>` **cannot read it**, and neither can it read `/etc/letsencrypt`. A backup
running as `<DEPLOY_USER>` therefore silently omits exactly the material you need to
restore TLS without re-issuing.

Put the job in the **root** crontab. This grants no new privilege: `<DEPLOY_USER>` is in
the `docker` group, which is already root-equivalent on any host
(`docker run -v /:/host --user 0 …` reads anything). Running the backup as root makes an
existing capability explicit rather than adding one.

Write the script so it works correctly under either user — name the rclone config
explicitly (root would otherwise look in `/root/.config` and find no remote), hand the
finished archive back to `<DEPLOY_USER>`, and degrade with a visible notice rather than
aborting when something is unreadable. A backup that dies on one unreadable file
produces no backup at all.

### Script

`/home/<DEPLOY_USER>/backup.sh`, mode `0750`:

```bash
#!/bin/bash
set -euo pipefail

STATE="<STATE_ROOT>"
HOME_DIR="/home/<DEPLOY_USER>"
BACKUP_DIR="$HOME_DIR/backups"
CLOUD_DEST="<CLOUD_DEST>"
LOCAL_RETENTION_DAYS=28
CLOUD_RETENTION="60d"

TIMESTAMP="$(date +%Y-%m-%d_%H-%M)"
ARCHIVE="<APP>_backup_$TIMESTAMP.tar.gz"
SNAPSHOT="$BACKUP_DIR/snapshot_$TIMESTAMP"

mkdir -p "$SNAPSHOT"
trap 'rm -rf "$SNAPSHOT"' EXIT

# Name the rclone remote explicitly so this behaves identically as root or as the
# deploy user.
export RCLONE_CONFIG="${RCLONE_CONFIG:-$HOME_DIR/.config/rclone/rclone.conf}"

# 1. Database — consistent online copy, not cp.
sqlite3 "$STATE/data/app.db" ".backup '$SNAPSHOT/app.db'"

# 2. Uploads — the whole tree, never a whitelist of subdirectories, so new content
#    types are covered automatically instead of being silently omitted.
cp -r "$STATE/data/sitefiles" "$SNAPSHOT/sitefiles"

# 3. Certificates and certbot state. Restored first on a rebuild so TLS comes back
#    without calling the CA, which rate-limits issuance. Both need root; degrade with a
#    visible notice rather than aborting the whole backup.
if cp -r "$STATE/certs" "$SNAPSHOT/certs" 2>/dev/null; then
	echo "Copied deployed certificates."
else
	rm -rf "$SNAPSHOT/certs"
	echo "NOTE: $STATE/certs not fully readable as $(id -un) - certificates NOT backed up."
fi

if [ -r /etc/letsencrypt/archive ]; then
	tar -czf "$SNAPSHOT/letsencrypt.tar.gz" -C /etc letsencrypt
else
	echo "NOTE: /etc/letsencrypt not readable as $(id -un) - renewal state NOT backed up."
fi

# 4. The scripts and schedule needed to rebuild the box.
cp "$HOME_DIR/update.sh" "$HOME_DIR/backup.sh" "$SNAPSHOT/"
cp "$HOME_DIR/.config/rclone/rclone.conf" "$SNAPSHOT/rclone.conf" 2>/dev/null || true
crontab -l > "$SNAPSHOT/crontab.txt" 2>/dev/null || true

# 5. Compress and ship. Keep archives owned by the deploy user even when run as root.
tar -czf "$BACKUP_DIR/$ARCHIVE" -C "$BACKUP_DIR" "snapshot_$TIMESTAMP"
chown <DEPLOY_USER>:<DEPLOY_USER> "$BACKUP_DIR/$ARCHIVE" 2>/dev/null || true
chmod 600 "$BACKUP_DIR/$ARCHIVE"
rclone copy "$BACKUP_DIR/$ARCHIVE" "$CLOUD_DEST"

# 6. Rotate both ends.
find "$BACKUP_DIR" -name '<APP>_backup_*.tar.gz' -mtime "+$LOCAL_RETENTION_DAYS" -delete
rclone delete --min-age "$CLOUD_RETENTION" "$CLOUD_DEST" --include '<APP>_backup_*.tar.gz'

echo "Backup complete: $ARCHIVE"
```

> **Never rename the archive without migrating the old files.** Rotation matches on the
> filename prefix at both ends. Change `<APP>_backup_` to anything else and every
> existing archive stops matching, so it is retained forever — silently, on local disk
> and in cloud storage alike. If you must rename, delete or rename the old set in the
> same change.

Including `rclone.conf` in the archive is deliberately circular — it is what lets a
rebuilt host reach the backups. Keep a second copy in a password manager, because the
copy inside the archive is unreachable until you can already authenticate.

### Schedule

`crontab -e` as `<DEPLOY_USER>`:

```
0 4 * * * /home/<DEPLOY_USER>/backup.sh >> /home/<DEPLOY_USER>/backup.log 2>&1
```

certbot installs its own systemd timer for renewals; confirm with
`systemctl list-timers | grep certbot`.

### Restore-test it

An untested backup is a hypothesis. Once a quarter, restore the newest archive onto a
throwaway VM by following [§8](#8-rebuilding-from-zero) and confirm the app starts and
the data is current. Record the date you last did this in the app's companion document.

---

## 8. Provisioning a host from bare metal

One path covers both cases — a brand-new deployment and a rebuild after losing the box.
They differ only at step 6 (data) and step 7 (certificates); everything else is
identical. Budget about 20 minutes, most of it waiting for DNS.

Run steps 1–4 as `root`. Steps 5 onward are marked with the user they need.

### 1. Base system and packages

```bash
apt update && apt upgrade -y
apt install -y docker.io certbot rclone sqlite3 ufw
```

`sqlite3` is for host-side database backup — the chiseled container has no shell, so
every maintenance task runs out here. `rclone` ships the backups off-box.

### 2. Firewall

The app binds `:80` and `:443` directly with no proxy in front, so the host's own
firewall is the only network filter there is. Allow SSH *before* enabling, or you lock
yourself out:

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw enable
```

```bash
ufw status verbose
```

> Docker publishes ports by writing its own `iptables` rules, which bypass UFW's
> `INPUT` chain — a container published with `-p` is reachable even when UFW says
> otherwise. That is harmless here, because `:80` and `:443` are exactly the ports we
> intend to expose. It matters if you ever publish an admin or database port: bind it to
> loopback (`-p 127.0.0.1:5432:5432`) rather than relying on UFW to hide it.

### 3. Deploy user

```bash
adduser --disabled-password --gecos "" <DEPLOY_USER>
usermod -aG docker <DEPLOY_USER>
```

`--disabled-password` creates no password, so the account is reachable only by SSH key —
set that up in step 5. Add a password with `passwd <DEPLOY_USER>` only if you want
console access.

> The `docker` group is root-equivalent: any member can run
> `docker run -v /:/host --user 0 …` and read or write the whole filesystem. Treat
> membership as granting root, and keep the group small.

### 4. Directory structure

```bash
mkdir -p <STATE_ROOT>/data <STATE_ROOT>/certs
chown -R 1654:1654 <STATE_ROOT>
chmod 750 <STATE_ROOT>/data <STATE_ROOT>/certs
```

UID 1654 is the non-root user the chiseled image runs as. **This is the step most often
missed**, and its symptom is an app that starts and then fails every write.

There is deliberately no `/var/www/certbot`: the ACME webroot lives inside
`<STATE_ROOT>/data`, which is already mounted into the container — see
[§5](#5-tls-with-certbot).

### 5. SSH key access — from your workstation

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh <DEPLOY_USER>@<HOST> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

```bash
ssh-copy-id -i ~/.ssh/id_ed25519.pub <DEPLOY_USER>@<HOST>
```

Prefer ed25519 over RSA. Then add a host alias to `~/.ssh/config` so the target lives in
one place rather than being retyped into every script:

```
Host <ALIAS>
    HostName <HOST>
    User <DEPLOY_USER>
    IdentityFile ~/.ssh/id_ed25519
    IdentitiesOnly yes
    ServerAliveInterval 60
    ServerAliveCountMax 3
```

`IdentitiesOnly yes` matters: without it SSH offers every key in `~/.ssh` in turn, and a
server with a low `MaxAuthTries` can reject you before reaching the right one. The file
must be named exactly `config` — SSH ignores `config.txt`.

Confirm it works before going further:

```bash
ssh -o BatchMode=yes <ALIAS> 'id -un'
```

### 6. Data — the fork

**New deployment:** nothing to do. The app creates its database on first run, or you
apply the schema by hand if it has no automatic migration.

**Rebuild:** restore from the newest archive. Fetch it with `rclone` once step 8 has
configured the remote, or `scp` it from your workstation:

```bash
tar -xzf <APP>_backup_YYYY-MM-DD_HH-MM.tar.gz
cd snapshot_*
cp app.db       <STATE_ROOT>/data/app.db
cp -r sitefiles <STATE_ROOT>/data/sitefiles
```

Then re-apply ownership, because restored files land owned by root:

```bash
chown -R 1654:1654 <STATE_ROOT>
```

### 7. DNS, then certificates

**Point every hostname at the new host now and wait for propagation.** The HTTP-01
challenge resolves each name back to this box, so certificates cannot be issued until
DNS has moved.

```bash
dig +short <PRIMARY_DOMAIN>
```

#### 7a. Rebuild — reuse before reissuing

The CA rate-limits issuance, so on a rebuild try the backup first, in this order:

1. **Restore `/etc/letsencrypt`** from the archive (present only if the backup ran as
   root). This brings back the account, archive, and renewal config — the host both
   serves *and renews* without contacting the CA at all:

   ```bash
   tar -xzf letsencrypt.tar.gz -C /etc
   ```

2. **Restore `<STATE_ROOT>/certs`** from the archive. HTTPS works immediately if the
   certificate has not expired, but with no renewal lineage you must run `certbot
   certonly` once at leisure — before the 90 days are up, not during the outage.

3. **Reissue**, per 7b, only if neither is usable.

Check what you have before deciding:

```bash
openssl x509 -in <STATE_ROOT>/certs/cert.pem -noout -subject -dates
```

#### 7b. New deployment — bootstrap with `--standalone`

There is a chicken-and-egg problem on a brand-new host. Kestrel is configured with an
HTTPS endpoint pointing at `cert.pem`, so **the container will not start without a
certificate** — but the webroot challenge is served *by the container*.

Break it with `--standalone`, which runs certbot's own temporary web server on port 80.
Nothing is bound to port 80 yet, so this works exactly once, at the beginning:

```bash
certbot certonly --standalone -d <PRIMARY_DOMAIN> -d www.<PRIMARY_DOMAIN> --key-type ecdsa --email <ops-email> --agree-tos --no-eff-email
```

List **every** hostname the app answers on, including ones that only exist to be
redirected — TLS completes before any redirect middleware runs, so a name missing from
the certificate gets a warning rather than a redirect.

Publish the result where the container expects it, and lock down the key:

```bash
install -o 1654 -g 1654 -m 644 /etc/letsencrypt/live/<PRIMARY_DOMAIN>/fullchain.pem <STATE_ROOT>/certs/cert.pem
install -o 1654 -g 1654 -m 600 /etc/letsencrypt/live/<PRIMARY_DOMAIN>/privkey.pem   <STATE_ROOT>/certs/key.pem
```

#### 7c. Switch renewals to webroot — do not skip this

certbot records the authenticator used at issuance. Left as `standalone`, **every future
renewal will try to bind port 80 and fail**, because the container now holds it. The
certificate then quietly expires.

Edit `/etc/letsencrypt/renewal/<lineage>.conf` and set the authenticator to webroot:

```
authenticator = webroot
webroot_path = <STATE_ROOT>/data,
[[webroot_map]]
<PRIMARY_DOMAIN> = <STATE_ROOT>/data
www.<PRIMARY_DOMAIN> = <STATE_ROOT>/data
```

One `webroot_map` line per hostname on the certificate. Remove any `standalone` lines.
Verify after the container is running (step 9) with `certbot renew --dry-run`.

#### 7d. Install the renewal deploy hook

Renewal writes to `/etc/letsencrypt/live/...`, which is not where the container reads
from, and Kestrel loads the PEM only at startup. Without this hook a successful renewal
changes nothing the running app can see. See [§5.3](#53-renewal-must-copy-the-certs-and-restart-the-container)
for the script; install it as:

```bash
install -o root -g root -m 755 <path>/01-update-app.sh /etc/letsencrypt/renewal-hooks/deploy/01-update-app.sh
```

### 8. Scripts, registry access, and schedule — as `<DEPLOY_USER>`

Restore `update.sh` and `backup.sh` from the archive, or write them from
[§6](#6-deployment-script) and [§7](#7-backups):

```bash
chmod 755 ~/update.sh && chmod 750 ~/backup.sh
```

rclone config, if not restored — paste the remote definition from your password manager:

```bash
mkdir -p ~/.config/rclone && nano ~/.config/rclone/rclone.conf && chmod 600 ~/.config/rclone/rclone.conf
```

Authenticate to the registry so `docker pull` can reach a private image:

```bash
docker login ghcr.io
```

Schedule the backup **in the root crontab**, not the deploy user's — see
[§7](#run-the-backup-as-root-not-as-the-deploy-user). As root:

```bash
crontab -e
```

```
0 4 * * * /home/<DEPLOY_USER>/backup.sh >> /home/<DEPLOY_USER>/backup.log 2>&1
```

### 9. Launch

```bash
sudo -u <DEPLOY_USER> /home/<DEPLOY_USER>/update.sh
```

```bash
docker logs -f <APP>
```

### 10. Verify

Work through [§9](#9-post-deploy-verification) before calling it done — in particular
`certbot renew --dry-run`, which is the only thing that proves step 7c actually took.

### App-side prerequisite

The image must carry an `appsettings.Production.json` whose paths match the mounts —
this is what makes `/app/data` and `/app/certs` meaningful:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/app/data/app.db"
  },
  "Kestrel": {
    "Endpoints": {
      "HttpsInlineCertAndKeyFile": {
        "Url": "https://[::]:443",
        "Certificate": {
          "Path": "/app/certs/cert.pem",
          "KeyPath": "/app/certs/key.pem"
        }
      },
      "Http": { "Url": "http://[::]:80" }
    }
  }
}
```

Keep secrets out of it — the file is compiled into the image and pushed to the registry.
See [§6](#secrets).

## 9. Post-deploy verification

Run after every rebuild, and after any change to certificates or mounts.

| Check | Command | Expected |
| --- | --- | --- |
| Container healthy | `docker ps` | `Up`, not restarting |
| No startup errors | `docker logs <APP> \| tail -50` | clean |
| Runs as 1654 | `docker inspect -f '{{.Config.User}}' <APP>` | `1654` or `app` |
| Mounts correct | `docker inspect -f '{{json .Mounts}}' <APP>` | data + certs only |
| HTTPS serves | `curl -sSI https://<PRIMARY_DOMAIN>` | `200` |
| Cert covers all names | `openssl s_client -connect <PRIMARY_DOMAIN>:443 </dev/null 2>/dev/null \| openssl x509 -noout -text \| grep DNS:` | every `<ALL_DOMAINS>` |
| ACME path reachable | write a file into `<STATE_ROOT>/data/.well-known/acme-challenge/` and `curl` it over **http://** | the file contents, **not** a 308 |
| App can write | upload a file / save a record | succeeds |
| Renewal wired | `certbot renew --dry-run` | success |
| Renewal uses webroot, not standalone | `grep authenticator /etc/letsencrypt/renewal/*.conf` | `webroot` |
| Firewall up | `ufw status verbose` | active; 22, 80, 443 allowed |
| Backup runs | `sudo -u <DEPLOY_USER> ./backup.sh` | archive appears in `<CLOUD_DEST>` |

Delete the ACME probe file afterwards.

---

## 10. Known limitations

Accepted trade-offs of this pattern, recorded so they are decisions rather than
surprises:

- **Deploys drop connections** for a few seconds. No proxy, no blue/green.
- **One certificate for the host.** New domain ⇒ reissue + restart.
- **Single host, no redundancy.** Recovery time is a rebuild; recovery point is the last
  nightly backup — up to 24 hours of loss.
- **No shell in the container.** All maintenance is host-side.
- **Secrets baked into the image** unless moved to environment variables.
- **`docker.io` from the Ubuntu archive** lags upstream Docker CE. Fine for a single
  host; switch to the official repository if you need newer features.
