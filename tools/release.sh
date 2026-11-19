#!/usr/bin/env bash
# Builds KavrosSlayerHelper.exe and packs dist/KavrosSlayerHelper-win-Portable.zip
# (current/KavrosSlayerHelper.exe, the layout Companion's linked apps expect) with its SHA-256.
# Local only: it does not tag, push or publish anything.
#   tools/release.sh
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf out dist
dotnet build KavrosSlayerHelper.csproj -c Release -o out
mkdir -p dist
python3 - <<'PY'
import zipfile
with zipfile.ZipFile("dist/KavrosSlayerHelper-win-Portable.zip", "w", zipfile.ZIP_DEFLATED) as z:
    z.write("out/KavrosSlayerHelper.exe", "current/KavrosSlayerHelper.exe")
PY
(cd dist && sha256sum KavrosSlayerHelper-win-Portable.zip > KavrosSlayerHelper-win-Portable.zip.sha256.txt)
sha256sum dist/KavrosSlayerHelper-win-Portable.zip out/KavrosSlayerHelper.exe
