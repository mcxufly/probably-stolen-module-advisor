using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ModuleAdvisor;

// The adapter uses these only for localized UI; this tool never starts Unity.
namespace ModuleAdvisor { internal static class L { internal static string Text(string k, params object[] a) => k; } }

sealed class TestItem : GameItem
{
    public TestItem(string id) : base(id) { }
    public override void Destroy() => throw new InvalidOperationException("Verification must not destroy items.");
    public override void Validate() => throw new InvalidOperationException("Verification must not validate live state.");
}

static class Program
{
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    static void Main(string[] args)
    {
        Check(args.Length == 1, "Pass the installed PlayerSettings.bundleVersion.");
        var module = new TestItem("test");
        var p = new TagState(ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT).Enable().SetInt(16);
        var e = new TagState(ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT).Enable().SetInt(-8);
        var q = new TagState(ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT).Enable().SetInt(20);
        module.modifiedState.dict.Add(ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT, p);
        module.modifiedState.dict.Add(ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT, e);
        module.modifiedState.dict.Add(ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT, q);
        int before = module.modifiedState.dict.Count;
        Check(GameAdapter.Effects(module).All(x => x == null), "Absent effects must remain absent.");
        var shape = (GridShape)new GridShapeBuilder(2, 2).SetDataFill(1).SetTransform(0, 0, false, 0);
        Check(GameAdapter.EstimatedStats(module, shape).SequenceEqual(new[] {16, -8, 20}), "Base stats mismatch.");
        Check(module.modifiedState.dict.Count == before, "Reading preview added tags.");
        var missing = new TestItem("missing");
        GameAdapter.EstimatedStats(missing, shape);
        Check(missing.modifiedState.dict.Count == 0, "Missing-stat preview added tags.");
        module.modifiedState.dict.Add(ModuleEffectHelper.MODULE_EFFECT1_TAG,
            new TagState(ModuleEffectHelper.MODULE_EFFECT1_TAG).Enable().SetString(ModuleEffectHelper.MODULE_EFFECT_TOP_MOUNT));
        Check(GameAdapter.EstimatedStats(module, shape).SequenceEqual(new[] {19, -9, 24}), "Top mount rounding mismatch.");
        shape = new GridShapeBuilder(shape).SetPosition(1, 1);
        Check(GameAdapter.EstimatedStats(module, shape).SequenceEqual(new[] {16, -8, 20}), "Top mount active away from top.");
        var furnace = new Bay { Machine = new TestItem("furnace") };
        var alarm = new Bay { Machine = new TestItem("security_alarm") };
        Check(GameAdapter.Score(module, furnace, shape, 0) == 12, "Furnace must ignore performance.");
        Check(GameAdapter.Score(module, alarm, shape, 0) == 8, "Alarm must ignore quality.");
        Check(GameAdapter.Score(module, new Bay {Machine = new TestItem("unknown")}, shape, 0) == 0,
            "Unknown machine should not receive a numeric score.");
        Console.WriteLine("Real GameItem/TagSystem adapter checks passed; no preview tag mutation.");

        var bayShape = (GridShape)new GridShapeBuilder().SetData(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, 7);
        int free = 0;
        for (int y = 0; y < bayShape.height; y++)
            for (int x = 0; x < bayShape.width; x++) if (bayShape.Get(x, y) == 0) free++;
        Check(free == 26 && bayShape.width == 7 && bayShape.height == 5, "Unexpected native bay geometry.");
        var testShape = (GridShape)new GridShapeBuilder().SetData("1101", 2);
        var original = testShape.Clone();
        int cases = 0;
        for (int flip = 0; flip < 2; flip++)
            for (int rotation = 0; rotation < 4; rotation++)
            {
                var transformed = (GridShape)new GridShapeBuilder(testShape).SetTransform(2, 1, flip != 0, rotation);
                int cells = 0;
                for (int y = transformed.minY; y <= transformed.maxY; y++)
                    for (int x = transformed.minX; x <= transformed.maxX; x++) if (transformed.Get(x, y) == 1) cells++;
                Check(cells == 3, "Native rotation did not conserve occupied cells.");
                cases++;
            }
        Check(testShape.minX == original.minX && testShape.minY == original.minY && testShape.orientation == original.orientation,
            "Preview shape mutated source.");
        Console.WriteLine($"Real GridShape checks passed: default bay {free} free cells, {cases} rotation/flip cases.");

        LayoutChecks.Run();
        TemporalChecks.Run();
        string folder = Path.GetFullPath("dist/ModuleAdvisor");
        var manifest = ModLoader.ParseManifestForTools(folder, out string problem);
        Check(manifest != null && problem == null, "Manifest invalid: " + problem);
        var normalize = typeof(ModUIManager).GetMethod("NormalizeVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Normalize(string value) => (string)normalize.Invoke(null, new object[] {value})!;
        string actual = Normalize(args[0]);
        Check(actual.Length > 0 && manifest.GameVersions.Any(v => {
            string declared = Normalize(v);
            return declared.Length > 0 && (declared == actual || declared.StartsWith(actual + ".") || actual.StartsWith(declared + "."));
        }), "Manifest does not match product version.");
        // Scanner needs the full installed game's allow-list, not just the tool's dependencies.
        string game = Environment.GetEnvironmentVariable("GAME_MANAGED_DIR")
            ?? "/data/SteamLibrary/steamapps/common/Probably Stolen/Probably Stolen_Data/Managed";
        var allowed = System.IO.Directory.GetFiles(game, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray();
        string dll = Path.Combine(folder, "ProbablyStolenModuleAdvisor.dll");
        using (var metadata = DseCecil.AssemblyDefinition.ReadAssembly(dll))
        {
            var references = metadata.MainModule.GetMemberReferences().OfType<DseCecil.MethodReference>().ToArray();
            string[] mutations = {"TryAcceptOnce", "AcceptUnchecked", "UncheckedAccept", "Expel", "SetModuleStuck",
                "ComputeAllModuleTempStat", "ComputeModuleEffect", "TryFindOneValidInventorySlot", "GetUniqueID", "GetTagReadonly"};
            Check(!references.Any(r => mutations.Contains(r.Name)), "Preview contains an unexpected mutable game API.");
            Check(!references.Any(r => r.DeclaringType.Name == "CustomUIBuilder" && r.Name == "GetWindow"),
                "Diagram must not use the builder getter that hides the live window.");
            var captureRow = metadata.MainModule.Types.Single(t => t.Name == "HotkeyConfigRow");
            Check(captureRow.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch"), "Missing scoped keybind row patch.");
            Check(references.Any(r => r.DeclaringType.Name == "KeyBindHelper" && r.Name == "PollCapture"), "Missing native key capture.");
            Check(references.Any(r => r.DeclaringType.Name == "KeyBindHelper" && r.Name == "StopCapture"), "Missing native capture cleanup.");
            Check(references.Any(r => r.DeclaringType.Name == "CustomUIElement" && r.Name == "SetString"), "Captured binding must update the native Apply/Reset field.");
        }
        var scan = ModScanner.Scan(new[] {dll}, allowed, folder, false);
        Console.WriteLine("Manifest and compatibility passed: " + manifest.ID + " / " + manifest.ModVersion + " / " + args[0]);
        Console.WriteLine("Scanner: " + scan.MethodsScanned + " methods; capabilities=" + string.Join(",", scan.Capabilities));
        Console.WriteLine(scan.FormatFindings(100));
        Check(scan.Error == null && !scan.Refused && scan.Capabilities.Count == 0, "Native scanner did not pass: " + scan.Error);
        Console.WriteLine("Native scanner passed with no capabilities or hard flags. Unity runtime UI is not exercised by this tool.");
    }
}
