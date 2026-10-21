#!/usr/bin/env bash
# Builds the browser playtest into $1 (default: Release/playtest).
# The artifact host reserves "_" paths, so _framework is renamed to framework.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/Release/playtest}"
dotnet publish "$ROOT/Playtest/Playtest.csproj" -c Release -o "$ROOT/Playtest/bin/publish" >/dev/null
rm -rf "$OUT" && mkdir -p "$OUT"
cp -r "$ROOT/Playtest/bin/publish/wwwroot/." "$OUT/"
mv "$OUT/_framework" "$OUT/framework"
find "$OUT" \( -name "*.br" -o -name "*.gz" -o -name "*.pdb" \) -delete
rm -f "$OUT/web.config"
# Game art and audio are shared with the Unity project.
[ -d "$ROOT/Assets/_Project/Resources/Art" ] && cp -r "$ROOT/Assets/_Project/Resources/Art" "$OUT/art"
[ -d "$ROOT/Assets/_Project/Resources/Audio" ] && mkdir -p "$OUT/audio" && cp -r "$ROOT/Assets/_Project/Resources/Audio/Music" "$OUT/audio/music" && cp -r "$ROOT/Assets/_Project/Resources/Audio/Sfx" "$OUT/audio/sfx"
find "$OUT" -name "*.meta" -delete
echo "Playtest published to $OUT ($(find "$OUT" -type f | wc -l) files, $(du -sh "$OUT" | cut -f1))"
