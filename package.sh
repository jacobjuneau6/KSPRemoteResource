#!/usr/bin/env bash
# package.sh — Create a release zip for SpaceDock / GitHub Releases / CKAN
# Usage: ./package.sh [version]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="${1:-1.0.0}"
MODNAME="RemoteResourceTransfer"
DISTDIR="$SCRIPT_DIR/dist"
PACKAGE="$MODNAME-$VERSION.zip"

echo "Packaging $MODNAME v$VERSION..."

# Clean
rm -rf "$DISTDIR"
mkdir -p "$DISTDIR/$MODNAME"

# Copy GameData contents
cp -r "$SCRIPT_DIR/GameData/$MODNAME" "$DISTDIR/$MODNAME/"

# Strip build artefacts
find "$DISTDIR" -name "*.pdb" -delete 2>/dev/null || true
find "$DISTDIR" -name "*.mdb" -delete 2>/dev/null || true

# Create the zip (KSP mod convention: zip contains GameData/RemoteResourceTransfer/...)
cd "$DISTDIR"
zip -r "$SCRIPT_DIR/$PACKAGE" "$MODNAME/"
cd "$SCRIPT_DIR"

echo ""
echo "✓ Package created: $SCRIPT_DIR/$PACKAGE"
ls -lh "$SCRIPT_DIR/$PACKAGE"
rm -rf "$DISTDIR"
