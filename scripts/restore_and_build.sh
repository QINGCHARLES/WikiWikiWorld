#!/usr/bin/env bash
set -euo pipefail

SLN="${1:-WikiWikiWorld.slnx}"
NUGET_CFG="${HOME}/.nuget/NuGet/NuGet.Config"

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then source "${CLAUDE_ENV_FILE}" 2>/dev/null || true; fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[build] dotnet not on PATH. Run scripts/install_dotnet9.sh first."
  exit 1
fi

if [ ! -f "${NUGET_CFG}" ]; then
  echo "[build] NuGet.Config not found at ${NUGET_CFG}. Run scripts/setup_nuget_proxy.sh first."
  exit 1
fi

echo "[build] Restoring ${SLN}..."
dotnet restore "${SLN}" --configfile "${NUGET_CFG}" --verbosity minimal

echo "[build] Building ${SLN} (Release)..."
dotnet build "${SLN}" -c Release --no-restore
