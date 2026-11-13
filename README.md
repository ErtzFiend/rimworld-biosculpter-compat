# Rimworld Biosculpter Compat

A RimWorld 1.7 Harmony mod. It stops a biosculpter **bioregeneration** cycle from "healing" away
implants, gene hediffs and psycast hediffs that are part of a pawn's build rather than injuries.

Protected (exact `defName`, nothing else):

| defName | label | where | source mod |
|---|---|---|---|
| `VPEP_Puppet` | Puppet | brain | Vanilla Psycasts Expanded – Puppeteer |
| `VPEP_Puppeteer` | Puppeteer | brain | Vanilla Psycasts Expanded – Puppeteer |
| `ControlSublinkImplant` | control sublink | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `RemoteRepairerImplant` | remote repairer | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `MechFormfeederImplant` | mech gestation processor | brain | Biotech (`Hediffs_Mechanitor.xml`) |
| `WarprogrammerInterfaceImplant` | warprogrammer interface | brain | Integrated Implants (`lts.I`) |
| `LTS_ShoulderTurretActive` | extended shoulder turret | torso | Integrated Implants (`lts.I`) |
| `VRE_SharpTalons` | sharp talons | each hand | Vanilla Races Expanded – Sanguophage |

Those defNames were read from the live, enabled mod set — never guessed from a label. Several
unrelated defs share the same label text (`ThingDef` names on the install items), so the table above
is `defName` exact and case-sensitive.

## The problem

`CompBiosculpterPod_HealingCycle.CycleCompleted` cures any hediff whose body part is listed in the
cycle's `bodyPartsToRestore`, using `WillHeal` → `HealthUtility.Cure(Hediff)` →
`Pawn_HealthTracker.RemoveHediff`. Vanilla's list is only Eye, Ear, Nose, Finger, Toe and Tongue, so
brain/torso/hand hediffs normally survive. **BattIePatch: Enhanced Bioregeneration** adds `Brain`,
`Torso` and `Hand`, among others, through an XML patch. It also replaces `CycleCompleted` with its own
`HealingCycle_Utils.CycleCompleted` when any of its scar or regeneration settings is on. That version
also calls `HealthUtility.Cure`.

All eight hediffs use the default `everCurableByItem=true`, none sets `countsAsAddedPartOrImplant`,
and `keepOnBodyPartRestoration` is ignored by the cycle — so once their part is restorable they are
cured like injuries. Results: mechanitor implants and the shoulder turret vanish, the sanguophage
loses both talons, and the puppet link is stripped (a removed `VPEP_Puppet` gives abasia and a
psychic coma).

The old 1.0.0 mod also left the pod tooltip listing the puppet hediff under "These conditions will
be healed" while it actually survived. That mismatch is fixed in 1.1.0.

## What the mod does

* A prefix (priority First) and a finalizer on `CycleCompleted` open a scope for the pod occupant,
  only when `Regenerate` is true (a bioregeneration cycle).
* A postfix on the vanilla private `WillHeal` and, when Enhanced Bioregeneration is loaded, on
  `HealingCycle_Utils.WillHeal` returns false for a preserved hediff of the pawn in scope. This
  covers both the cycle and the "will heal" tooltip text on both description implementations.
* A prefix on `HealthUtility.Cure(Hediff)` is a backstop: it skips the cure only when the scope is
  open, the hediff belongs to the occupant, and its `defName` is exactly one of the eight.

Everything else stays the same: wounds, scars, missing parts, blood loss, conditions, other brain
hediffs (for example `VPEP_BrainLeech`), medic cycles, and any removal outside the cycle. Implants
are not made incurable globally and nothing is suppressed except the eight defNames above.

## Compatibility

* Built and tested only against **RimWorld 1.7.5005 rev1082** (Assembly-CSharp 1.7.9810.32479) with
  Harmony 3.0 (brrainz.harmony).
* Required: **Harmony** only. Every protection is inert when its mod is absent.
* Optional, load after: Biotech, Ideology, Vanilla Psycasts Expanded + Puppeteer, Vanilla Races
  Expanded – Sanguophage, Integrated Implants (`lts.i`), Vanilla Factions Expanded – Core (required by
  Integrated Implants' shoulder weaponry) and BattIePatch: Enhanced Bioregeneration (also improves the
  tooltip). `loadAfter` lists them all; none is a hard dependency.
* No save-game data. You can add or remove the mod at any time.

## Installing 1.1.0 (migration from Puppet Bio-Regeneration Fix 1.0.0)

1. Close RimWorld.
2. In `ModsConfig.xml` disable **Puppet Bio-Regeneration Fix** (or unsubscribe/remove it). If you
   leave both enabled, 1.0.0's Harmony ID (`ErtzFiend.PuppetBioRegenerationFix`) and 1.1.0's
   (`ErtzFiend.RimworldBiosculpterCompat`) both patch the same methods; 1.0.0's narrower rules still
   work, but the tooltip fix and the six new protections are only complete with 1.0.0 removed.
3. Delete the old `Mods/PuppetBioRegenFix` folder. It ships `PuppetBioRegenFix.dll`; 1.1.0 ships
   `RimworldBiosculpterCompat.dll`, and two mods must never be left side by side with the old one.
4. Extract `RimworldBiosculpterCompat` from `RimworldBiosculpterCompat-1.1.0.zip` into RimWorld's
   `Mods` directory.
5. Enable **Rimworld Biosculpter Compat** after Harmony and after the optional mods you use, in the
   order listed in `loadAfter`, then restart RimWorld.
6. Check the log for `[Rimworld Biosculpter Compat 1.1.0] Harmony ErtzFiend.RimworldBiosculpterCompat:
   protecting ...`.

No save migration is needed: the mod has no `GameComponent`, no `WorldComponent` and no defs of its
own, and the package id change only affects the mod list. Saves made with 1.0.0 load unchanged.

This prevents future cycle-related removal; it does not restore puppet links or implants already
lost. Version 1.1.0 passed 258 executable regression assertions against the installed game
assemblies, covering both vanilla and Enhanced Bioregeneration completion and description paths. A
real in-game cycle has **not** been run; engine plumbing is stubbed as described below.

## Build and test

```
RIMWORLD_LIBS=/path/outside/repo ./build.sh
```

`RIMWORLD_LIBS` must contain your installed `Assembly-CSharp.dll`, `UnityStubs.dll` and
`0Harmony.dll`. For the tests it also needs `EnhancedBioregeneration.dll` and `data/` copies of the
installed XML: VPE Puppeteer `Hediffs.xml`, Ideology `Buildings_Ideo.xml`, Enhanced Bioregeneration's
`Patches/Buildings_Ideo.xml`, Biotech `Hediffs_Mechanitor.xml`, Integrated Implants
`Hediffs_ShoulderWeaponry.xml` and `Mods/Biotech/.../Hediffs_Mechanitor.xml`, and Vanilla Races
Expanded – Sanguophage `Hediffs.xml` + `GeneDefs/GeneDefs_Hands.xml`. This repository contains none
of these proprietary files.

The test runner (`Tests/RimworldBiosculpterCompat.Tests`) runs the real vanilla `CycleCompleted` and
`GetHealingDescriptionForPawn`, and the real Enhanced Bioregeneration patches, with the mod's Harmony
patches registered through its startup class. It checks that:

* the bug reproduces for all eight defNames without the fix (both cycle implementations, both tooltips)
* all eight survive a bioregeneration cycle, as the same instance on the same part
* the tooltip no longer lists them, while still listing a real injury and `VPEP_BrainLeech`
* injuries, other hediffs, other pawns, medic cycles and out-of-cycle removal are unaffected
* near-miss defNames (`VPEP_PuppetX`, `vpep_puppet`, `ControlSublink`, ...) are not matched
* the scope is cleared after an exception and after every description call
* the old behaviour returns after `UnpatchAll`

Only engine plumbing that needs a running game is stubbed: render caches, downed/death checks,
messages, translation and logging.

## License

MIT