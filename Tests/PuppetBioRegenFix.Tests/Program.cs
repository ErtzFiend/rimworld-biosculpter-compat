// Regression tests for Puppet Bio-Regeneration Fix.
//
// Runs the REAL vanilla CompBiosculpterPod_HealingCycle.CycleCompleted (Assembly-CSharp 1.7.5005)
// and the REAL BattIePatch: Enhanced Bioregeneration Harmony prefix, with the mod's Harmony
// patches registered through its actual [StaticConstructorOnStartup] class.
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
using PuppetBioRegenFix;
using RimWorld;
using Verse;

internal static class Program
{
    static string Libs;
    static int passed, failed;
    static readonly List<string> Failures = new List<string>();

    // Real data read from installed mod/game XML (copied outside the repo).
    static HediffDef PuppetDef, PuppeteerDef, BrainLeechDef, InjuryDef;
    static BodyPartDef BrainDef, HeadDef, FingerDef;
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

        // ---------- BASELINE: fix NOT installed -> the bug reproduces ----------
        Console.WriteLine("\n== Baseline (fix not loaded) ==");
        SetEb(false);
        {
            var (pawn, puppet, _, injury) = MakePawn(PuppetDef);
            RunCycle(MakeCycle(new CompBiosculpterPod_RegenerationCycle(), VanillaRestore), pawn);
            Check("baseline/pure vanilla restore list (no EB XML): VPEP_Puppet survives, injury healed (root cause is Brain in list)",
                Has(pawn, puppet) && !Has(pawn, injury));
        }
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB prefix path" : "vanilla CycleCompleted + EB restore list";
            var (pawn, puppet, _, _) = MakePawn(PuppetDef);
            RunCycle(Regen(), pawn);
            Check($"baseline/{path}: VPEP_Puppet IS removed without fix (bug reproduces)", !Has(pawn, puppet));
            var (m, master, _, _) = MakePawn(PuppeteerDef);
            RunCycle(Regen(), m);
            Check($"baseline/{path}: VPEP_Puppeteer IS removed without fix (bug reproduces)", !Has(m, master));
        }
        Check("PuppetBioRegenFix patches not present before startup",
            Harmony.GetPatchInfo(CureMethod)?.Prefixes.Any(p => p.owner == PuppetBioRegenFixStartup.HarmonyId) != true);

        // ---------- Install the fix exactly as the game does ----------
        RuntimeHelpers.RunClassConstructor(typeof(PuppetBioRegenFixStartup).TypeHandle);
        Console.WriteLine("\n== Harmony registration ==");
        Check("startup logged its banner via Verse.Log", GameLog.Any(l => l.StartsWith("[Puppet Bio-Regeneration Fix 1.0.0]")));
        var cc = Harmony.GetPatchInfo(CycleCompletedMethod);
        Check("CycleCompleted prefix from PuppetBioRegenFix",
            cc.Prefixes.Any(p => p.owner == PuppetBioRegenFixStartup.HarmonyId && p.PatchMethod.DeclaringType == typeof(Patch_HealingCycle_CycleCompleted)));
        Check("CycleCompleted finalizer from PuppetBioRegenFix",
            cc.Finalizers.Any(p => p.owner == PuppetBioRegenFixStartup.HarmonyId));
        Check("PuppetBioRegenFix CycleCompleted prefix runs before EB's prefix (priority First)",
            cc.Prefixes.First(p => p.owner == PuppetBioRegenFixStartup.HarmonyId).priority > cc.Prefixes.First(p => p.owner.StartsWith("test.realmod")).priority);
        var cure = Harmony.GetPatchInfo(CureMethod);
        Check("HealthUtility.Cure(Hediff) prefix from PuppetBioRegenFix",
            cure.Prefixes.Any(p => p.owner == PuppetBioRegenFixStartup.HarmonyId && p.PatchMethod.DeclaringType == typeof(Patch_HealthUtility_Cure)));
        Check("Exactly 2 methods patched by PuppetBioRegenFix",
            Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(PuppetBioRegenFixStartup.HarmonyId)) == 2);

        // ---------- Exact def checks ----------
        Console.WriteLine("\n== Exact defName matching ==");
        Check("VPEP_Puppet preserved-def", PreservedHediffs.IsPreservedDef(PuppetDef));
        Check("VPEP_Puppeteer preserved-def", PreservedHediffs.IsPreservedDef(PuppeteerDef));
        Check("VPEP_BrainLeech NOT preserved", !PreservedHediffs.IsPreservedDef(BrainLeechDef));
        foreach (var n in new[] { "vpep_puppet", "VPEP_Puppet ", "VPEP_PuppetX", "VPEP_Puppeteers", "Puppet", "Puppeteer", "VPEP_Puppet_Master_Death", "" })
            Check($"'{n}' NOT preserved (exact, case-sensitive)", !PreservedHediffs.IsPreservedDef(new HediffDef { defName = n }));
        Check("null def NOT preserved", !PreservedHediffs.IsPreservedDef(null));

        // ---------- Behaviour with fix, both cycle implementations ----------
        foreach (bool eb in new[] { false, true })
        {
            SetEb(eb);
            string path = eb ? "EB prefix replaces CycleCompleted (user setting KeepMinorScars=True)" : "vanilla CycleCompleted body + EB restore list (all EB toggles off)";
            Console.WriteLine($"\n== Bioregeneration with fix: {path} ==");
            foreach (var def in new[] { PuppetDef, PuppeteerDef })
            {
                var (pawn, keep, leech, injury) = MakePawn(def);
                RunCycle(Regen(), pawn);
                Check($"{def.defName} preserved through bioregeneration", Has(pawn, keep));
                Check($"{def.defName}: same hediff instance, still on Brain", Has(pawn, keep) && keep.Part.def == BrainDef && keep.pawn == pawn);
                Check($"{def.defName}: fresh finger injury still healed (normal healing unaffected)", !Has(pawn, injury));
                Check($"{def.defName}: other brain hediff VPEP_BrainLeech still removed (narrow scope)", !Has(pawn, leech));
                Check($"{def.defName}: scope closed after cycle", BioregenerationScope.Current == null);
            }
        }

        // ---------- Outside the cycle: removal unaffected ----------
        Console.WriteLine("\n== Outside the bioregeneration cycle ==");
        foreach (var def in new[] { PuppetDef, PuppeteerDef })
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
            CureSpy = h => { sawCall = true; seen = BioregenerationScope.Current; };
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
            Check("scope cleared after exception (finalizer)", BioregenerationScope.Current == null);
            var (p2, h2, _, _) = MakePawn(PuppetDef);
            HealthUtility.Cure(h2);
            Check("after exception, removal outside cycle still works", !Has(p2, h2));
        }

        // Unpatching restores original behaviour (patch is what makes the difference).
        new Harmony(PuppetBioRegenFixStartup.HarmonyId).UnpatchAll(PuppetBioRegenFixStartup.HarmonyId);
        SetEb(true);
        {
            var (pawn, puppet, _, _) = MakePawn(PuppetDef);
            RunCycle(Regen(), pawn);
            Check("after UnpatchAll(fix): VPEP_Puppet removed again (fix is the cause)", !Has(pawn, puppet));
        }
    }

    // ---------------- real data ----------------
    static void LoadRealData()
    {
        string data = Path.Combine(Libs, "data");
        var hediffXml = XDocument.Load(Path.Combine(data, "VPEPuppeteer_1.6_HediffDefs_Hediffs.xml"));
        HediffDef FromXml(string defName, string expectedClass)
        {
            var el = hediffXml.Root.Elements("HediffDef").Single(e => (string)e.Element("defName") == defName);
            string cls = (string)el.Element("hediffClass");
            Check($"installed XML defines {defName} with hediffClass {expectedClass}", cls == expectedClass);
            bool everCurable = el.Element("everCurableByItem") == null || bool.Parse((string)el.Element("everCurableByItem"));
            Check($"{defName}: everCurableByItem is default true (so bioregeneration WillHeal can cure it)", everCurable);
            return new HediffDef { defName = defName, label = (string)el.Element("label"), hediffClass = typeof(HediffWithComps), everCurableByItem = everCurable };
        }
        PuppetDef = FromXml("VPEP_Puppet", "VPEPuppeteer.Hediff_Puppet");
        PuppeteerDef = FromXml("VPEP_Puppeteer", "VPEPuppeteer.Hediff_Puppeteer");
        BrainLeechDef = FromXml("VPEP_BrainLeech", "VPEPuppeteer.Hediff_BrainLeech");
        InjuryDef = new HediffDef { defName = "Cut", label = "cut", hediffClass = typeof(Hediff_Injury), injuryProps = new InjuryProps() };

        BrainDef = new BodyPartDef { defName = "Brain" };
        HeadDef = new BodyPartDef { defName = "Head" };
        FingerDef = new BodyPartDef { defName = "Finger" };
        var defs = new Dictionary<string, BodyPartDef> { ["Brain"] = BrainDef, ["Head"] = HeadDef, ["Finger"] = FingerDef };
        BodyPartDef Bp(string n) => defs.TryGetValue(n, out var d) ? d : defs[n] = new BodyPartDef { defName = n };

        var ideo = XDocument.Load(Path.Combine(data, "Ideology_ThingDefs_Buildings_Buildings_Ideo.xml"));
        var regenLi = ideo.Descendants("li").Single(li => (string)li.Element("key") == "bioregeneration");
        VanillaRestore = regenLi.Element("bodyPartsToRestore").Elements("li").Select(e => Bp(e.Value.Trim())).ToList();
        var ebPatch = XDocument.Load(Path.Combine(data, "EnhancedBioregeneration_1.7_Patches_Buildings_Ideo.xml"));
        var ebAdd = ebPatch.Descendants("li").Where(li => (string)li.Attribute("Class") == "PatchOperationAdd"
            && ((string)li.Element("xpath") ?? "").Contains("bodyPartsToRestore")).Single();
        EnhancedRestore = VanillaRestore.Concat(ebAdd.Element("value").Elements("li").Select(e => Bp(e.Value.Trim()))).ToList();
        Check("vanilla bioregeneration bodyPartsToRestore lacks Brain", !VanillaRestore.Contains(BrainDef));
        Check("EnhancedBioregeneration XML patch adds Brain to bodyPartsToRestore", EnhancedRestore.Contains(BrainDef));
        Console.WriteLine("vanilla bodyPartsToRestore: " + string.Join(",", VanillaRestore.Select(d => d.defName)));
        Console.WriteLine("with EB patch: " + string.Join(",", EnhancedRestore.Select(d => d.defName)));
    }

    // ---------------- fixtures ----------------
    static MethodInfo CycleCompletedMethod => AccessTools.Method(typeof(CompBiosculpterPod_HealingCycle), nameof(CompBiosculpterPod_HealingCycle.CycleCompleted));
    static MethodInfo CureMethod => AccessTools.Method(typeof(HealthUtility), nameof(HealthUtility.Cure), new[] { typeof(Hediff) });

    // The installed EB XML patch is always loaded with the mod, so Brain is restorable on both paths.
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

    static (Pawn, Hediff keep, Hediff leech, Hediff injury) MakePawn(HediffDef keepDef)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        var health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").SetValue(health, pawn);
        health.hediffSet = new HediffSet(pawn);
        pawn.health = health;

        var head = new BodyPartRecord { def = HeadDef };
        var brain = new BodyPartRecord { def = BrainDef, parent = head };
        var finger = new BodyPartRecord { def = FingerDef };

        Hediff Add(HediffDef def, BodyPartRecord part)
        {
            var h = (Hediff)Activator.CreateInstance(def.hediffClass);
            h.def = def;
            h.pawn = pawn;
            h.Part = part;
            health.hediffSet.hediffs.Add(h);
            return h;
        }
        var keep = Add(keepDef, brain);
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
        // Spy runs AFTER the fix's prefix (lower priority) but only when Cure body will run.
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
