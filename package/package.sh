#!/usr/bin/env bash
# Builds the tool with the .NET SDK and zips the single DLL XrmToolBox needs.
# Usage:  ./package/package.sh            (build + zip)
#         ./package/package.sh --no-build (zip only)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/Oliver4.IconLibrary/Oliver4.IconLibrary.csproj"
BIN="$ROOT/src/Oliver4.IconLibrary/bin/Release/net48"
OUT="$ROOT/package/out"

if [[ "${1:-}" != "--no-build" ]]; then
  echo "Building (Release)..."
  dotnet build "$PROJ" -c Release
fi

STAGE="$OUT/stage"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp "$BIN/Oliver4.IconLibrary.dll" "$STAGE/"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$STAGE/"
cp "$ROOT/LICENSE" "$STAGE/LICENSE.txt"
cp "$ROOT/package/INSTALL.txt" "$STAGE/"

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -1)"
ZIP="$OUT/Oliver4.IconLibrary-$VERSION.zip"
rm -f "$ZIP"
# -X drops macOS extended attributes; the exclusions keep Finder files out of the download.
(cd "$STAGE" && zip -qrX "$ZIP" . -x '.DS_Store' '*/.DS_Store' '._*' '__MACOSX/*')
echo "Done: $ZIP"
unzip -l "$ZIP"
ls -la "$STAGE/Oliver4.IconLibrary.dll"
