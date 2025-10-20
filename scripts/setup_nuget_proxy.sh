#!/usr/bin/env bash
set -euo pipefail

SLN="${1:-WikiWikiWorld.slnx}"
NUGET_DIR="${HOME}/.nuget/NuGet"
NUGET_CFG="${NUGET_DIR}/NuGet.Config"

# Prefer HTTPS_PROXY, else HTTP_PROXY
PROXY_URL="${HTTPS_PROXY:-${HTTP_PROXY:-}}"

if [ -z "${PROXY_URL}" ]; then
  echo "[nuget-proxy] ERROR: No HTTPS_PROXY/HTTP_PROXY in env."
  exit 1
fi

# PROXY_URL like: http://username:password@host:port
# Strip scheme
NO_SCHEME="${PROXY_URL#http://}"
NO_SCHEME="${NO_SCHEME#https://}"

# Split credentials and host:port
CREDS="${NO_SCHEME%@*}"
HOSTPORT="${NO_SCHEME#*@}"

# Username and password (password is the JWT)
USER="${CREDS%%:*}"
PASS="${CREDS#*:}"

HOST="${HOSTPORT%%:*}"
PORT="${HOSTPORT##*:}"

if [[ -z "${USER}" || -z "${PASS}" || -z "${HOST}" || -z "${PORT}" ]]; then
  echo "[nuget-proxy] ERROR: Could not parse proxy URL (${PROXY_URL})."
  exit 1
fi

mkdir -p "${NUGET_DIR}"

cat > "${NUGET_CFG}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>

  <!-- Tell NuGet/msbuild to use this proxy -->
  <proxy proxy="http://${HOST}:${PORT}" username="${USER}" password="${PASS}" />

  <!-- Optional quality-of-life -->
  <config>
    <add key="noProxy" value="localhost;127.0.0.1" />
  </config>
</configuration>
EOF

echo "[nuget-proxy] Wrote ${NUGET_CFG}"
# Avoid the env-based proxy confusing dotnet once we have explicit config:
unset http_proxy HTTPS_PROXY HTTP_PROXY https_proxy || true

# Ensure dotnet on PATH if you installed to a custom dir
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then source "${CLAUDE_ENV_FILE}" 2>/dev/null || true; fi

# Clear caches then quick connectivity check
dotnet nuget locals http-cache --clear || true
dotnet nuget list source --configfile "${NUGET_CFG}"

# Try a trivial query to nuget index (will happen during restore anyway)
echo "[nuget-proxy] Proxy configured. Ready to restore/build ${SLN}."
