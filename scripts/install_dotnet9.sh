#!/usr/bin/env bash
set -euo pipefail

# Only run in the remote (web) VM
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Check if dotnet is already installed
if command -v dotnet &> /dev/null; then
  echo ".NET is already installed:"
  dotnet --info
  exit 0
fi

# Download and run Microsoft's official .NET installer
INSTALL_DIR="/usr/local/dotnet"
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh

# Install .NET 9 SDK using the channel option
/tmp/dotnet-install.sh --channel 9.0 --install-dir "$INSTALL_DIR"

# Add to system PATH permanently
echo "export PATH=\"$INSTALL_DIR:\$PATH\"" | sudo tee /etc/profile.d/dotnet.sh
sudo chmod +x /etc/profile.d/dotnet.sh

# Add to current session PATH
export PATH="$INSTALL_DIR:$PATH"

# Prove it's installed
dotnet --info

# (Optional) restore/build your solution
# dotnet restore
# dotnet build --no-restore
