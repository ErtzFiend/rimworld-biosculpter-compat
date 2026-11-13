using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

[assembly: InternalsVisibleTo("PuppetBioRegenFix.Tests")]

namespace PuppetBioRegenFix
{
    /// <summary>
    /// Exact Vanilla Psycasts Expanded - Puppeteer hediff defNames that must survive a
    /// biosculpter bioregeneration cycle (from VPE Puppeteer 1.6/Defs/HediffDefs/Hediffs.xml).
    /// </summary>
    public static class PreservedHediffs
    {
        /// <summary>Hediff on a puppet's brain (class VPEPuppeteer.Hediff_Puppet).</summary>
        public const string PuppetDefName = "VPEP_Puppet";

        /// <summary>Hediff on the puppet master's brain (class VPEPuppeteer.Hediff_Puppeteer).</summary>
        public const string PuppeteerDefName = "VPEP_Puppeteer";

        public static bool IsPreservedDef(HediffDef def)
        {
            if (def == null) return false;
            string name = def.defName;
            return string.Equals(name, PuppetDefName, StringComparison.Ordinal)
                || string.Equals(name, PuppeteerDefName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Tracks the pawn whose bioregeneration cycle is currently completing.
    /// Set only for the duration of CompBiosculpterPod_HealingCycle.CycleCompleted when
    /// the cycle regenerates (vanilla "bioregeneration"); cleared by a Harmony finalizer.
    /// </summary>
    public static class BioregenerationScope
    {
        [ThreadStatic] private static Pawn current;

        public static Pawn Current
        {
            get => current;
            internal set => current = value;
        }

        /// <summary>
        /// True only if the hediff is a puppet/puppeteer hediff belonging to the pawn whose
        /// bioregeneration cycle is completing right now.
        /// </summary>
        public static bool ShouldPreserve(Hediff hediff)
        {
            Pawn cyclePawn = current;
            return cyclePawn != null
                && hediff != null
                && hediff.pawn == cyclePawn
                && PreservedHediffs.IsPreservedDef(hediff.def);
        }
    }

    /// <summary>
    /// Opens the bioregeneration scope around CycleCompleted. Runs first so the scope is
    /// active even when another mod's prefix (e.g. BattIePatch: Enhanced Bioregeneration)
    /// replaces the original method body.
    /// </summary>
    [HarmonyPatch(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.CycleCompleted))]
    public static class Patch_HealingCycle_CycleCompleted
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(CompBiosculpterPod_HealingCycle __instance, Pawn pawn, out Pawn __state)
        {
            __state = BioregenerationScope.Current;
            if (__instance != null && __instance.Regenerate)
            {
                BioregenerationScope.Current = pawn;
            }
        }

        public static Exception Finalizer(Exception __exception, Pawn __state)
        {
            BioregenerationScope.Current = __state;
            return __exception;
        }
    }

    /// <summary>
    /// Both vanilla CycleCompleted and Enhanced Bioregeneration's replacement remove healed
    /// hediffs through HealthUtility.Cure(Hediff). Skip that removal only for the cycle pawn's
    /// VPEP_Puppet / VPEP_Puppeteer hediffs while the bioregeneration scope is open.
    /// </summary>
    [HarmonyPatch(typeof(HealthUtility), nameof(HealthUtility.Cure), new Type[] { typeof(Hediff) })]
    public static class Patch_HealthUtility_Cure
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Hediff hediff, ref TaggedString __result)
        {
            if (!BioregenerationScope.ShouldPreserve(hediff))
            {
                return true;
            }
            __result = new TaggedString(string.Empty);
            return false;
        }
    }

    [StaticConstructorOnStartup]
    public static class PuppetBioRegenFixStartup
    {
        public const string HarmonyId = "ErtzFiend.PuppetBioRegenerationFix";

        static PuppetBioRegenFixStartup()
        {
            new Harmony(HarmonyId).PatchAll(typeof(PuppetBioRegenFixStartup).Assembly);
            Log.Message("[Puppet Bio-Regeneration Fix 1.0.0] Harmony patches applied: protecting VPEP_Puppet / VPEP_Puppeteer during bioregeneration.");
        }
    }
}
