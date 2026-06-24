#!/usr/bin/env bash
# build.sh — Build the RemoteResourceTransfer KSP mod
# Usage: ./build.sh [Debug|Release]
set -euo pipefail

CONFIG="${1:-Release}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/Source/RemoteResourceTransfer.csproj"
OUTDIR="$SCRIPT_DIR/GameData/RemoteResourceTransfer/Plugins"

# ---- Locate KSP ----
find_ksp() {
    # Common install paths
    local PATHS=(
        "$HOME/.steam/steam/steamapps/common/Kerbal Space Program"
        "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
        "/mnt/c/Program Files (x86)/Steam/steamapps/common/Kerbal Space Program"  # WSL
        "${KSP_DIR:-}"  # env var override
    )
    for p in "${PATHS[@]}"; do
        [ -n "$p" ] && [ -f "$p/KSP_Data/Managed/Assembly-CSharp.dll" ] && { echo "$p"; return 0; }
    done
    return 1
}

KSP="$(find_ksp)" || {
    echo "ERROR: KSP not found. Set KSP_DIR to your KSP install path."
    echo "  export KSP_DIR=\"/path/to/Kerbal Space Program\""
    exit 1
}
echo "KSP found at: $KSP"

# ---- Build ----
echo "Building $CONFIG..."

# Prefer dotnet if available, fall back to Mono msbuild
if command -v dotnet &>/dev/null; then
    dotnet build "$PROJ" -c "$CONFIG" /p:KSP_DIR="$KSP"
elif command -v msbuild &>/dev/null; then
    msbuild "$PROJ" /p:Configuration="$CONFIG" /p:KSP_DIR="$KSP"
elif command -v xbuild &>/dev/null; then
    xbuild "$PROJ" /p:Configuration="$CONFIG" /p:KSP_DIR="$KSP"
else
    echo "ERROR: No build tool found. Install 'dotnet', 'msbuild', or 'mono-msbuild'."
    exit 1
fi

echo ""
echo "✓ Build complete — DLL at: $OUTDIR/RemoteResourceTransfer.dll"
ls -lh "$OUTDIR"/*.dll 2>/dev/null || echo "(DLL not found — check build errors above)"
