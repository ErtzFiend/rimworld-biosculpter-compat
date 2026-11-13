// Regression tests for Rimworld Biosculpter Compat.
//
// Runs the REAL vanilla CompBiosculpterPod_HealingCycle (CycleCompleted and
// GetHealingDescriptionForPawn) and the REAL BattIePatch: Enhanced Bioregeneration Harmony
// patches, with this mod's Harmony patches registered through its actual
// [StaticConstructorOnStartup] class.
//
// Only engine plumbing that needs a running game (render-cache dirtying, downed/death state
// checks, on-screen messages, translation) is stubbed by a separate test-only Harmony instance.
// Hediff removal itself (HealthUtility.Cure -> Pawn_HealthTracker.RemoveHediff -> list removal)
// is real game code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using BattIePatch_EnhancedBioregeneration;
using HarmonyLib;
using RimWorld;
using RimworldBiosculpterCompat;
using Verse;

internal static class Program
{
    static string Libs;
    static int passed, failed;
    static readonly List<string> Failures = new List<string>();

    // Real data read from installed mod/game XML (copied outside the repo).
    static HediffDef PuppetDef, PuppeteerDef, BrainLeechDef, InjuryDef;
    static HediffDef ControlSublinkDef, RemoteRepairerDef, MechFormfeederDef, WarprogrammerDef, ShoulderTurretDef, SharpTalonsDef;
    static HediffDef RemoteShielderDef, RepairProbeDef, RemoteDominatorDef, RemoteResurrectorDef, SignalBoosterDef,
        MechwombDef, ShoulderBeamActiveDef, ShoulderChargeActiveDef, PlasteelClawsDef, InfectorHandsDef, InfectorMouthDef,
        SauridClawsDef, SentienceCatalystDef, ArchonexusKeyOneDef, ArchonexusKeyTwoDef, ArchonexusKeyThreeDef,
        NeutroamineIncubatorDef, GrowthStimulatorDef;
    static HediffDef TraumaSavantDef, SuturedEyeDef, SuturedMouthDef;
    static BodyPartDef BrainDef, HeadDef, FingerDef, TorsoDef, HandDef, JawDef, EyeDef;
    // Assigned in LoadRealData: a field initializer here would force the game assemblies to load
    // before Main registers the AssemblyLoadContext resolver.
    static Dictionary<string, BodyPartDef> PartDefs;

    // Exact user-approved set for v1.2.1 (24 prior + 2 J resource-farm implants). The tests assert
    // the mod protects exactly this set and nothing else; TraumaSavant and the two WCE2 sutures are
    // deliberately excluded because the user chose to let the biosculpter heal them.
    static readonly string[] ApprovedDefNames =
    {
        "VPEP_Puppet", "VPEP_Puppeteer", "ControlSublinkImplant", "RemoteRepairerImplant",
        "MechFormfeederImplant", "RemoteShielderImplant", "RepairProbeImplant",
        "WarprogrammerInterfaceImplant", "RemoteDominatorImplant", "RemoteResurrectorImplant",
        "SignalBoosterImplant", "MechwombImplant", "LTS_ShoulderTurretActive",
        "LTS_ShoulderBeamTurretActive", "LTS_ShoulderChargeTurretActive", "VRE_SharpTalons",
        "VQEA_PlasteelClaws", "VRE_InfectorHands", "VRE_InfectorMouth", "VRESaurids_SauridClaws",
        "SentienceCatalyst", "MAG_ArchonexusCore_One", "MAG_ArchonexusCore_Two", "MAG_ArchonexusCore_Three",
        "WCE2_NeutroamineIncubator", "WCE2_GrowthStimulator",
    };
    static List<BodyPartDef> VanillaRestore, EnhancedRestore;

    static int Main()
    {
        Libs = Environment.GetEnvironmentVariable("RIMWORLD_LIBS") ?? "/root/rimworld_libs/1.7.5005-rev1082";
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string n = name.Name ?? "";
            if (n.StartsWith("UnityEngine") || n.StartsWith("Unity.") || n == "Assembly-CSharp-firstpass")
                n = "UnityStubs";
            string p = Path.Combine(Libs, n + ".dll");
            return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null;
        };
        try { Run(); }
        catch (Exception e) { Fail("harness", e.ToString()); }
        Console.WriteLine();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        foreach (var f in Failures) Console.WriteLine("  FAILED: " + f);
        foreach (var l in GameLog.Distinct()) Console.WriteLine("  [game log] " + l.Split('\n')[0]);
        return failed == 0 ? 0 : 1;
    }

    static void Run()
    {
        Console.WriteLine("Game assembly: " + typeof(Pawn).Assembly.GetName().Version + "  (" + typeof(Pawn).Assembly.Location + ")");
        Console.WriteLine("Harmony: " + typeof(Harmony).Assembly.GetName().Version);
        Console.WriteLine("EnhancedBioregeneration: " + typeof(HealingCycle_Utils).Assembly.Location);
        LoadRealData();
        InstallEnvironmentStubs();

        // Real Enhanced Bioregeneration patches (as installed).
        new Harmony("test.realmod.EnhancedBioregeneration").PatchAll(typeof(HealingCycle_Utils).Assembly);
        Check("EB prefix registered on CycleCompleted",
            Harmony.GetPatchInfo(CycleCompletedMethod).Prefixes.Any(p => p.owner == "test.realmod.EnhancedBioregeneration"));
        Check("EB prefix registered on GetHealingDescriptionForPawn",
            Harmony.GetPatchInfo(DescriptionMethod).Prefixes.Any(p => p.owner == "test.realmod.EnhancedBioregeneration"));

        // ---------- BASELINE: fix NOT installed -> the bug reproduces ----------
        Console.WriteLine("\n== Baseline (fix not loaded) ==");
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB prefix path" : "vanilla CycleCompleted + EB restore list";
            foreach (var def in AllProtectedDefs())
            {
                var (pawn, target, _, injury) = MakePawn(def);
                RunCycle(Regen(), pawn);
                Check($"baseline/{path}: {def.defName} IS removed without fix (bug reproduces)", !Has(pawn, target));
                Check($"baseline/{path}: {def.defName} case: minor injury still healed", !Has(pawn, injury));
            }
            var (descPawn, _, _, _) = MakePawn(PuppetDef, allProtections: true);
            string baselineDesc = Regen().GetHealingDescriptionForPawn(descPawn);
            Check($"baseline/{path}: tooltip DID list VPEP_Puppet (the gap this version closes)",
                baselineDesc.Contains(PuppetDef.label));
            Check($"baseline/{path}: tooltip DID list sharp talons",
                baselineDesc.Contains(SharpTalonsDef.label));
        }
        Check("RimworldBiosculpterCompat patches not present before startup",
            Harmony.GetPatchInfo(CureMethod)?.Prefixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId) != true);

        // ---------- Install the fix exactly as the game does ----------
        RuntimeHelpers.RunClassConstructor(typeof(RimworldBiosculpterCompatStartup).TypeHandle);
        Console.WriteLine("\n== Harmony registration ==");
        Check("startup logged its banner via Verse.Log", GameLog.Any(l => l.StartsWith("[Rimworld Biosculpter Compat 1.2.1]")));
        Check("startup log names all 26 protected defNames", PreservedHediffs.AllDefNames.All(n => GameLog.Any(l => l.Contains(n))));
        var cc = Harmony.GetPatchInfo(CycleCompletedMethod);
        Check("CycleCompleted prefix from the mod",
            cc.Prefixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId && p.PatchMethod.DeclaringType == typeof(Patch_HealingCycle_CycleCompleted)));
        Check("CycleCompleted finalizer from the mod", cc.Finalizers.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId));
        Check("the mod's CycleCompleted prefix runs before EB's prefix (priority First)",
            cc.Prefixes.First(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId).priority > cc.Prefixes.First(p => p.owner.StartsWith("test.realmod")).priority);
        var desc = Harmony.GetPatchInfo(DescriptionMethod);
        Check("GetHealingDescriptionForPawn prefix from the mod",
            desc.Prefixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId && p.PatchMethod.DeclaringType == typeof(Patch_HealingCycle_GetHealingDescriptionForPawn)));
        Check("GetHealingDescriptionForPawn finalizer from the mod", desc.Finalizers.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId));
        Check("the mod's description prefix runs before EB's prefix (priority First)",
            desc.Prefixes.First(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId).priority > desc.Prefixes.First(p => p.owner.StartsWith("test.realmod")).priority);
        var cure = Harmony.GetPatchInfo(CureMethod);
        Check("HealthUtility.Cure(Hediff) prefix from the mod",
            cure.Prefixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId && p.PatchMethod.DeclaringType == typeof(Patch_HealthUtility_Cure)));
        Check("vanilla WillHeal postfix from the mod",
            Harmony.GetPatchInfo(VanillaWillHealMethod).Postfixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId));
        Check("EB WillHeal postfix from the mod (optional path detected)",
            Harmony.GetPatchInfo(EbWillHealMethod).Postfixes.Any(p => p.owner == RimworldBiosculpterCompatStartup.HarmonyId));
        Check("mod patches exactly 5 methods",
            Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(RimworldBiosculpterCompatStartup.HarmonyId)) == 5);

        // ---------- Exact def checks ----------
        Console.WriteLine("\n== Exact defName matching ==");
        foreach (var def in AllProtectedDefs()) Check($"{def.defName} preserved-def", PreservedHediffs.IsPreservedDef(def));
        Check("approved list has 26 unique defNames (24 prior + 2 J resource farms)",
            ApprovedDefNames.Length == 26 && ApprovedDefNames.Distinct().Count() == 26);
        Check("mod protects exactly the 26 approved defNames (none missing, no unapproved)",
            PreservedHediffs.AllDefNames.OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(ApprovedDefNames.OrderBy(n => n, StringComparer.Ordinal)));
        Check("mod's own list has 26 unique defNames",
            PreservedHediffs.AllDefNames.Length == 26 && PreservedHediffs.AllDefNames.Distinct().Count() == 26);
        Check("every approved defName is protected",
            ApprovedDefNames.All(n => PreservedHediffs.IsPreservedDef(new HediffDef { defName = n })));
        Check("VPEP_BrainLeech NOT preserved", !PreservedHediffs.IsPreservedDef(BrainLeechDef));
        Check("ordinary injury NOT preserved", !PreservedHediffs.IsPreservedDef(InjuryDef));
        Check("TraumaSavant NOT preserved (deliberate K choice: let the pod heal speech/hearing)",
            !PreservedHediffs.IsPreservedDef(TraumaSavantDef));
        Check("WCE2_SuturedEye NOT preserved (deliberate L choice: let the pod remove sutures)",
            !PreservedHediffs.IsPreservedDef(SuturedEyeDef));
        Check("WCE2_SuturedMouth NOT preserved (deliberate L choice: let the pod remove sutures)",
            !PreservedHediffs.IsPreservedDef(SuturedMouthDef));
        foreach (var n in new[]
                 {
                     "vpep_puppet", "VPEP_Puppet ", "VPEP_PuppetX", "VPEP_Puppeteers", "Puppet", "Puppeteer",
                     "controlsublinkimplant", "ControlSublink", "MechFormfeeder", "WarprogrammerInterface",
                     "LTS_ShoulderTurret", "VRE_SharpTalon", "VPEP_Puppet_Master_Death", "",
                     "RemoteShielder", "RepairProbe", "RemoteDominator", "RemoteResurrector", "SignalBooster",
                     "Mechwomb", "LTS_ShoulderBeamTurret", "LTS_ShoulderChargeTurret",
                     "MAG_ArchonexusCore_", "MAG_ArchonexusCore_Four", "mag_ArchonexusCore_One",
                     "VQEA_PlasteelClaw", "VRE_InfectorHand", "VRE_InfectorMouths", "VRESaurids_SauridClaw",
                     "sentiencecatalyst", "SentienceCatalystX", "SentienceCatalyst ",
                     "wce2_neutroamineincubator", "WCE2_NeutroamineIncubators", "NeutroamineIncubator",
                     "WCE2_GrowthStimulators", "GrowthStimulator", "WCE2_GrowthStimulator ",
                     "traumasavant", "TraumaSavantX", "WCE2_SuturedEyes", "WCE2_SuturedMouth "
                 })
            Check($"'{n}' NOT preserved (exact, case-sensitive)", !PreservedHediffs.IsPreservedDef(new HediffDef { defName = n }));
        Check("null def NOT preserved", !PreservedHediffs.IsPreservedDef(null));

        // ---------- Behaviour with the fix, both cycle implementations ----------
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB prefix replaces CycleCompleted (KeepMinorScars=True)" : "vanilla CycleCompleted body + EB restore list (EB toggles off)";
            Console.WriteLine($"\n== Bioregeneration with fix: {path} ==");
            foreach (var def in AllProtectedDefs())
            {
                var (pawn, keep, leech, injury) = MakePawn(def);
                RunCycle(Regen(), pawn);
                Check($"{def.defName} preserved through bioregeneration", Has(pawn, keep));
                Check($"{def.defName}: same instance, still on {ExpectedPart(def).defName}, same pawn",
                    Has(pawn, keep) && keep.Part.def == ExpectedPart(def) && keep.pawn == pawn);
                Check($"{def.defName}: fresh finger injury still healed (normal healing unaffected)", !Has(pawn, injury));
                Check($"{def.defName}: other brain hediff VPEP_BrainLeech still removed (narrow scope)", !Has(pawn, leech));
                Check($"{def.defName}: cycle scope closed after cycle", BiosculpterScope.Current == null);
            }
        }

        // ---------- Tooltip ("will heal") filtering, both description implementations ----------
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB HealingCycle_Utils.GetHealingDescriptionForPawn (replaced)" : "vanilla GetHealingDescriptionForPawn";
            Console.WriteLine($"\n== Tooltip description: {path} ==");
            var (pawn, _, _, _) = MakePawn(PuppetDef, allProtections: true);
            string text = Regen().GetHealingDescriptionForPawn(pawn);
            foreach (var def in AllProtectedDefs())
            {
                Check($"{path}: tooltip no longer lists {def.defName} (label '{def.label}')", !text.Contains(def.label));
            }
            Check($"{path}: no 'Archonexus key' text at all (labels I/II/III share a prefix)", !text.Contains("Archonexus key"));
            Check($"{path}: tooltip still lists the ordinary injury ('{InjuryDef.label}')", text.Contains(InjuryDef.label));
            Check($"{path}: tooltip still lists other brain hediff VPEP_BrainLeech", text.Contains(BrainLeechDef.label));
            Check($"{path}: description scope closed after the call", BiosculpterScope.DescriptionPawn == null);

            // Second call must behave identically (no leaked state between pod tooltip builds).
            string again = Regen().GetHealingDescriptionForPawn(pawn);
            Check($"{path}: repeated description call is stable", again == text);

            // Medic cycle description is not touched.
            string medicText = Medic().GetHealingDescriptionForPawn(pawn);
            Check($"{path}: medic cycle description unaffected (still lists injury)", medicText.Contains(InjuryDef.label));
        }

        // ---------- Deliberate K/L healing choice: excluded defs stay curable and visible ----------
        // The user explicitly chose to heal TraumaSavant (K) and the two WCE2 sutures (L). These are
        // regression assertions: neither protection may leak onto them, on either cycle implementation.
        Console.WriteLine("\n== Deliberate K/L healing choice: excluded defs remain curable ==");
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB prefix path" : "vanilla CycleCompleted body + EB restore list";
            foreach (var def in new[] { TraumaSavantDef, SuturedEyeDef, SuturedMouthDef })
            {
                var (pawn, target, _, injury) = MakePawn(def);
                RunCycle(Regen(), pawn);
                Check($"{path}: {def.defName} deliberately NOT protected -> cured by bioregeneration", !Has(pawn, target));
                Check($"{path}: {def.defName} cure does not disturb ordinary healing", !Has(pawn, injury));

                var (previewPawn, previewTarget, _, _) = MakePawn(def);
                string preview = Regen().GetHealingDescriptionForPawn(previewPawn);
                Check($"{path}: {def.defName} still appears in the preview (offered to cure) ('{def.label}')",
                    preview.Contains(def.label));
                Check($"{path}: {def.defName} preview case still leaves the hediff in place", Has(previewPawn, previewTarget));
                Check($"{path}: {def.defName} description scope closed after the call", BiosculpterScope.DescriptionPawn == null);
            }
        }

        // ---------- Outside the cycle: removal unaffected ----------
        Console.WriteLine("\n== Outside the bioregeneration cycle ==");
        foreach (var def in AllProtectedDefs())
        {
            var (p1, h1, _, _) = MakePawn(def);
            HealthUtility.Cure(h1);
            Check($"{def.defName}: HealthUtility.Cure outside cycle still removes", !Has(p1, h1));
            var (p2, h2, _, _) = MakePawn(def);
            p2.health.RemoveHediff(h2);
            Check($"{def.defName}: Pawn_HealthTracker.RemoveHediff outside cycle still removes", !Has(p2, h2));
        }

        // Medic cycle (Regenerate == false) must not open the scope.
        SetEb(false);
        {
            var (pawn, keep, leech, injury) = MakePawn(PuppetDef);
            Pawn seen = null; bool sawCall = false;
            CureSpy = h => { sawCall = true; seen = BiosculpterScope.Current; };
            RunCycle(Medic(), pawn);
            CureSpy = null;
            Check("medic cycle: Cure called (injury healed)", sawCall && !Has(pawn, injury));
            Check("medic cycle: bioregeneration scope NOT opened", seen == null);
        }

        // During a cycle, only the occupant's hediffs are protected.
        {
            var (occupant, _, _, _) = MakePawn(PuppetDef);
            var (other, otherPuppet, _, _) = MakePawn(PuppetDef);
            bool removedOther = false;
            CureSpy = h => { if (h.pawn == occupant && !removedOther) { removedOther = true; HealthUtility.Cure(otherPuppet); } };
            RunCycle(Regen(), occupant);
            CureSpy = null;
            Check("during cycle: another pawn's VPEP_Puppet is NOT protected", removedOther && !Has(other, otherPuppet));
        }

        // Exception inside cycle: scope restored by finalizer and exception still propagates.
        {
            var (pawn, _, _, _) = MakePawn(PuppetDef);
            bool threw = false;
            CureSpy = h => throw new InvalidOperationException("boom");
            try { RunCycle(Regen(), pawn); } catch (InvalidOperationException) { threw = true; }
            CureSpy = null;
            Check("exception propagates out of CycleCompleted", threw);
            Check("cycle scope cleared after exception (finalizer)", BiosculpterScope.Current == null);
            var (p2, h2, _, _) = MakePawn(PuppetDef);
            HealthUtility.Cure(h2);
            Check("after exception, removal outside cycle still works", !Has(p2, h2));
        }

        // Optional mod absent: remove Enhanced Bioregeneration's own patches and run the real vanilla
        // CycleCompleted body. Protection must still hold and ordinary healing must still work.
        // (The vanilla description path with this mod enabled is already covered above.)
        Console.WriteLine("\n== Optional mod (Enhanced Bioregeneration) absent ==");
        new Harmony("test.realmod.EnhancedBioregeneration").UnpatchAll("test.realmod.EnhancedBioregeneration");
        Check("EB absent: EB prefixes really are gone",
            !Harmony.GetPatchInfo(CycleCompletedMethod).Prefixes.Any(p => p.owner == "test.realmod.EnhancedBioregeneration"));
        {
            var (pawn, keep, _, injury) = MakePawn(PuppetDef);
            RunCycle(Regen(), pawn);
            Check("EB absent: VPEP_Puppet still preserved on the vanilla CycleCompleted body", Has(pawn, keep));
            Check("EB absent: ordinary finger injury still healed", !Has(pawn, injury));
            var (pawn2, keep2, _, injury2) = MakePawn(SharpTalonsDef);
            RunCycle(Regen(), pawn2);
            Check("EB absent: VRE_SharpTalons still preserved on the vanilla body", Has(pawn2, keep2) && !Has(pawn2, injury2));
            var (pawn3, keep3, _, injury3) = MakePawn(NeutroamineIncubatorDef);
            RunCycle(Regen(), pawn3);
            Check("EB absent: WCE2_NeutroamineIncubator still preserved on the vanilla body", Has(pawn3, keep3) && !Has(pawn3, injury3));
        }
        Check("EB absent: optional patch helper with a missing type is a safe no-op",
            EnhancedBioregenerationSupport.PatchType(new Harmony("test.ebless"), null) == 0);

        // Restore the installed EB patches so the final UnpatchAll test still exercises the EB path.
        new Harmony("test.realmod.EnhancedBioregeneration").PatchAll(typeof(HealingCycle_Utils).Assembly);

        // Unpatching restores original behaviour (the patches are what make the difference).
        var modHarmony = new Harmony(RimworldBiosculpterCompatStartup.HarmonyId);
        modHarmony.UnpatchAll(RimworldBiosculpterCompatStartup.HarmonyId);
        SetEb(true);
        {
            foreach (var def in new[] { PuppetDef, SharpTalonsDef, ControlSublinkDef, GrowthStimulatorDef })
            {
                var (pawn, target, _, _) = MakePawn(def);
                RunCycle(Regen(), pawn);
                Check($"after UnpatchAll(mod): {def.defName} removed again (mod is the cause)", !Has(pawn, target));
            }
        }
    }

    static IEnumerable<HediffDef> AllProtectedDefs() => new[]
    {
        PuppetDef, PuppeteerDef, ControlSublinkDef, RemoteRepairerDef, MechFormfeederDef,
        RemoteShielderDef, RepairProbeDef, WarprogrammerDef, RemoteDominatorDef, RemoteResurrectorDef,
        SignalBoosterDef, MechwombDef, ShoulderTurretDef, ShoulderBeamActiveDef, ShoulderChargeActiveDef,
        SharpTalonsDef, PlasteelClawsDef, InfectorHandsDef, InfectorMouthDef, SauridClawsDef,
        SentienceCatalystDef, ArchonexusKeyOneDef, ArchonexusKeyTwoDef, ArchonexusKeyThreeDef,
        NeutroamineIncubatorDef, GrowthStimulatorDef
    };

    // The body part each approved hediff actually attaches to, from its installing ThingDef /
    // VEF gene extension. LTS shoulder-turret active hediffs carry no bodyPart in their own XML;
    // their install recipes are appliedOnFixedBodyParts Torso, so that is the runtime mount used here.
    static string PartName(HediffDef def)
    {
        if (def == SharpTalonsDef || def == PlasteelClawsDef || def == InfectorHandsDef || def == SauridClawsDef) return "Hand";
        if (def == InfectorMouthDef || def == SuturedMouthDef) return "Jaw";
        if (def == SuturedEyeDef) return "Eye";
        if (def == TraumaSavantDef) return "Brain";
        if (def == NeutroamineIncubatorDef || def == GrowthStimulatorDef) return "Torso";
        if (def == ShoulderTurretDef || def == ShoulderBeamActiveDef || def == ShoulderChargeActiveDef
            || def == MechwombDef || def == ArchonexusKeyOneDef || def == ArchonexusKeyTwoDef
            || def == ArchonexusKeyThreeDef) return "Torso";
        return "Brain";
    }

    static BodyPartDef ExpectedPart(HediffDef def) => PartDefs[PartName(def)];

    // ---------------- real data ----------------
    static void LoadRealData()
    {
        string data = Path.Combine(Libs, "data");

        // Defs are instantiated as plain HediffWithComps so removal side effects (puppet PostRemoved,
        // implant messages) need no engine plumbing; the real hediffClass from the installed XML is
        // still asserted, and the protection only ever compares defName.
        HediffDef FromXml(XDocument doc, string defName, string expectedClass, string expectedLabel, params XDocument[] parentDocs)
        {
            var el = doc.Root.Elements("HediffDef").Single(e => (string)e.Element("defName") == defName);

            // Real mods use ParentName inheritance for hediffClass and flags. Resolve it across the
            // fixture set so the assertions read what the game would load, not just the direct child.
            IEnumerable<XDocument> docs = new[] { doc }.Concat(parentDocs);
            string Inherited(XElement node, string field)
            {
                for (int guard = 0; node != null && guard < 32; guard++)
                {
                    var value = node.Element(field);
                    if (value != null) return (string)value;
                    string parent = (string)node.Attribute("ParentName");
                    if (parent == null) return null;
                    node = docs.SelectMany(d => d.Root.Elements("HediffDef"))
                        .FirstOrDefault(e => (string)e.Attribute("Name") == parent);
                }
                return null;
            }

            string cls = Inherited(el, "hediffClass");
            string label = (string)el.Element("label");
            Check($"{defName}: installed hediffClass is {expectedClass}", cls == expectedClass);
            Check($"{defName}: installed label is '{expectedLabel}'", label == expectedLabel);
            bool everCurable = Inherited(el, "everCurableByItem") == null || bool.Parse(Inherited(el, "everCurableByItem"));
            Check($"{defName}: everCurableByItem is default true (bioregeneration WillHeal can cure it)", everCurable);
            bool addedPart = Inherited(el, "countsAsAddedPartOrImplant") != null && bool.Parse(Inherited(el, "countsAsAddedPartOrImplant"));
            Check($"{defName}: not flagged countsAsAddedPartOrImplant", !addedPart);
            Check($"{defName}: not chronic", Inherited(el, "chronic") == null);
            bool keepOnRestore = Inherited(el, "keepOnBodyPartRestoration") != null && bool.Parse(Inherited(el, "keepOnBodyPartRestoration"));
            Console.WriteLine($"    {defName}: keepOnBodyPartRestoration={keepOnRestore} (vanilla ignores this flag during the cycle)");
            return new HediffDef
            {
                defName = defName,
                label = label,
                hediffClass = typeof(HediffWithComps),
                everCurableByItem = everCurable,
                countsAsAddedPartOrImplant = addedPart,
                chronic = Inherited(el, "chronic") != null && bool.Parse(Inherited(el, "chronic")),
            };
        }

        var vpe = XDocument.Load(Path.Combine(data, "VPEPuppeteer_1.6_HediffDefs_Hediffs.xml"));
        PuppetDef = FromXml(vpe, "VPEP_Puppet", "VPEPuppeteer.Hediff_Puppet", "Puppet");
        PuppeteerDef = FromXml(vpe, "VPEP_Puppeteer", "VPEPuppeteer.Hediff_Puppeteer", "Puppeteer");
        BrainLeechDef = FromXml(vpe, "VPEP_BrainLeech", "VPEPuppeteer.Hediff_BrainLeech", "Brain leech");

        var biotech = XDocument.Load(Path.Combine(data, "Biotech_HediffDefs_Hediffs_Mechanitor.xml"));
        ControlSublinkDef = FromXml(biotech, "ControlSublinkImplant", "Hediff_Level", "control sublink");
        RemoteRepairerDef = FromXml(biotech, "RemoteRepairerImplant", "Hediff_Level", "remote repairer");
        MechFormfeederDef = FromXml(biotech, "MechFormfeederImplant", "Hediff_Level", "mech gestation processor");

        var ltsBio = XDocument.Load(Path.Combine(data, "LTSII_1.7_Mods_Biotech_HediffDefs_Hediffs_Mechanitor.xml"));
        WarprogrammerDef = FromXml(ltsBio, "WarprogrammerInterfaceImplant", "Hediff_Level", "warprogrammer interface");

        var ltsShoulder = XDocument.Load(Path.Combine(data, "LTSII_1.7_HediffDefs_Hediffs_ShoulderWeaponry.xml"));
        ShoulderTurretDef = FromXml(ltsShoulder, "LTS_ShoulderTurretActive", "HediffWithComps", "extended shoulder turret");

        var vre = XDocument.Load(Path.Combine(data, "VRE_Sanguophage_1.6_HediffDefs_Hediffs.xml"));
        SharpTalonsDef = FromXml(vre, "VRE_SharpTalons", "HediffWithComps", "sharp talons");

        var vreGenes = XDocument.Load(Path.Combine(data, "VRE_Sanguophage_1.6_GeneDefs_GeneDefs_Hands.xml"));
        var talonGene = vreGenes.Descendants("hediffsToBodyParts").Elements("li")
            .Single(x => (string)x.Element("hediff") == "VRE_SharpTalons");
        var talonParts = talonGene.Element("bodyparts").Elements("li").Select(e => e.Value.Trim()).ToList();
        Check("VRE_SharpTalons gene maps to both hands", talonParts.Count == 2 && talonParts.All(p => p == "Hand"));

        // ---- v1.2.0 additions: real XML, same exact defName/label/class/flag assertions ----
        RemoteShielderDef = FromXml(biotech, "RemoteShielderImplant", "Hediff_Level", "remote shielder");
        RepairProbeDef = FromXml(biotech, "RepairProbeImplant", "Hediff_Level", "repair probe");

        RemoteDominatorDef = FromXml(ltsBio, "RemoteDominatorImplant", "Hediff_Level", "remote dominator");
        RemoteResurrectorDef = FromXml(ltsBio, "RemoteResurrectorImplant", "Hediff_Level", "remote resurrector");
        SignalBoosterDef = FromXml(ltsBio, "SignalBoosterImplant", "Hediff_Level", "signal booster");
        MechwombDef = FromXml(ltsBio, "MechwombImplant", "Hediff_Level", "mechwomb");

        ShoulderBeamActiveDef = FromXml(ltsShoulder, "LTS_ShoulderBeamTurretActive", "HediffWithComps", "extended shoulder beam turret");
        ShoulderChargeActiveDef = FromXml(ltsShoulder, "LTS_ShoulderChargeTurretActive", "HediffWithComps", "extended shoulder charge turret");

        var vqeaHediffs = XDocument.Load(Path.Combine(data, "VQEA_1.6_Defs_HediffDefs_Hediffs_Genes.xml"));
        PlasteelClawsDef = FromXml(vqeaHediffs, "VQEA_PlasteelClaws", "HediffWithComps", "plasteel claw");

        var fungoidHediffs = XDocument.Load(Path.Combine(data, "VRE_Fungoid_1.7_Defs_HediffDefs_Hediffs_Attacks.xml"));
        InfectorHandsDef = FromXml(fungoidHediffs, "VRE_InfectorHands", "HediffWithComps", "infector hand");
        InfectorMouthDef = FromXml(fungoidHediffs, "VRE_InfectorMouth", "HediffWithComps", "infector mouth");

        var sauridHediffs = XDocument.Load(Path.Combine(data, "VRESaurids_1.6_Defs_HediffDefs_HediffDef_SauridClaws.xml"));
        SauridClawsDef = FromXml(sauridHediffs, "VRESaurids_SauridClaws", "HediffWithComps", "saurid claws");

        var odysseyHediffs = XDocument.Load(Path.Combine(data, "Odyssey_HediffDefs_Hediffs_BodyParts_Prosthetic.xml"));
        SentienceCatalystDef = FromXml(odysseyHediffs, "SentienceCatalyst", "Hediff_SentienceCatalyst", "sentience catalyst");

        var mag = XDocument.Load(Path.Combine(data, "MAG_1.6_Compat_Ideology_Defs_Ideology_Crafting.xml"));
        ArchonexusKeyOneDef = FromXml(mag, "MAG_ArchonexusCore_One", "HediffWithComps", "Archonexus key I");
        ArchonexusKeyTwoDef = FromXml(mag, "MAG_ArchonexusCore_Two", "HediffWithComps", "Archonexus key II");
        ArchonexusKeyThreeDef = FromXml(mag, "MAG_ArchonexusCore_Three", "HediffWithComps", "Archonexus key III");

        // ---- v1.2.1 additions: J resource farms from real WCE2 XML; K/L exclusions from real source ----
        var wceGrowths = XDocument.Load(Path.Combine(data, "WCE2_1.6_Defs_WCE2_Hediffs_WCE2_Local_Growths.xml"));
        var wceImplants = XDocument.Load(Path.Combine(data, "WCE2_1.6_Defs_WCE2_Hediffs_WCE2_Local_Implants.xml"));
        var wceTorsoRecipes = XDocument.Load(Path.Combine(data, "WCE2_1.6_Defs_WCE2_Recipes_WCE2_Recipes_Implants_Torso.xml"));
        var wceLowTechRecipes = XDocument.Load(Path.Combine(data, "WCE2_1.6_Defs_WCE2_Recipes_WCE2_Recipes_LowTech.xml"));
        var coreMisc = XDocument.Load(Path.Combine(data, "Core_Defs_HediffDefs_Hediffs_Local_Misc.xml"));
        var giverSets = XDocument.Load(Path.Combine(data, "Core_Defs_HediffGiverSetDefs_HediffGiverSets.xml"));

        NeutroamineIncubatorDef = FromXml(wceGrowths, "WCE2_NeutroamineIncubator", "Hediff_Implant", "neutroamine incubator", wceImplants);
        GrowthStimulatorDef = FromXml(wceGrowths, "WCE2_GrowthStimulator", "Hediff_Implant", "growth stimulator", wceImplants);
        TraumaSavantDef = FromXml(coreMisc, "TraumaSavant", "HediffWithComps", "trauma savant");
        SuturedEyeDef = FromXml(wceImplants, "WCE2_SuturedEye", "Hediff_Implant", "sutured eye", wceImplants);
        SuturedMouthDef = FromXml(wceImplants, "WCE2_SuturedMouth", "Hediff_Implant", "sutured mouth", wceImplants);

        // ---- runtime attachment evidence: the part comes from the install comp / VEF gene extension ----
        string InstallBodyPart(XDocument doc, string hediff) =>
            doc.Descendants("li")
               .Where(li => ((string)li.Attribute("Class") ?? "").Contains("InstallImplant"))
               .Where(li => (string)li.Element("hediffDef") == hediff)
               .Select(li => (string)li.Element("bodyPart"))
               .FirstOrDefault();

        List<string> GeneParts(XDocument doc, string hediff) =>
            doc.Descendants("hediffsToBodyParts").Elements("li")
               .Where(li => (string)li.Element("hediff") == hediff)
               .SelectMany(li => li.Element("bodyparts").Elements("li"))
               .Select(e => e.Value.Trim()).ToList();

        List<string> RecipeInstallParts(XDocument doc, string recipe) =>
            doc.Descendants("RecipeDef").Where(r => (string)r.Element("defName") == recipe)
               .SelectMany(r => r.Element("appliedOnFixedBodyParts")?.Elements("li") ?? Enumerable.Empty<XElement>())
               .Select(e => e.Value.Trim()).ToList();

        XElement WceDef(string defName) => wceGrowths.Root.Elements("HediffDef")
            .Single(e => (string)e.Element("defName") == defName);
        bool RecipeAddsHediff(XDocument doc, string recipe, string hediff) =>
            doc.Descendants("RecipeDef").Where(r => (string)r.Element("defName") == recipe)
               .Any(r => (string)r.Element("addsHediff") == hediff);
        bool RecipeConsumesOne(XDocument doc, string recipe, string thingDef) =>
            doc.Descendants("RecipeDef").Where(r => (string)r.Element("defName") == recipe)
               .SelectMany(r => r.Element("ingredients")?.Elements("li") ?? Enumerable.Empty<XElement>())
               .Any(li => (string)li.Element("filter")?.Element("thingDefs")?.Element("li") == thingDef
                          && (int?)li.Element("count") == 1);

        // J resource farms: installed from a consumed item, not a bodyPart on the HediffDef. The cure
        // path is RemoveHediff with no spawnThingOnRemoved refund, so protecting them preserves both
        // the implant and its adverse effects (the deathMtbDays timer) as the user accepted.
        Check("WCE2_NeutroamineIncubator install recipe mounts on Torso",
            RecipeInstallParts(wceTorsoRecipes, "WCE2_InstallNeutroamineIncubator").SequenceEqual(new[] { "Torso" }));
        Check("WCE2_NeutroamineIncubator install recipe adds the exact hediff",
            RecipeAddsHediff(wceTorsoRecipes, "WCE2_InstallNeutroamineIncubator", "WCE2_NeutroamineIncubator"));
        Check("WCE2_NeutroamineIncubator install recipe consumes the item x1",
            RecipeConsumesOne(wceTorsoRecipes, "WCE2_InstallNeutroamineIncubator", "WCE2_NeutroamineIncubator"));
        Check("WCE2_NeutroamineIncubator def has no bodyPart of its own (part comes from the install recipe)",
            WceDef("WCE2_NeutroamineIncubator").Element("bodyPart") == null);
        Check("WCE2_NeutroamineIncubator has no spawnThingOnRemoved (biosculpter cure never refunds the item)",
            WceDef("WCE2_NeutroamineIncubator").Element("spawnThingOnRemoved") == null);
        Check("WCE2_NeutroamineIncubator carries deathMtbDays 60 (preserving it preserves the lethal timer)",
            (int?)WceDef("WCE2_NeutroamineIncubator").Descendants("deathMtbDays").FirstOrDefault() == 60);

        Check("WCE2_GrowthStimulator install recipe mounts on Torso",
            RecipeInstallParts(wceTorsoRecipes, "WCE2_InstallGrowthStimulator").SequenceEqual(new[] { "Torso" }));
        Check("WCE2_GrowthStimulator install recipe adds the exact hediff",
            RecipeAddsHediff(wceTorsoRecipes, "WCE2_InstallGrowthStimulator", "WCE2_GrowthStimulator"));
        Check("WCE2_GrowthStimulator install recipe consumes the item x1",
            RecipeConsumesOne(wceTorsoRecipes, "WCE2_InstallGrowthStimulator", "WCE2_GrowthStimulator"));
        Check("WCE2_GrowthStimulator def has no bodyPart of its own (part comes from the install recipe)",
            WceDef("WCE2_GrowthStimulator").Element("bodyPart") == null);
        Check("WCE2_GrowthStimulator has no spawnThingOnRemoved (biosculpter cure never refunds the item)",
            WceDef("WCE2_GrowthStimulator").Element("spawnThingOnRemoved") == null);
        Check("WCE2_GrowthStimulator carries deathMtbDays 30 (preserving it preserves the lethal timer)",
            (int?)WceDef("WCE2_GrowthStimulator").Descendants("deathMtbDays").FirstOrDefault() == 30);

        // K/L evidence: deliberately excluded, and still removable by the cycle.
        Check("TraumaSavant is a vanilla brain-injury giver on Brain with chancePerDamagePct 0.12",
            giverSets.Descendants("HediffGiverSetDef").Single(e => (string)e.Element("defName") == "Human")
                .Descendants("li").Any(li => (string)li.Element("hediff") == "TraumaSavant"
                    && (string)li.Element("partsToAffect")?.Element("li") == "Brain"
                    && (double?)li.Element("chancePerDamagePct") == 0.12));
        Check("TraumaSavant def has no bodyPart of its own (runtime part is Brain via the giver)",
            coreMisc.Root.Elements("HediffDef").Single(e => (string)e.Element("defName") == "TraumaSavant").Element("bodyPart") == null);

        Check("WCE2_SutureEye install recipe mounts on Eye",
            RecipeInstallParts(wceLowTechRecipes, "WCE2_SutureEye").SequenceEqual(new[] { "Eye" }));
        Check("WCE2_SutureEye install recipe adds the exact hediff",
            RecipeAddsHediff(wceLowTechRecipes, "WCE2_SutureEye", "WCE2_SuturedEye"));
        Check("WCE2_SutureMouth install recipe mounts on Jaw",
            RecipeInstallParts(wceLowTechRecipes, "WCE2_SutureMouth").SequenceEqual(new[] { "Jaw" }));
        Check("WCE2_SutureMouth install recipe adds the exact hediff",
            RecipeAddsHediff(wceLowTechRecipes, "WCE2_SutureMouth", "WCE2_SuturedMouth"));
        Check("WCE2_SuturedEye has spawnThingOnRemoved=Cloth but the cure path does not spawn it",
            (string)wceImplants.Root.Elements("HediffDef").Single(e => (string)e.Element("defName") == "WCE2_SuturedEye").Element("spawnThingOnRemoved") == "Cloth");
        Check("WCE2_SuturedMouth has spawnThingOnRemoved=Cloth but the cure path does not spawn it",
            (string)wceImplants.Root.Elements("HediffDef").Single(e => (string)e.Element("defName") == "WCE2_SuturedMouth").Element("spawnThingOnRemoved") == "Cloth");

        var odysseyItems = XDocument.Load(Path.Combine(data, "Odyssey_ThingDefs_Items_Items_Exotic.xml"));
        Check("RemoteShielderImplant install comp attaches to Brain (Biotech XML)", InstallBodyPart(biotech, "RemoteShielderImplant") == "Brain");
        Check("RepairProbeImplant install comp attaches to Brain (Biotech XML)", InstallBodyPart(biotech, "RepairProbeImplant") == "Brain");
        Check("RemoteDominatorImplant install comp attaches to Brain (lts.i XML)", InstallBodyPart(ltsBio, "RemoteDominatorImplant") == "Brain");
        Check("RemoteResurrectorImplant install comp attaches to Brain (lts.i XML)", InstallBodyPart(ltsBio, "RemoteResurrectorImplant") == "Brain");
        Check("SignalBoosterImplant install comp attaches to Brain (lts.i XML)", InstallBodyPart(ltsBio, "SignalBoosterImplant") == "Brain");
        Check("MechwombImplant install comp attaches to Torso (lts.i XML)", InstallBodyPart(ltsBio, "MechwombImplant") == "Torso");
        Check("SentienceCatalyst install comp attaches to Brain (Odyssey item XML)", InstallBodyPart(odysseyItems, "SentienceCatalyst") == "Brain");
        foreach (var key in new[] { "MAG_ArchonexusCore_One", "MAG_ArchonexusCore_Two", "MAG_ArchonexusCore_Three" })
            Check($"{key} install comp attaches to Torso (MAG XML)", InstallBodyPart(mag, key) == "Torso");

        // The LTS active hediffs carry no bodyPart themselves; the installed turret recipes fix Torso.
        Check("LTS_ShoulderBeamTurretActive def has no bodyPart of its own (runtime part comes from the turret)",
            ltsShoulder.Root.Elements("HediffDef")
                .Single(e => (string)e.Element("defName") == "LTS_ShoulderBeamTurretActive").Element("bodyPart") == null);
        Check("LTS beam turret install recipe mounts on Torso",
            RecipeInstallParts(ltsShoulder, "LTS_InstallShoulderBeamTurret").SequenceEqual(new[] { "Torso" }));
        Check("LTS charge turret install recipe mounts on Torso",
            RecipeInstallParts(ltsShoulder, "LTS_InstallShoulderChargeTurret").SequenceEqual(new[] { "Torso" }));

        var vqeaGenes = XDocument.Load(Path.Combine(data, "VQEA_1.6_Defs_GeneDefs_GeneDefs_Archite.xml"));
        Check("VQEA_PlasteelClaws gene maps to both hands",
            GeneParts(vqeaGenes, "VQEA_PlasteelClaws").SequenceEqual(new[] { "Hand", "Hand" }));
        var fungoidGenes = XDocument.Load(Path.Combine(data, "VRE_Fungoid_1.7_Defs_GeneDefs_GeneDefs_Archite.xml"));
        Check("VRE_InfectorHands gene maps to both hands",
            GeneParts(fungoidGenes, "VRE_InfectorHands").SequenceEqual(new[] { "Hand", "Hand" }));
        Check("VRE_InfectorMouth gene maps to the jaw",
            GeneParts(fungoidGenes, "VRE_InfectorMouth").SequenceEqual(new[] { "Jaw" }));
        var sauridGenes = XDocument.Load(Path.Combine(data, "VRESaurids_1.6_Defs_GeneDefs_GeneDefs_Endogenes.xml"));
        Check("VRESaurids_SauridClaws gene maps to both hands",
            GeneParts(sauridGenes, "VRESaurids_SauridClaws").SequenceEqual(new[] { "Hand", "Hand" }));

        InjuryDef = new HediffDef { defName = "Cut", label = "cut", hediffClass = typeof(Hediff_Injury), injuryProps = new InjuryProps() };

        BrainDef = new BodyPartDef { defName = "Brain" };
        HeadDef = new BodyPartDef { defName = "Head" };
        FingerDef = new BodyPartDef { defName = "Finger" };
        TorsoDef = new BodyPartDef { defName = "Torso" };
        HandDef = new BodyPartDef { defName = "Hand" };
        JawDef = new BodyPartDef { defName = "Jaw" };
        EyeDef = new BodyPartDef { defName = "Eye" };
        PartDefs = new Dictionary<string, BodyPartDef>();
        PartDefs["Brain"] = BrainDef;
        PartDefs["Head"] = HeadDef;
        PartDefs["Finger"] = FingerDef;
        PartDefs["Torso"] = TorsoDef;
        PartDefs["Hand"] = HandDef;
        PartDefs["Jaw"] = JawDef;
        PartDefs["Eye"] = EyeDef;
        BodyPartDef Bp(string n) => PartDefs.TryGetValue(n, out var d) ? d : PartDefs[n] = new BodyPartDef { defName = n };

        var ideo = XDocument.Load(Path.Combine(data, "Ideology_ThingDefs_Buildings_Buildings_Ideo.xml"));
        var regenLi = ideo.Descendants("li").Single(li => (string)li.Element("key") == "bioregeneration");
        VanillaRestore = regenLi.Element("bodyPartsToRestore").Elements("li").Select(e => Bp(e.Value.Trim())).ToList();
        var ebPatch = XDocument.Load(Path.Combine(data, "EnhancedBioregeneration_1.7_Patches_Buildings_Ideo.xml"));
        var ebAdd = ebPatch.Descendants("li").Where(li => (string)li.Attribute("Class") == "PatchOperationAdd"
            && ((string)li.Element("xpath") ?? "").Contains("bodyPartsToRestore")).Single();
        EnhancedRestore = VanillaRestore.Concat(ebAdd.Element("value").Elements("li").Select(e => Bp(e.Value.Trim()))).ToList();
        Check("vanilla bioregeneration bodyPartsToRestore lacks Brain", !VanillaRestore.Contains(BrainDef));
        Check("EnhancedBioregeneration XML patch adds Brain to bodyPartsToRestore", EnhancedRestore.Contains(BrainDef));
        Check("EnhancedBioregeneration XML patch makes Torso restorable", EnhancedRestore.Contains(TorsoDef));
        Check("EnhancedBioregeneration XML patch makes Hand restorable", EnhancedRestore.Contains(HandDef));
        Check("EnhancedBioregeneration XML patch makes Jaw restorable", EnhancedRestore.Contains(JawDef));
        Check("EnhancedBioregeneration XML patch makes Shoulder restorable",
            EnhancedRestore.Any(d => d.defName == "Shoulder"));
        Check("vanilla restore list does not cover the new brain/torso/hand/jaw parts",
            !VanillaRestore.Contains(BrainDef) && !VanillaRestore.Contains(TorsoDef)
            && !VanillaRestore.Contains(HandDef) && !VanillaRestore.Contains(JawDef));
        Console.WriteLine("vanilla bodyPartsToRestore: " + string.Join(",", VanillaRestore.Select(d => d.defName)));
        Console.WriteLine("with EB patch: " + string.Join(",", EnhancedRestore.Select(d => d.defName)));
    }

    // ---------------- fixtures ----------------
    static MethodInfo CycleCompletedMethod => AccessTools.Method(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.CycleCompleted));
    static MethodInfo DescriptionMethod => AccessTools.Method(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.GetHealingDescriptionForPawn));
    static MethodInfo CureMethod => AccessTools.Method(typeof(HealthUtility), nameof(HealthUtility.Cure), new[] { typeof(Hediff) });
    static MethodInfo VanillaWillHealMethod => AccessTools.Method(typeof(CompBiosculpterPod_HealingCycle), "WillHeal", new[] { typeof(Pawn), typeof(Hediff) });
    static MethodInfo EbWillHealMethod => AccessTools.Method(typeof(HealingCycle_Utils), "WillHeal", new[]
    {
        typeof(Pawn), typeof(Hediff), typeof(bool), typeof(CompProperties_BiosculpterPod_HealingCycle)
    });

    // The installed EB XML patch is always loaded with the mod, so Brain/Hand/Torso are restorable on both paths.
    static CompBiosculpterPod_HealingCycle Regen() => MakeCycle(new CompBiosculpterPod_RegenerationCycle(), EnhancedRestore);
    static CompBiosculpterPod_HealingCycle Medic() => MakeCycle(new CompBiosculpterPod_MedicCycle(), null);

    static CompBiosculpterPod_HealingCycle MakeCycle(CompBiosculpterPod_HealingCycle comp, List<BodyPartDef> restore)
    {
        comp.props = new CompProperties_BiosculpterPod_HealingCycle
        {
            bodyPartsToRestore = restore,
            conditionsToPossiblyCure = new List<HediffDef>(),
        };
        return comp;
    }

    static void SetEb(bool userSettings)
    {
        // Installed user config: KeepMinorScars=True (others default false). Any true flag makes EB replace CycleCompleted.
        BattIePatchEnhancedBioregenerationSettings.LessenedRegeneration = false;
        BattIePatchEnhancedBioregenerationSettings.HealAllConditions = false;
        BattIePatchEnhancedBioregenerationSettings.KeepAllScars = false;
        BattIePatchEnhancedBioregenerationSettings.KeepMinorScars = userSettings;
    }

    static void RunCycle(CompBiosculpterPod_HealingCycle cycle, Pawn pawn) => cycle.CycleCompleted(pawn);

    static (Pawn, Hediff keep, Hediff leech, Hediff injury) MakePawn(HediffDef keepDef, bool allProtections = false)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        var health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(health, pawn);
        health.hediffSet = new HediffSet(pawn);
        pawn.health = health;

        var head = new BodyPartRecord { def = HeadDef };
        var brain = new BodyPartRecord { def = BrainDef, parent = head };
        var torso = new BodyPartRecord { def = TorsoDef };
        var hand = new BodyPartRecord { def = HandDef };
        var finger = new BodyPartRecord { def = FingerDef };
        var jaw = new BodyPartRecord { def = JawDef };
        var eye = new BodyPartRecord { def = EyeDef, parent = head };

        BodyPartRecord Part(HediffDef def)
        {
            string name = PartName(def);
            if (name == "Hand") return hand;
            if (name == "Torso") return torso;
            if (name == "Jaw") return jaw;
            if (name == "Eye") return eye;
            return brain;
        }

        Hediff Add(HediffDef def, BodyPartRecord part)
        {
            var h = (Hediff)Activator.CreateInstance(def.hediffClass);
            h.def = def;
            h.pawn = pawn;
            h.Part = part;
            health.hediffSet.hediffs.Add(h);
            return h;
        }
        var keep = Add(keepDef, Part(keepDef));
        if (allProtections)
        {
            foreach (var def in AllProtectedDefs())
            {
                if (def != keepDef) Add(def, Part(def));
            }
        }
        var leech = Add(BrainLeechDef, brain);
        var injury = Add(InjuryDef, finger);
        return (pawn, keep, leech, injury);
    }

    static bool Has(Pawn p, Hediff h) => p.health.hediffSet.hediffs.Contains(h);

    // ---------------- environment stubs (test-only) ----------------
    internal static Action<Hediff> CureSpy;

    static void InstallEnvironmentStubs()
    {
        var h = new Harmony("test.environment");
        var skip = new HarmonyMethod(typeof(Program), nameof(SkipVoid));
        h.Patch(AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)), skip);
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.CheckForStateChange)), skip);
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged)), skip);
        // Hediff_Injury.PostRemoved only refreshes wound render overlays (runs after list removal).
        h.Patch(AccessTools.Method(typeof(Hediff_Injury), nameof(Hediff_Injury.PostRemoved)), skip);
        foreach (var m in typeof(Messages).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Message"))
            h.Patch(m, skip);
        // Translation needs the language database of a running game.
        foreach (var m in typeof(TranslatorFormattedStringExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "Translate" && m.ReturnType == typeof(TaggedString) && !m.IsGenericMethod))
            h.Patch(m, new HarmonyMethod(typeof(Program), nameof(FakeTranslate)));
        // Verse.Log -> Brutal.Logging needs the game's logging backend; capture text instead.
        foreach (var m in typeof(Log).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => (m.Name == "Message" || m.Name == "Warning" || m.Name == "Error") && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)))
            h.Patch(m, new HarmonyMethod(typeof(Program), nameof(CaptureLog)));
        // Graphics flags in RemoveHediff read def graphic data; plain defs have none.
        // Spy runs AFTER the mod's prefix (lower priority) but only when Cure body will run.
        h.Patch(CureMethod, new HarmonyMethod(AccessTools.Method(typeof(Program), nameof(CureSpyPrefix))) { priority = Priority.Last });
    }

    static bool SkipVoid() => false;
    internal static readonly List<string> GameLog = new List<string>();
    static bool CaptureLog(string text) { GameLog.Add(text); return false; }
    static bool FakeTranslate(ref TaggedString __result) { __result = new TaggedString("x"); return false; }
    static void CureSpyPrefix(Hediff hediff) => CureSpy?.Invoke(hediff);

    // ---------------- assertions ----------------
    static void Check(string name, bool ok)
    {
        if (ok) { passed++; Console.WriteLine("  PASS " + name); }
        else Fail(name, null);
    }
    static void Fail(string name, string detail)
    {
        failed++; Failures.Add(name);
        Console.WriteLine("  FAIL " + name + (detail != null ? "\n" + detail : ""));
    }
}