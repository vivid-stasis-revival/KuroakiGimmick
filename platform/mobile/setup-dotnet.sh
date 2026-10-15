#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
sdk="$root/dist/mobile-dotnet"
mkdir -p "$root/dist/mobile-deps"
if [[ ! -x "$sdk/dotnet" ]]; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$root/dist/mobile-deps/dotnet-install.sh"
  bash "$root/dist/mobile-deps/dotnet-install.sh" --version 10.0.101 --install-dir "$sdk" --no-path
fi
# Matches this workstation's Xcode 26.2; installation stays inside dist/.
"$sdk/dotnet" workload install android ios --version 10.0.101.1
