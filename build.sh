#!/usr/bin/env bash
# Build, test and package Puppet Bio-Regeneration Fix.
# RIMWORLD_LIBS must point at a directory OUTSIDE this repo containing the installed game's
# Assembly-CSharp.dll, UnityStubs.dll, 0Harmony.dll (brrainz.harmony 1.7) and, for tests,
# EnhancedBioregeneration.dll plus data/*.xml copied from the installed game/mods.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$PWD"
VERSION=1.0.0
export RIMWORLD_LIBS="${RIMWORLD_LIBS:-/root/rimworld_libs/1.7.5005-rev1082}"

rm -rf 1.7/Assemblies/*.dll Source/PuppetBioRegenFix/obj Tests/PuppetBioRegenFix.Tests/bin Tests/PuppetBioRegenFix.Tests/obj dist
dotnet build Source/PuppetBioRegenFix/PuppetBioRegenFix.csproj -c Release -nologo
dotnet build Tests/PuppetBioRegenFix.Tests/PuppetBioRegenFix.Tests.csproj -c Release -nologo
dotnet Tests/PuppetBioRegenFix.Tests/bin/Release/net10.0/PuppetBioRegenFix.Tests.dll

# Package: only our files. Fails if any proprietary assembly sneaks in.
STAGE="dist/PuppetBioRegenFix"
mkdir -p "$STAGE/1.7/Assemblies"
cp -r About LoadFolders.xml README.md LICENSE "$STAGE/"
cp 1.7/Assemblies/PuppetBioRegenFix.dll "$STAGE/1.7/Assemblies/"
find "$STAGE" -name '*.dll' ! -name 'PuppetBioRegenFix.dll' -print -exec false {} + || { echo "unexpected DLL in package"; exit 1; }
ZIP="dist/PuppetBioRegenFix-$VERSION.zip"
(cd dist && zip -qr "$(basename "$ZIP")" PuppetBioRegenFix)
sha256sum "$ZIP" | tee "$ZIP.sha256"
unzip -l "$ZIP"
