#!/usr/bin/env bash
set -euo pipefail

SLN="${1:-WikiWikiWorld.slnx}"

# Make sure dotnet is on PATH in this shell
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then source "${CLAUDE_ENV_FILE}" 2>/dev/null || true; fi
export PATH="/usr/local/dotnet:${PATH}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[fix-proxy] dotnet not found on PATH; run scripts/install_dotnet9.sh first." >&2
  exit 1
fi

# Prefer HTTPS proxy; fall back to HTTP
RAW_PROXY="${HTTPS_PROXY:-${HTTP_PROXY:-}}"
if [ -z "${RAW_PROXY}" ]; then
  echo "[fix-proxy] No HTTPS_PROXY/HTTP_PROXY set; cannot configure proxy." >&2
  exit 1
fi

# URL-encode username/password in the proxy URL using Python (present in your VM)
ENC_PROXY="$(python3 - <<'PY'
import os, sys
from urllib.parse import urlsplit, urlunsplit, quote

raw = os.environ.get('RAW_PROXY', '')
if not raw:
    print('', end='')
    sys.exit(0)

s = urlsplit(raw)
# username/password may be None
user = '' if s.username is None else quote(s.username, safe='')
pwd  = '' if s.password is None else quote(s.password, safe='')
host = s.hostname or ''
port = f":{s.port}" if s.port else ''
netloc = f"{user}:{pwd}@{host}{port}" if (user or pwd) else f"{host}{port}"
print(urlunsplit((s.scheme or 'http', netloc, s.path, s.query, s.fragment)), end='')
PY
)"
if [ -z "${ENC_PROXY}" ]; then
  echo "[fix-proxy] Could not encode proxy URL." >&2
  exit 1
fi

# Export encoded proxies for this restore
export HTTPS_PROXY="${ENC_PROXY}"
export HTTP_PROXY="${ENC_PROXY}"
export ALL_PROXY="${ENC_PROXY}"

# These can interfere; rely on encoded env only
unset http_proxy https_proxy

# Optional knobs that help behind picky proxies
export DOTNET_SYSTEM_NET_HTTP_USEPROXY=true
export DOTNET_SYSTEM_NET_HTTP_SOCKETSHTTPHANDLER_HTTP2SUPPORT=false

echo "[fix-proxy] Using proxy: ${ENC_PROXY}"
dotnet --info >/dev/null

# Clean stale state and restore/build
dotnet nuget locals all --clear || true

echo "[fix-proxy] Restoring ${SLN}…"
dotnet restore "${SLN}" --verbosity minimal

echo "[fix-proxy] Building ${SLN} (Release)…"
dotnet build "${SLN}" -c Release --no-restore
