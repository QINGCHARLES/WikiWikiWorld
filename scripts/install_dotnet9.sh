#!/usr/bin/env bash
set -euo pipefail

# Only run in the remote (web) VM
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Ensure repo helpers exist
sudo apt-get update -y
sudo apt-get install -y software-properties-common

# Add Canonical's .NET backports PPA (safe on 22.04/24.04) and refresh
sudo add-apt-repository -y ppa:dotnet/backports
sudo apt-get update -y

# Install .NET 9 SDK
sudo apt-get install -y dotnet-sdk-9.0

# Prove it's installed
dotnet --info

# (Optional) restore/build your solution
# dotnet restore
# dotnet build --no-restore
