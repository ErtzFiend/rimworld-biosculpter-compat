using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

[assembly: InternalsVisibleTo("RimworldBiosculpterCompat.Tests")]

namespace RimworldBiosculpterCompat
{
    /// <summary>
    /// Exact hediff defNames that must survive a biosculpter bioregeneration cycle.
    /// Every entry was read from the live, enabled mod set — defName, never the label.
    /// Part names below are the part the hediff is actually attached to at runtime, from the
    /// installing ThingDef's CompProperties_UseEffectInstallImplant / a VEF GeneExtension, not a guess:
    ///   VPEP_Puppet / VPEP_Puppeteer          Vanilla Psycasts Expanded - Puppeteer (VPEPuppeteer), brain
    ///   ControlSublinkImplant                 Biotech, Defs/HediffDefs/Hediffs_Mechanitor.xml, brain
    ///   RemoteRepairerImplant                 Biotech, same file, brain
    ///   MechFormfeederImplant                 Biotech, same file, brain (label "mech gestation processor")
    ///   RemoteShielderImplant                 Biotech, same file, brain (RemoteShielder install comp)
    ///   RepairProbeImplant                    Biotech, same file, brain (RepairProbe install comp)
    ///   WarprogrammerInterfaceImplant         Integrated Implants (lts.I), 1.7/Mods/Biotech/Defs/HediffDefs/Hediffs_Mechanitor.xml, brain
    ///   RemoteDominatorImplant                lts.I, same file, brain
    ///   RemoteResurrectorImplant              lts.I, same file, brain
    ///   SignalBoosterImplant                  lts.I, same file, brain (SignalBooster/High/Ultra install comps)
    ///   MechwombImplant                       lts.I, same file, torso (Mechwomb install comp)
    ///   LTS_ShoulderTurretActive              lts.I, 1.7/Defs/HediffDefs/Hediffs_ShoulderWeaponry.xml, torso
    ///   LTS_ShoulderBeamTurretActive          lts.I, same file, torso (the def carries no bodyPart itself;
    ///                                         its install recipe is appliedOnFixedBodyParts Torso)
    ///   LTS_ShoulderChargeTurretActive        lts.I, same file, torso (same evidence)
    ///   VRE_SharpTalons                       Vanilla Races Expanded - Sanguophage, one per hand
    ///   VQEA_PlasteelClaws                    Vanilla Quests Expanded - Ancients, gene hediff, one per hand
    ///   VRE_InfectorHands                     Vanilla Races Expanded - Fungoid, gene hediff, one per hand
    ///   VRE_InfectorMouth                     Vanilla Races Expanded - Fungoid, gene hediff, jaw
    ///   VRESaurids_SauridClaws                Vanilla Races Expanded - Saurid, gene hediff, one per hand
    ///   SentienceCatalyst                     Odyssey, Defs/HediffDefs/Hediffs_BodyParts_Prosthetic.xml,
    ///                                         brain (TargetEffectInstallImplantInOtherPawn on the item)
    ///   MAG_ArchonexusCore_One/_Two/_Three    More Archotech Garbage, 1.6/Compat/Ideology/Defs/Ideology_Crafting.xml,
    ///                                         torso (CompProperties_UseEffectInstallImplant bodyPart Torso)
    /// All of these use the default everCurableByItem=true, none sets countsAsAddedPartOrImplant and none
    /// is chronic, so once their part is restorable the cycle cures them like injuries. keepOnBodyPartRestoration
    /// is ignored by the biosculpter cycle (it only affects generic part-restoration handling), which is why the
    /// brain implants are at risk too.
    /// </summary>
    public static class PreservedHediffs
    {
        /// <summary>Hediff on a puppet's brain (VPEPuppeteer.Hediff_Puppet).</summary>
        public const string PuppetDefName = "VPEP_Puppet";

        /// <summary>Hediff on the puppet master's brain (VPEPuppeteer.Hediff_Puppeteer).</summary>
        public const string PuppeteerDefName = "VPEP_Puppeteer";

        /// <summary>Biotech mechanitor implant on the brain, Hediff_Level, up to 6.</summary>
        public const string ControlSublinkDefName = "ControlSublinkImplant";

        /// <summary>Biotech mechanitor implant on the brain, Hediff_Level, up to 3.</summary>
        public const string RemoteRepairerDefName = "RemoteRepairerImplant";

        /// <summary>Biotech mechanitor implant on the brain, Hediff_Level, up to 6; label "mech gestation processor".</summary>
        public const string MechFormfeederDefName = "MechFormfeederImplant";

        /// <summary>Biotech mechanitor implant on the brain, Hediff_Level, up to 3 (RemoteShielder).</summary>
        public const string RemoteShielderDefName = "RemoteShielderImplant";

        /// <summary>Biotech mechanitor implant on the brain, Hediff_Level, up to 6 (RepairProbe).</summary>
        public const string RepairProbeDefName = "RepairProbeImplant";

        /// <summary>Integrated Implants mechanitor implant on the brain, Hediff_Level, up to 3.</summary>
        public const string WarprogrammerInterfaceDefName = "WarprogrammerInterfaceImplant";

        /// <summary>Integrated Implants mechanitor implant on the brain, Hediff_Level.</summary>
        public const string RemoteDominatorDefName = "RemoteDominatorImplant";

        /// <summary>Integrated Implants mechanitor implant on the brain, Hediff_Level.</summary>
        public const string RemoteResurrectorDefName = "RemoteResurrectorImplant";

        /// <summary>Integrated Implants mechanitor implant on the brain, Hediff_Level.</summary>
        public const string SignalBoosterDefName = "SignalBoosterImplant";

        /// <summary>Integrated Implants mechanitor implant on the torso, Hediff_Level.</summary>
        public const string MechwombDefName = "MechwombImplant";

        /// <summary>Integrated Implants hidden shoulder-weapon hediff on the torso; drives the turret verb.</summary>
        public const string ShoulderTurretDefName = "LTS_ShoulderTurretActive";

        /// <summary>Integrated Implants beam-turret active hediff; the installed turret is mounted on the torso.</summary>
        public const string ShoulderBeamTurretDefName = "LTS_ShoulderBeamTurretActive";

        /// <summary>Integrated Implants charge-turret active hediff; the installed turret is mounted on the torso.</summary>
        public const string ShoulderChargeTurretDefName = "LTS_ShoulderChargeTurretActive";

        /// <summary>Vanilla Races Expanded - Sanguophage gene hediff, one per hand.</summary>
        public const string SharpTalonsDefName = "VRE_SharpTalons";

        /// <summary>Vanilla Quests Expanded - Ancients archite gene hediff, one per hand.</summary>
        public const string PlasteelClawsDefName = "VQEA_PlasteelClaws";

        /// <summary>Vanilla Races Expanded - Fungoid gene hediff, one per hand.</summary>
        public const string InfectorHandsDefName = "VRE_InfectorHands";

        /// <summary>Vanilla Races Expanded - Fungoid gene hediff on the jaw.</summary>
        public const string InfectorMouthDefName = "VRE_InfectorMouth";

        /// <summary>Vanilla Races Expanded - Saurid gene hediff, one per hand.</summary>
        public const string SauridClawsDefName = "VRESaurids_SauridClaws";

        /// <summary>Odyssey archotech sentience hediff installed on an animal's brain.</summary>
        public const string SentienceCatalystDefName = "SentienceCatalyst";

        /// <summary>More Archotech Garbage archonexus key I, installed on the torso.</summary>
        public const string ArchonexusKeyOneDefName = "MAG_ArchonexusCore_One";

        /// <summary>More Archotech Garbage archonexus key II, installed on the torso.</summary>
        public const string ArchonexusKeyTwoDefName = "MAG_ArchonexusCore_Two";

        /// <summary>More Archotech Garbage archonexus key III, installed on the torso.</summary>
        public const string ArchonexusKeyThreeDefName = "MAG_ArchonexusCore_Three";

        /// <summary>All protected defNames. Always compared with Ordinal (case-sensitive) equality.</summary>
        public static readonly string[] AllDefNames =
        {
            PuppetDefName,
            PuppeteerDefName,
            ControlSublinkDefName,
            RemoteRepairerDefName,
            MechFormfeederDefName,
            RemoteShielderDefName,
            RepairProbeDefName,
            WarprogrammerInterfaceDefName,
            RemoteDominatorDefName,
            RemoteResurrectorDefName,
            SignalBoosterDefName,
            MechwombDefName,
            ShoulderTurretDefName,
            ShoulderBeamTurretDefName,
            ShoulderChargeTurretDefName,
            SharpTalonsDefName,
            PlasteelClawsDefName,
            InfectorHandsDefName,
            InfectorMouthDefName,
            SauridClawsDefName,
            SentienceCatalystDefName,
            ArchonexusKeyOneDefName,
            ArchonexusKeyTwoDefName,
            ArchonexusKeyThreeDefName,
        };

        public static bool IsPreservedDef(HediffDef def)
        {
            if (def == null) return false;
            string name = def.defName;
            if (name == null) return false;
            string[] all = AllDefNames;
            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(name, all[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsPreservedHediff(Hediff hediff)
        {
            return hediff != null && IsPreservedDef(hediff.def);
        }
    }

    /// <summary>
    /// Scopes for the two places where the game decides a hediff will be healed: the biosculpter
    /// cycle itself, and the "will heal" description used in the pod tooltip. Both are
    /// thread-static, opened by a Priority.First prefix and cleared by a finalizer.
    /// </summary>
    public static class BiosculpterScope
    {
        [ThreadStatic] private static Pawn regeneratingPawn;
        [ThreadStatic] private static Pawn describingPawn;

        /// <summary>Pawn whose bioregeneration cycle is completing right now (null otherwise).</summary>
        public static Pawn Current
        {
            get => regeneratingPawn;
            internal set => regeneratingPawn = value;
        }

        /// <summary>Pawn whose bioregeneration healing description is being built right now (null otherwise).</summary>
        public static Pawn DescriptionPawn
        {
            get => describingPawn;
            internal set => describingPawn = value;
        }

        /// <summary>
        /// True only if the hediff is a preserved hediff on the pawn whose bioregeneration cycle
        /// is completing right now.
        /// </summary>
        public static bool ShouldPreserve(Hediff hediff)
        {
            Pawn cyclePawn = regeneratingPawn;
            return cyclePawn != null
                && hediff != null
                && hediff.pawn == cyclePawn
                && PreservedHediffs.IsPreservedDef(hediff.def);
        }

        /// <summary>
        /// True only if the healing decision concerns the pawn whose description is being built
        /// right now and the hediff is preserved. Used to keep the pod tooltip accurate.
        /// </summary>
        public static bool ShouldHideFromDescription(Pawn pawn, Hediff hediff)
        {
            Pawn descriptionPawn = describingPawn;
            return descriptionPawn != null
                && pawn == descriptionPawn
                && hediff != null
                && hediff.pawn == descriptionPawn
                && PreservedHediffs.IsPreservedDef(hediff.def);
        }
    }

    /// <summary>
    /// Opens the bioregeneration scope around CycleCompleted. Runs first so the scope is active
    /// even when another mod's prefix (e.g. BattIePatch: Enhanced Bioregeneration) replaces the
    /// original method body.
    /// </summary>
    [HarmonyPatch(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.CycleCompleted))]
    public static class Patch_HealingCycle_CycleCompleted
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(CompBiosculpterPod_HealingCycle __instance, Pawn pawn, out Pawn __state)
        {
            __state = BiosculpterScope.Current;
            if (__instance != null && __instance.Regenerate)
            {
                BiosculpterScope.Current = pawn;
            }
        }

        public static Exception Finalizer(Exception __exception, Pawn __state)
        {
            BiosculpterScope.Current = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Opens the description scope around GetHealingDescriptionForPawn. Vanilla builds the list
    /// itself; Enhanced Bioregeneration replaces it from a prefix on the same method
    /// (HealingCycle_Utils.GetHealingDescriptionForPawn). Opening the scope before either runs
    /// lets the WillHeal patches filter the tooltip on both paths.
    /// </summary>
    [HarmonyPatch(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.GetHealingDescriptionForPawn))]
    public static class Patch_HealingCycle_GetHealingDescriptionForPawn
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(CompBiosculpterPod_HealingCycle __instance, Pawn pawn, out Pawn __state)
        {
            __state = BiosculpterScope.DescriptionPawn;
            if (__instance != null && __instance.Regenerate)
            {
                BiosculpterScope.DescriptionPawn = pawn;
            }
        }

        public static Exception Finalizer(Exception __exception, Pawn __state)
        {
            BiosculpterScope.DescriptionPawn = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Vanilla WillHeal (private instance method) decides both what the cycle cures and what the
    /// tooltip lists. Force it to false for preserved hediffs of the pawn in scope. Wounds, scars,
    /// missing parts and every other condition are untouched.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_HealingCycle_WillHeal
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(CompBiosculpterPod_HealingCycle), "WillHeal", new[] { typeof(Pawn), typeof(Hediff) });
        }

        public static void Postfix(Pawn pawn, Hediff hediff, ref bool __result)
        {
            if (!__result) return;
            if (BiosculpterScope.ShouldPreserve(hediff) || BiosculpterScope.ShouldHideFromDescription(pawn, hediff))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Both vanilla CycleCompleted and Enhanced Bioregeneration's replacement remove healed
    /// hediffs through HealthUtility.Cure(Hediff). This guard keeps a preserved hediff of the
    /// cycle pawn from being removed even if another mod reaches Cure by another route.
    /// </summary>
    [HarmonyPatch(typeof(HealthUtility), nameof(HealthUtility.Cure), new Type[] { typeof(Hediff) })]
    public static class Patch_HealthUtility_Cure
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Hediff hediff, ref TaggedString __result)
        {
            if (!BiosculpterScope.ShouldPreserve(hediff))
            {
                return true;
            }
            __result = new TaggedString(string.Empty);
            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class RimworldBiosculpterCompatStartup
    {
        public const string HarmonyId = "ErtzFiend.RimworldBiosculpterCompat";
        public const string Version = "1.2.0";

        static RimworldBiosculpterCompatStartup()
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(RimworldBiosculpterCompatStartup).Assembly);

            int optional = EnhancedBioregenerationSupport.PatchIfLoaded(harmony);

            Log.Message($"[Rimworld Biosculpter Compat {RimworldBiosculpterCompatStartup.Version}] Harmony {harmony.Id}: protecting "
                + string.Join(", ", PreservedHediffs.AllDefNames)
                + " during bioregeneration"
                + (optional > 0 ? " (Enhanced Bioregeneration tooltip path detected)." : "."));
        }
    }

    /// <summary>
    /// Enhanced Bioregeneration is optional and has no compile-time reference here. When its
    /// assembly is loaded, its private static HealingCycle_Utils.WillHeal is patched by
    /// reflection so the replaced healing-description path is filtered too.
    /// </summary>
    internal static class EnhancedBioregenerationSupport
    {
        private const string UtilsTypeName = "BattIePatch_EnhancedBioregeneration.HealingCycle_Utils";

        internal static int PatchIfLoaded(Harmony harmony)
        {
            Type utils = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                utils = asm.GetType(UtilsTypeName, throwOnError: false);
                if (utils != null) break;
            }
            if (utils == null)
            {
                // Not installed (or not loaded): every protection still works through the
                // CycleCompleted and Cure patches; only Enhanced Bioregeneration's custom
                // tooltip text can then list protected hediffs.
                return 0;
            }

            return PatchType(harmony, utils);
        }

        /// <summary>
        /// Patches the resolved WillHeal method. A null type (optional mod absent) is a safe no-op,
        /// which is also the unit-test seam for the "mod not installed" case.
        /// </summary>
        internal static int PatchType(Harmony harmony, Type utils)
        {
            if (utils == null || harmony == null) return 0;

            MethodInfo willHeal = AccessTools.Method(utils, "WillHeal", new[]
            {
                typeof(Pawn), typeof(Hediff), typeof(bool), typeof(CompProperties_BiosculpterPod_HealingCycle)
            });
            if (willHeal == null)
            {
                Log.Warning($"[Rimworld Biosculpter Compat {RimworldBiosculpterCompatStartup.Version}] Enhanced Bioregeneration is loaded but its WillHeal signature was not found. All protections remain active; its tooltip may still list protected hediffs.");
                return 0;
            }

            try
            {
                harmony.Patch(willHeal, postfix: new HarmonyMethod(typeof(EnhancedBioregenerationSupport), nameof(WillHealPostfix)));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning($"[Rimworld Biosculpter Compat {RimworldBiosculpterCompatStartup.Version}] Could not patch Enhanced Bioregeneration WillHeal: {e.GetType().Name}: {e.Message}");
                return 0;
            }
        }

        public static void WillHealPostfix(Pawn pawn, Hediff hediff, ref bool __result)
        {
            if (!__result) return;
            if (BiosculpterScope.ShouldPreserve(hediff) || BiosculpterScope.ShouldHideFromDescription(pawn, hediff))
            {
                __result = false;
            }
        }
    }
}