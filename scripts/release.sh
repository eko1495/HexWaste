#!/usr/bin/env bash
# Builds distributable folder artifacts for a Hexwaste release.
#
# Folder publish (not single-file) per MonoGame guidance; no trimming —
# System.Text.Json reflection in the save system breaks under trimming.
# Linux ships as tar.gz to preserve the executable bit.
set -euo pipefail

cd "$(dirname "$0")/.."
VERSION="${1:?usage: scripts/release.sh <version, e.g. 0.5.0>}"
OUT="artifacts/v$VERSION"
PROJECT="src/Hexwaste.Viewer/Hexwaste.Viewer.csproj"

rm -rf "$OUT"
mkdir -p "$OUT"

for RID in linux-x64 win-x64; do
    echo "== publishing $RID =="
    dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained \
        -p:PublishSingleFile=false -p:PublishTrimmed=false \
        -o "$OUT/hexwaste-$VERSION-$RID"
    cp LICENSE.md NOTICE.md README.md CHANGELOG.md "$OUT/hexwaste-$VERSION-$RID/"
    rm -f "$OUT/hexwaste-$VERSION-$RID"/*.pdb
done

echo "== archiving =="
tar -C "$OUT" -czf "$OUT/hexwaste-$VERSION-linux-x64.tar.gz" "hexwaste-$VERSION-linux-x64"
(cd "$OUT" && zip -qr "hexwaste-$VERSION-win-x64.zip" "hexwaste-$VERSION-win-x64")

echo
echo "artifacts:"
ls -lh "$OUT"/*.tar.gz "$OUT"/*.zip
