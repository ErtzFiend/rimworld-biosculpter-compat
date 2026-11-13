# Puppet Bio-Regeneration Fix

A RimWorld 1.7 Harmony mod. It stops a biosculpter **bioregeneration** cycle from removing
[Vanilla Psycasts Expanded – Puppeteer](https://steamcommunity.com/sharedfiles/filedetails/?id=3033779606)'s
**puppet** and **puppet master** hediffs.

## The problem

VPE Puppeteer puts two hediffs on the **brain**. Both defNames come from `1.6/Defs/HediffDefs/Hediffs.xml`, which VPE Puppeteer also loads on RimWorld 1.7:

| defName | class | on |
|---|---|---|
| `VPEP_Puppet` | `VPEPuppeteer.Hediff_Puppet` | the puppet |
| `VPEP_Puppeteer` | `VPEPuppeteer.Hediff_Puppeteer` | the puppet master |

Neither def sets `everCurableByItem`, so both use the default value of `true`.

In RimWorld 1.7, `CompBiosculpterPod_HealingCycle.CycleCompleted` cures any hediff whose part is listed in the cycle's `bodyPartsToRestore`, using `WillHeal` → `HealthUtility.Cure(Hediff)` → `Pawn_HealthTracker.RemoveHediff`. Vanilla's list is only Eye, Ear, Nose, Finger, Toe and Tongue. **BattIePatch: Enhanced Bioregeneration** adds `Brain`, among other parts, through an XML patch. It also replaces `CycleCompleted` with its own `HealingCycle_Utils.CycleCompleted` when any of its scar or regeneration settings is on. That version also calls `HealthUtility.Cure`. As a result, both puppet hediffs are cured when the cycle finishes:

* puppet: `Hediff_Puppet.PostRemoved` adds **Abasia** and a **psychic coma**, and removes the pawn from the master's list
* master: the `VPEP_Puppeteer` hediff and its puppet list are lost

## The fix

* A prefix (priority First) and a finalizer on `CompBiosculpterPod_HealingCycle.CycleCompleted` open a scope for the pod occupant. This only happens when `Regenerate` is true, which means a bioregeneration cycle.
* A prefix on `HealthUtility.Cure(Hediff)` skips the cure only when all of these are true: the scope is open, the hediff belongs to the occupant, and its `def.defName` is exactly `VPEP_Puppet` or `VPEP_Puppeteer`.

Everything else stays the same: wounds, scars, missing parts, conditions, other brain hediffs (for example `VPEP_BrainLeech`), medic cycles, and any removal outside the cycle. The mod doesn't patch the vanilla "will heal" description text, so the pod tooltip can still list the puppet hediff.

## Compatibility

* Built and tested only against **RimWorld 1.7.5005 rev1082** (Assembly-CSharp 1.7.9810.32479) with Harmony 3.0 (brrainz.harmony).
* Required: Harmony, Ideology and VPE Puppeteer. Enhanced Bioregeneration is optional (load after it if you use it).
* No save-game data. You can add or remove the mod at any time.

## Installation

Download `PuppetBioRegenFix-1.0.0.zip` from [Releases](https://github.com/ErtzFiend/rimworld-puppet-bio-regeneration-fix/releases/tag/v1.0.0), then extract the `PuppetBioRegenFix` folder into RimWorld's `Mods` directory. Enable **Puppet Bio-Regeneration Fix** after Harmony, Vanilla Psycasts Expanded, Puppeteer and Enhanced Bioregeneration (if installed), then restart RimWorld.

This prevents future cycle-related removal; it does not restore puppet links already lost. Version 1.0.0 passed 64 executable regression assertions against the installed game assemblies, including both vanilla and Enhanced Bioregeneration completion paths. A real in-game cycle has **not** been tested; engine plumbing is stubbed as described below.

## Build and test

```
RIMWORLD_LIBS=/path/outside/repo ./build.sh
```

`RIMWORLD_LIBS` must contain your installed `Assembly-CSharp.dll`, `UnityStubs.dll` and `0Harmony.dll`. For the tests it also needs `EnhancedBioregeneration.dll` and `data/` copies of the installed XML (VPE Puppeteer `Hediffs.xml`, Ideology `Buildings_Ideo.xml`, and Enhanced Bioregeneration's `Patches/Buildings_Ideo.xml`). This repository contains none of these proprietary files.

The test runner (`Tests/PuppetBioRegenFix.Tests`) runs the real vanilla `CycleCompleted` and the real Enhanced Bioregeneration prefix, with the mod's Harmony patches registered through its startup class. It checks that:

* the bug reproduces without the fix
* both hediffs survive with the fix
* injuries still heal
* other hediffs, other pawns, medic cycles and out-of-cycle removal are unaffected
* the scope is cleared after an exception
* the old behaviour returns after unpatching

Only engine plumbing that needs a running game is stubbed: render caches, downed/death checks, messages, translation and logging.

## License

MIT
