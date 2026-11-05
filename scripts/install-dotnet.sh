#!/usr/bin/env bash
# Installs the .NET 10 SDK into ~/.dotnet for environments without a system package (e.g. cloud agents).
set -euo pipefail
INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$INSTALL_DIR"
echo "Add to PATH: export PATH=$INSTALL_DIR:\$PATH"
