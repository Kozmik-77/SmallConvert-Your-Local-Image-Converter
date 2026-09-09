#!/usr/bin/env bash
# Builds SmallConvert for every supported platform into the dist/ folder.
# Requires the .NET 8 SDK. Run from the project folder.
set -euo pipefail

VERSION=2.0.0
OUT=dist
COMMON=(-c Release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none)

rm -rf "$OUT"

echo "=== Windows x64, self-contained (no .NET needed, ~100 MB)"
dotnet publish "${COMMON[@]}" -r win-x64 --self-contained true  -o "$OUT/win-x64-standalone"

echo "=== Windows x64, framework-dependent (needs .NET 8 Desktop Runtime, ~5 MB)"
dotnet publish "${COMMON[@]}" -r win-x64 --self-contained false -o "$OUT/win-x64-dotnet"

echo "=== Linux x64"
dotnet publish "${COMMON[@]}" -r linux-x64 --self-contained true -o "$OUT/linux-x64"

echo "=== macOS Intel"
dotnet publish "${COMMON[@]}" -r osx-x64   --self-contained true -o "$OUT/macos-x64"

echo "=== macOS Apple Silicon"
dotnet publish "${COMMON[@]}" -r osx-arm64 --self-contained true -o "$OUT/macos-arm64"

chmod +x "$OUT"/linux-x64/SmallConvert "$OUT"/macos-*/SmallConvert 2>/dev/null || true

echo
echo "Done. Outputs are in $OUT/"
echo "Suggested release file names:"
echo "  SmallConvert-$VERSION-windows-x64.exe          (from win-x64-standalone)"
echo "  SmallConvert-$VERSION-windows-x64-dotnet.exe   (from win-x64-dotnet)"
echo "  SmallConvert-$VERSION-linux-x64                (from linux-x64)"
echo "  SmallConvert-$VERSION-macos-x64                (from macos-x64)"
echo "  SmallConvert-$VERSION-macos-arm64              (from macos-arm64)"
