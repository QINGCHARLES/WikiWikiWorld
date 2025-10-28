# WikiWikiWorld Build Instructions (Cold Boot)

This document contains the exact commands needed to compile WikiWikiWorld in a fresh session with the JWT-authenticated proxy environment.

## Quick Start (One Command)

```bash
bash scripts/install_dotnet9.sh && \
eval "$(python3 -c "import os; from urllib.parse import urlsplit; s = urlsplit(os.environ.get('HTTPS_PROXY','')); print(f'export TARGET_PROXY_HOST={s.hostname}'); print(f'export TARGET_PROXY_PORT={s.port}'); print(f'export PROXY_BEARER={s.password or \"\"}')")" && \
python3 scripts/bearer_proxy.py --listen 127.0.0.1:8888 --also-auth-header --with-basic > /tmp/bearer_proxy.log 2>&1 & sleep 2 && \
unset http_proxy https_proxy && \
export HTTPS_PROXY=http://127.0.0.1:8888 HTTP_PROXY=http://127.0.0.1:8888 ALL_PROXY=http://127.0.0.1:8888 PATH=/usr/local/dotnet:$PATH && \
dotnet restore WikiWikiWorld.slnx && \
dotnet build WikiWikiWorld.slnx -c Release --no-restore
```

---

## Step-by-Step Instructions

### Step 1: Install .NET 9 SDK

```bash
bash scripts/install_dotnet9.sh
```

**What it does:**
- Downloads and installs .NET 9.0.306 SDK to `/usr/local/dotnet`
- Adds dotnet to system PATH via `/etc/profile.d/dotnet.sh`
- Only runs if `CLAUDE_CODE_REMOTE=true` (skips on local machine)

**Expected output:**
```
.NET SDK:
 Version:           9.0.306
 ...
```

---

### Step 2: Start the Bearer Proxy

```bash
# Extract current proxy credentials from environment using Python (more reliable than cut)
eval "$(python3 -c "import os; from urllib.parse import urlsplit; s = urlsplit(os.environ.get('HTTPS_PROXY','')); print(f'export TARGET_PROXY_HOST={s.hostname}'); print(f'export TARGET_PROXY_PORT={s.port}'); print(f'export PROXY_BEARER={s.password or \"\"}')")"

# Start the bearer proxy in background
python3 scripts/bearer_proxy.py --listen 127.0.0.1:8888 --also-auth-header --with-basic > /tmp/bearer_proxy.log 2>&1 &

# Wait for it to start
sleep 2
```

**What it does:**
- Extracts the JWT token from the current `HTTPS_PROXY` environment variable
- Starts a local HTTP proxy on `127.0.0.1:8888`
- The proxy intercepts .NET requests and adds proper JWT Bearer authentication
- Tries multiple auth strategies (Bearer, Authorization header, Basic auth)

**Expected output:**
```
[bearer-proxy] listening on 127.0.0.1:8888 -> proxy <host>:<port>
```

**Verify it's running:**
```bash
ps aux | grep bearer_proxy.py | grep -v grep
```

---

### Step 3: Configure Environment for .NET

```bash
# Point dotnet at the local bearer proxy instead of corporate proxy
unset http_proxy https_proxy
export HTTPS_PROXY=http://127.0.0.1:8888
export HTTP_PROXY=http://127.0.0.1:8888
export ALL_PROXY=http://127.0.0.1:8888
export PATH=/usr/local/dotnet:$PATH
```

**What it does:**
- Clears lowercase proxy vars that might interfere
- Points all .NET traffic to the local bearer proxy
- Ensures dotnet is on PATH

---

### Step 4: Restore NuGet Packages

```bash
dotnet restore WikiWikiWorld.slnx
```

**What it does:**
- Downloads all NuGet package dependencies (50+ packages)
- Uses the bearer proxy to authenticate with api.nuget.org
- Caches packages in `~/.nuget/packages/`

**Expected output:**
```
Determining projects to restore...
  Restored /home/user/WikiWikiWorld/src/WikiWikiWorld.Data/WikiWikiWorld.Data.csproj (in X sec).
  Restored /home/user/WikiWikiWorld/src/WikiWikiWorld.Database/WikiWikiWorld.Database.csproj (in X sec).
  Restored /home/user/WikiWikiWorld/src/WikiWikiWorld.Web/WikiWikiWorld.Web.csproj (in X sec).
```

---

### Step 5: Build the Solution

```bash
dotnet build WikiWikiWorld.slnx -c Release --no-restore
```

**What it does:**
- Compiles all 3 projects (Database, Data, Web)
- Uses Release configuration for optimized builds
- Skips restore since we just did it

**Expected output:**
```
WikiWikiWorld.Database -> /home/user/WikiWikiWorld/src/WikiWikiWorld.Database/bin/Release/net9.0/WikiWikiWorld.Database.dll
WikiWikiWorld.Data -> /home/user/WikiWikiWorld/src/WikiWikiWorld.Data/bin/Release/net9.0/WikiWikiWorld.Data.dll
WikiWikiWorld.Web -> /home/user/WikiWikiWorld/src/WikiWikiWorld.Web/bin/Release/net9.0/WikiWikiWorld.Web.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:XX.XX
```

---

## Troubleshooting

### Bearer Proxy Issues

**Check if proxy is running:**
```bash
ps aux | grep bearer_proxy.py | grep -v grep
```

**View proxy logs:**
```bash
tail -20 /tmp/bearer_proxy.log
```

**Restart the proxy:**
```bash
pkill -9 -f bearer_proxy.py
# Then re-run Step 2
```

---

### Connection Refused Errors

If you see `Connection refused (127.0.0.1:8888)`:

1. The proxy crashed or wasn't started
2. Re-run Step 2 to start it
3. Check logs: `cat /tmp/bearer_proxy.log`

---

### 401 Unauthorized Errors

If you see `failed with status code '401'`:

1. The JWT token may have expired (they rotate periodically)
2. Re-run Step 2 to extract the current token from `HTTPS_PROXY`
3. The corporate proxy credentials changed - verify `echo $HTTPS_PROXY` has valid credentials

---

### NuGet Restore Failures

If restore fails with NU1301 errors:

1. Verify bearer proxy is running: `ps aux | grep bearer_proxy`
2. Check proxy can reach NuGet: `curl -x http://127.0.0.1:8888 https://api.nuget.org/v3/index.json`
3. Verify environment: `echo $HTTPS_PROXY` should be `http://127.0.0.1:8888`

---

## Technical Details

### Why We Need the Bearer Proxy

**.NET's Limitation:**
- .NET's HttpClient only supports `Proxy-Authorization: Basic` and NTLM
- It cannot send `Proxy-Authorization: Bearer <JWT>` headers
- The corporate proxy requires JWT Bearer authentication

**How bearer_proxy.py Solves This:**
1. Acts as a local HTTP proxy that .NET can talk to (no auth required)
2. Intercepts CONNECT requests from .NET
3. Adds proper `Proxy-Authorization: Bearer <JWT>` header
4. Forwards to the corporate proxy with correct authentication
5. Tunnels encrypted traffic between .NET and destination

**Multi-Strategy Authentication:**
The proxy tries 4 different auth strategies in order:
1. `Proxy-Authorization: Bearer <token>` (strips jwt_ prefix)
2. Both `Proxy-Authorization: Bearer` + `Authorization: Bearer`
3. `Proxy-Authorization: Basic <base64(username:JWT)>`
4. Both `Proxy-Authorization: Basic` + `Authorization: Basic`

This handles different proxy configurations and auth requirements.

---

## Helper Scripts Reference

All scripts are in the `scripts/` folder:

- **`install_dotnet9.sh`** - Installs .NET 9 SDK using Microsoft's official installer
- **`bearer_proxy.py`** - Local proxy that adds JWT Bearer auth headers (THE KEY SCRIPT)
- **`setup_nuget_proxy.sh`** - Configures NuGet.Config with proxy settings (not needed with bearer proxy)
- **`restore_and_build.sh`** - Wrapper script for restore + build
- **`download_nuget_packages.sh`** - Manual wget-based package downloader (fallback)
- **`fix_proxy_and_restore.sh`** - URL-encoded proxy attempt (superseded by bearer_proxy.py)

---

## Build Outputs

Successful build produces DLLs in:
```
src/WikiWikiWorld.Database/bin/Release/net9.0/WikiWikiWorld.Database.dll  (35 KB)
src/WikiWikiWorld.Data/bin/Release/net9.0/WikiWikiWorld.Data.dll          (201 KB)
src/WikiWikiWorld.Web/bin/Release/net9.0/WikiWikiWorld.Web.dll            (252 KB)
```

Plus 50+ dependency DLLs (NuGet packages) in the Web project output.

---

## Environment Variables Summary

**Before starting bearer proxy:**
- `HTTPS_PROXY` = corporate proxy URL with JWT credentials (auto-set by environment)

**After starting bearer proxy:**
- `HTTPS_PROXY` = `http://127.0.0.1:8888` (local proxy)
- `HTTP_PROXY` = `http://127.0.0.1:8888`
- `ALL_PROXY` = `http://127.0.0.1:8888`
- `PATH` includes `/usr/local/dotnet`
- `http_proxy` and `https_proxy` are **unset**

---

## Success Criteria

Build is successful when you see:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

All 3 DLL files should exist in their respective `bin/Release/net9.0/` directories.
