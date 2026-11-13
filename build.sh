#!/usr/bin/env bash
# Build, test and package Rimworld Biosculpter Compat.
# RIMWORLD_LIBS must point at a directory OUTSIDE this repo containing the installed game's
# Assembly-CSharp.dll, UnityStubs.dll, 0Harmony.dll (brrainz.harmony 1.7) and, for tests,
# EnhancedBioregeneration.dll plus data/*.xml copied from the installed game/mods.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$PWD"
VERSION=1.2.1
MOD=RimworldBiosculpterCompat
export RIMWORLD_LIBS="${RIMWORLD_LIBS:-/root/rimworld_libs/1.7.5005-rev1082}"

# Test fixtures documented in README.md must exist outside the repo before building.
for f in \
  VPEPuppeteer_1.6_HediffDefs_Hediffs.xml \
  Ideology_ThingDefs_Buildings_Buildings_Ideo.xml \
  EnhancedBioregeneration_1.7_Patches_Buildings_Ideo.xml \
  Biotech_HediffDefs_Hediffs_Mechanitor.xml \
  LTSII_1.7_Mods_Biotech_HediffDefs_Hediffs_Mechanitor.xml \
  LTSII_1.7_HediffDefs_Hediffs_ShoulderWeaponry.xml \
  VRE_Sanguophage_1.6_HediffDefs_Hediffs.xml \
  VRE_Sanguophage_1.6_GeneDefs_GeneDefs_Hands.xml \
  VQEA_1.6_Defs_HediffDefs_Hediffs_Genes.xml \
  VQEA_1.6_Defs_GeneDefs_GeneDefs_Archite.xml \
  VRE_Fungoid_1.7_Defs_HediffDefs_Hediffs_Attacks.xml \
  VRE_Fungoid_1.7_Defs_GeneDefs_GeneDefs_Archite.xml \
  VRESaurids_1.6_Defs_HediffDefs_HediffDef_SauridClaws.xml \
  VRESaurids_1.6_Defs_GeneDefs_GeneDefs_Endogenes.xml \
  Odyssey_HediffDefs_Hediffs_BodyParts_Prosthetic.xml \
  Odyssey_ThingDefs_Items_Items_Exotic.xml \
  MAG_1.6_Compat_Ideology_Defs_Ideology_Crafting.xml \
  WCE2_1.6_Defs_WCE2_Hediffs_WCE2_Local_Growths.xml \
  WCE2_1.6_Defs_WCE2_Hediffs_WCE2_Local_Implants.xml \
  WCE2_1.6_Defs_WCE2_Recipes_WCE2_Recipes_Implants_Torso.xml \
  WCE2_1.6_Defs_WCE2_Recipes_WCE2_Recipes_LowTech.xml \
  Core_Defs_HediffDefs_Hediffs_Local_Misc.xml \
  Core_Defs_HediffGiverSetDefs_HediffGiverSets.xml; do
  [[ -f "$RIMWORLD_LIBS/data/$f" ]] || { echo "missing test fixture: $RIMWORLD_LIBS/data/$f (see README.md)"; exit 1; }
done

rm -rf 1.7/Assemblies/*.dll Source/$MOD/obj Tests/$MOD.Tests/bin Tests/$MOD.Tests/obj dist
dotnet build Source/$MOD/$MOD.csproj -c Release -nologo
dotnet build Tests/$MOD.Tests/$MOD.Tests.csproj -c Release -nologo
dotnet Tests/$MOD.Tests/bin/Release/net10.0/$MOD.Tests.dll

# Package: only our files. Fails if any proprietary assembly sneaks in.
STAGE="dist/$MOD"
mkdir -p "$STAGE/1.7/Assemblies"
cp -r About LoadFolders.xml README.md LICENSE "$STAGE/"
cp 1.7/Assemblies/$MOD.dll "$STAGE/1.7/Assemblies/"
find "$STAGE" -name '*.dll' ! -name "$MOD.dll" -print -exec false {} + || { echo "unexpected DLL in package"; exit 1; }
ZIP="dist/$MOD-$VERSION.zip"
(cd dist && zip -qr "$(basename "$ZIP")" $MOD)
sha256sum "$ZIP" | tee "$ZIP.sha256"
unzip -l "$ZIP"