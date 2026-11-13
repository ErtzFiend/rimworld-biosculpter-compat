# Rimworld Biosculpter Compat

A RimWorld 1.7 Harmony mod. It stops a biosculpter **bioregeneration** cycle from "healing" away
implants, gene hediffs and psycast hediffs that are part of a pawn's build rather than injuries.

Protected (exact `defName`, nothing else; 24 in total):

| defName | label | where | source mod |
|---|---|---|---|
| `VPEP_Puppet` | Puppet | brain | Vanilla Psycasts Expanded – Puppeteer |
| `VPEP_Puppeteer` | Puppeteer | brain | Vanilla Psycasts Expanded – Puppeteer |
| `ControlSublinkImplant` | control sublink | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `RemoteRepairerImplant` | remote repairer | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `MechFormfeederImplant` | mech gestation processor | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `RemoteShielderImplant` | remote shielder | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `RepairProbeImplant` | repair probe | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `WarprogrammerInterfaceImplant` | warprogrammer interface | brain | Integrated Implants (`lts.I`) |
| `RemoteDominatorImplant` | remote dominator | brain | Integrated Implants (`lts.I`) |
| `RemoteResurrectorImplant` | remote resurrector | brain | Integrated Implants (`lts.I`) |
| `SignalBoosterImplant` | signal booster | brain | Integrated Implants (`lts.I`) |
| `MechwombImplant` | mechwomb | torso | Integrated Implants (`lts.I`) |
| `LTS_ShoulderTurretActive` | extended shoulder turret | torso | Integrated Implants (`lts.I`) |
| `LTS_ShoulderBeamTurretActive` | extended shoulder beam turret | torso | Integrated Implants (`lts.I`) |
| `LTS_ShoulderChargeTurretActive` | extended shoulder charge turret | torso | Integrated Implants (`lts.I`) |
| `VRE_SharpTalons` | sharp talons | each hand | Vanilla Races Expanded – Sanguophage |
| `VQEA_PlasteelClaws` | plasteel claw | each hand | Vanilla Quests Expanded – Ancients |
| `VRE_InfectorHands` | infector hand | each hand | Vanilla Races Expanded – Fungoid |
| `VRE_InfectorMouth` | infector mouth | jaw | Vanilla Races Expanded – Fungoid |
| `VRESaurids_SauridClaws` | saurid claws | each hand | Vanilla Races Expanded – Saurid |
| `SentienceCatalyst` | sentience catalyst | brain | Odyssey |
| `MAG_ArchonexusCore_One` | Archonexus key I | torso | More Archotech Garbage |
| `MAG_ArchonexusCore_Two` | Archonexus key II | torso | More Archotech Garbage |
| `MAG_ArchonexusCore_Three` | Archonexus key III | torso | More Archotech Garbage |

Those defNames were read from the live, enabled mod set — never guessed from a label. Several
unrelated defs share the same label text (`ThingDef` names on the install items), so the table above
is `defName` exact and case-sensitive. The part column is the part the hediff is actually attached to,
taken from the installing `ThingDef`'s `CompProperties_UseEffectInstallImplant` (brain implants,
`MAG_ArchonexusCore_*` on the torso, `SentienceCatalyst` via `TargetEffectInstallImplantInOtherPawn`)
or the gene's VEF `GeneExtension.hediffsToBodyParts` (hands/jaw). The two newer LTS shoulder-turret
active hediffs carry no `bodyPart` in their own XML; their install recipes are
`appliedOnFixedBodyParts Torso`, so that is the runtime mount. If LTS ever adds them with no part at
all they are already outside the part-restore gate, and the protection is a harmless no-op.

## The problem

`CompBiosculpterPod_HealingCycle.CycleCompleted` cures any hediff whose body part is listed in the
cycle's `bodyPartsToRestore`, using `WillHeal` → `HealthUtility.Cure(Hediff)` →
`Pawn_HealthTracker.RemoveHediff`. Vanilla's list is only Eye, Ear, Nose, Finger, Toe and Tongue, so
brain/torso/hand/jaw hediffs normally survive. **BattIePatch: Enhanced Bioregeneration** adds `Brain`,
`Torso`, `Hand`, `Jaw`, `Shoulder` and more through an XML patch. It also replaces `CycleCompleted`
with its own `HealingCycle_Utils.CycleCompleted` when any of its scar or regeneration settings is on.
That version also calls `HealthUtility.Cure`.

All 24 hediffs use the default `everCurableByItem=true`, none sets `countsAsAddedPartOrImplant`, and
none is `chronic`; `keepOnBodyPartRestoration` (set only on the leveled mechanitor implants) is
ignored by the cycle. So once their part is restorable they are cured like injuries. Results:
mechanitor implants and the shoulder turrets vanish, the sanguophage loses both talons, the fungoid
loses its infector hands/mouth, the saurid and the ancients lose their claws, the archonexus keys and
sentience catalyst are stripped, and the puppet link is removed (a removed `VPEP_Puppet` gives abasia
and a psychic coma).

The old 1.0.0 mod also left the pod tooltip listing the puppet hediff under "These conditions will
be healed" while it actually survived. That mismatch is fixed in 1.1.0 and remains fixed here.

## What the mod does

* A prefix (priority First) and a finalizer on `CycleCompleted` open a scope for the pod occupant,
  only when `Regenerate` is true (a bioregeneration cycle).
* A postfix on the vanilla private `WillHeal` and, when Enhanced Bioregeneration is loaded, on
  `HealingCycle_Utils.WillHeal` returns false for a preserved hediff of the pawn in scope. This
  covers both the cycle and the "will heal" tooltip text on both description implementations.
* A prefix on `HealthUtility.Cure(Hediff)` is a backstop: it skips the cure only when the scope is
  open, the hediff belongs to the occupant, and its `defName` is exactly one of the 24.

Everything else stays the same: wounds, scars, missing parts, blood loss, conditions, other brain
hediffs (for example `VPEP_BrainLeech`), medic cycles, and any removal outside the cycle. Implants
are not made incurable globally and nothing is suppressed except the 24 defNames above.

## Compatibility

* Built and tested only against **RimWorld 1.7.5005 rev1082** (Assembly-CSharp 1.7.9810.32479) with
  Harmony 3.0 (brrainz.harmony).
* Required: **Harmony** only. Every protection is inert when its mod is absent.
* Optional, load after: Biotech, Ideology, Odyssey, Vanilla Psycasts Expanded + Puppeteer, Vanilla
  Races Expanded – Sanguophage/Fungoid/Saurid, Vanilla Quests Expanded – Ancients, Integrated Implants
  (`lts.i`), More Archotech Garbage (`zal.morearchotechgarbage`), Vanilla Factions Expanded – Core
  (required by Integrated Implants' shoulder weaponry) and BattIePatch: Enhanced Bioregeneration (also
  improves the tooltip). `loadAfter` lists them all; none is a hard dependency.
* No save-game data. You can add or remove the mod at any time.

## Installing 1.2.0

Upgrading from 1.1.0 needs no migration: same package id, same Harmony id, no defs or save data of its
own. Close RimWorld, replace the old `Mods/RimworldBiosculpterCompat` folder with the one from
`RimworldBiosculpterCompat-1.2.0.zip`, then restart. Saves load unchanged; already-lost implants are
not restored.

Upgrading from **Puppet Bio-Regeneration Fix 1.0.0** (different package and Harmony id):

1. Close RimWorld.
2. In `ModsConfig.xml` disable **Puppet Bio-Regeneration Fix** (or unsubscribe/remove it). If you
   leave both enabled, 1.0.0's Harmony ID (`ErtzFiend.PuppetBioRegenerationFix`) and 1.2.0's
   (`ErtzFiend.RimworldBiosculpterCompat`) both patch the same methods; 1.0.0's narrower rules still
   work, but the tooltip fix and the new protections are only complete with 1.0.0 removed.
3. Delete the old `Mods/PuppetBioRegenFix` folder. It ships `PuppetBioRegenFix.dll`; 1.2.0 ships
   `RimworldBiosculpterCompat.dll`, and two mods must never be left side by side with the old one.
4. Extract `RimworldBiosculpterCompat` from `RimworldBiosculpterCompat-1.2.0.zip` into RimWorld's
   `Mods` directory.
5. Enable **Rimworld Biosculpter Compat** after Harmony and after the optional mods you use, in the
   order listed in `loadAfter`, then restart RimWorld.
6. Check the log for `[Rimworld Biosculpter Compat 1.2.0] Harmony ErtzFiend.RimworldBiosculpterCompat:
   protecting ...` with all 24 defNames.

This prevents future cycle-related removal. Version 1.2.0 passed 690 executable regression assertions
against the installed game assemblies, covering both vanilla and Enhanced Bioregeneration completion
and description paths, plus the Enhanced-Bioregeneration-absent path. A real in-game cycle has
**not** been run; engine plumbing is stubbed as described below.

## Build and test

```
RIMWORLD_LIBS=/path/outside/repo ./build.sh
```

`RIMWORLD_LIBS` must contain your installed `Assembly-CSharp.dll`, `UnityStubs.dll` and
`0Harmony.dll`. For the tests it also needs `EnhancedBioregeneration.dll` and `data/` copies of the
installed XML (none of these proprietary files are in this repository):

| fixture | installed source |
|---|---|
| `VPEPuppeteer_1.6_HediffDefs_Hediffs.xml` | VPE Puppeteer `Defs/HediffDefs/Hediffs.xml` |
| `Ideology_ThingDefs_Buildings_Buildings_Ideo.xml` | Ideology `Defs/ThingDefs_Buildings/Buildings_Ideo.xml` |
| `EnhancedBioregeneration_1.7_Patches_Buildings_Ideo.xml` | Enhanced Bioregeneration `Patches/Buildings_Ideo.xml` |
| `Biotech_HediffDefs_Hediffs_Mechanitor.xml` | Biotech `Defs/HediffDefs/Hediffs_Mechanitor.xml` |
| `LTSII_1.7_Mods_Biotech_HediffDefs_Hediffs_Mechanitor.xml` | lts.I `1.7/Mods/Biotech/Defs/HediffDefs/Hediffs_Mechanitor.xml` |
| `LTSII_1.7_HediffDefs_Hediffs_ShoulderWeaponry.xml` | lts.I `1.7/Defs/HediffDefs/Hediffs_ShoulderWeaponry.xml` |
| `VRE_Sanguophage_1.6_HediffDefs_Hediffs.xml` | VRE Sanguophage `Defs/HediffDefs/Hediffs.xml` |
| `VRE_Sanguophage_1.6_GeneDefs_GeneDefs_Hands.xml` | VRE Sanguophage `Defs/GeneDefs/GeneDefs_Hands.xml` |
| `VQEA_1.6_Defs_HediffDefs_Hediffs_Genes.xml` | VQEA Ancients `1.6/Defs/HediffDefs/Hediffs_Genes.xml` |
| `VQEA_1.6_Defs_GeneDefs_GeneDefs_Archite.xml` | VQEA Ancients `1.6/Defs/GeneDefs/GeneDefs_Archite.xml` |
| `VRE_Fungoid_1.7_Defs_HediffDefs_Hediffs_Attacks.xml` | VRE Fungoid `1.7/Defs/HediffDefs/Hediffs_Attacks.xml` |
| `VRE_Fungoid_1.7_Defs_GeneDefs_GeneDefs_Archite.xml` | VRE Fungoid `1.7/Defs/GeneDefs/GeneDefs_Archite.xml` |
| `VRESaurids_1.6_Defs_HediffDefs_HediffDef_SauridClaws.xml` | VRE Saurid `1.6/Defs/HediffDefs/HediffDef_SauridClaws.xml` |
| `VRESaurids_1.6_Defs_GeneDefs_GeneDefs_Endogenes.xml` | VRE Saurid `1.6/Defs/GeneDefs/GeneDefs_Endogenes.xml` |
| `Odyssey_HediffDefs_Hediffs_BodyParts_Prosthetic.xml` | Odyssey `Defs/HediffDefs/Hediffs_BodyParts_Prosthetic.xml` |
| `Odyssey_ThingDefs_Items_Items_Exotic.xml` | Odyssey `Defs/ThingDefs_Items/Items_Exotic.xml` |
| `MAG_1.6_Compat_Ideology_Defs_Ideology_Crafting.xml` | MAG `1.6/Compat/Ideology/Defs/Ideology_Crafting.xml` |

The test runner (`Tests/RimworldBiosculpterCompat.Tests`) runs the real vanilla `CycleCompleted` and
`GetHealingDescriptionForPawn`, and the real Enhanced Bioregeneration patches, with the mod's Harmony
patches registered through its startup class. It checks that:

* the bug reproduces for all 24 defNames without the fix (both cycle implementations, both tooltips)
* the protected set equals exactly the 24 approved `defName`s — nothing missing, nothing unapproved
* all 24 survive a bioregeneration cycle, as the same instance on the part their install/gene XML says
* the tooltip no longer lists them, while still listing a real injury and `VPEP_BrainLeech`
* injuries, other hediffs, other pawns, medic cycles and out-of-cycle removal are unaffected
* near-miss defNames (`VPEP_PuppetX`, `vpep_puppet`, `MAG_ArchonexusCore_`, `RemoteShielder`, ...) are not matched
* the protection works when Enhanced Bioregeneration's own patches are removed, and the optional patch
  helper is a safe no-op when that mod's type is absent
* the scope is cleared after an exception and after every description call
* the old behaviour returns after `UnpatchAll`

Only engine plumbing that needs a running game is stubbed: render caches, downed/death checks,
messages, translation and logging.

## License

MIT