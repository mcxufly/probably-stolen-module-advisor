using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ModuleAdvisor;

static class LayoutChecks
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static TestItem Item(string id, int p, int e, int q)
    {
        var item = new TestItem(id);
        item.modifiedShape = new GridShapeBuilder(1, 1).SetDataFill(1);
        Set(item, ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT, p);
        Set(item, ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT, e);
        Set(item, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT, q);
        Set(item, ModuleHelper.MODULE_TAG, 0);
        item.itemTypes.Add("MODULE");
        return item;
    }
    private static void Set(GameItem item, string key, int value) =>
        item.modifiedState.dict[key] = new TagState(key).Enable().SetInt(value);

    private static GameGridInventory Grid(params GameItem[] items)
    {
        // Only for testing the native pure adjacency method; deliberately no Unity rendering tree.
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
        typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, items.ToList());
        typeof(GameGridInventory).GetField("<inventoryShape>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grid, new GridShapeBuilder(7, 5));
        return grid;
    }

    internal static void Run()
    {
        var random = new Random(167);
        for (int trial = 0; trial < 500; trial++)
        {
            string bitsA = string.Concat(Enumerable.Range(0, 6).Select(_ => random.Next(2).ToString()));
            string bitsB = string.Concat(Enumerable.Range(0, 6).Select(_ => random.Next(2).ToString()));
            var a = Item("a", 0, 0, 0);
            var b = Item("b", 50, -10, 20);
            a.modifiedShape = new GridShapeBuilder().SetData(bitsA, 3).SetTransform(random.Next(4), random.Next(3), random.Next(2) == 1, random.Next(4));
            b.modifiedShape = new GridShapeBuilder().SetData(bitsB, 3).SetTransform(random.Next(4), random.Next(3), random.Next(2) == 1, random.Next(4));
            var grid = Grid(a, b);
            bool adjacent = grid.IsAdjacent(new SlotMarker {item = a}, new SlotMarker {item = b});
            var la = GameAdapter.ToLayout(a, a.modifiedShape, 7);
            var lb = GameAdapter.ToLayout(b, b.modifiedShape, 7);
            la.Effects = new[] {"NODE_EFFECT_SUPPORT"};
            int[][] simulated = LayoutSimulation.Compute(7, new[] {la, lb});
            Check(simulated[0].SequenceEqual(adjacent ? new[] {10, -2, 4} : new[] {0, 0, 0}),
                "Simulation disagrees with native GameGridInventory.IsAdjacent at trial " + trial);
        }
        Console.WriteLine("500 irregular/rotated native adjacency differential checks passed.");

        var machine = Item("security_alarm", 0, 0, 0);
        Set(machine, "MACHINERY_DRAWN_POWER_BASE", 20);
        var existing = Item("receiver", 20, 0, 0);
        existing.modifiedState.dict[ModuleEffectHelper.MODULE_EFFECT1_TAG] =
            new TagState(ModuleEffectHelper.MODULE_EFFECT1_TAG).Enable().SetString(ModuleEffectHelper.MODULE_EFFECT_RECEIVER);
        var node = Item("node", 0, 0, 0);
        node.itemTypes.Clear(); node.itemTypes.Add("NODE");
        Set(node, "NODE", 0);
        node.modifiedState.dict[ModuleEffectHelper.MODULE_EFFECT1_TAG] =
            new TagState(ModuleEffectHelper.MODULE_EFFECT1_TAG).Enable().SetString(ModuleEffectHelper.NODE_EFFECT_SUPPORT);
        Check(!GameAdapter.ManualOnly(node), "Nodes are excluded from automatic planning.");
        var option = new Placement {Module = 0, Bay = 0, Cells = new[] {1}, X = 1, Y = 0};
        int dictCount = existing.modifiedState.dict.Count;
        var evaluator = new GameEvaluation(new[] {new Bay {Machine = machine, Grid = Grid(existing)}}, new[] {node}, new[] {option}, 0);
        var prediction = evaluator.Predict(0, new[] {option});
        Check(prediction.Before[0] == 20 && prediction.After[0] == 26, "Whole-bay receiver/support integration mismatch.");
        Check(evaluator.Evaluate(new[] {option}) > evaluator.Evaluate(Array.Empty<Placement>()), "Node gain did not enter objective.");
        Check(existing.modifiedState.dict.Count == dictCount, "Evaluation mutated installed item tags.");
        Check(GameAdapter.IntTag(existing, "absent") == 0 && existing.modifiedState.dict.Count == dictCount, "Model read created tag.");

        var model = new MachineModel {Id = "security_alarm", BasePower = 20};
        Check(model.Measure(new[] {-100,0,0}).Performance == 1, "Alarm extreme penalty must retain native 1% roll.");
        Check(model.Measure(new[] {49,0,0}).Performance == 100, "Alarm cap boundary wrong.");
        Check(model.Measure(new[] {200,0,0}).Score(0) == model.Measure(new[] {49,0,0}).Score(0), "Alarm overcap gains score.");
        Check(model.Measure(new[] {0,1000,0}).Power == 1, "Native power floor missing.");
        Check(model.Measure(new[] {0,95,0}).Score(0) == model.Measure(new[] {0,200,0}).Score(0), "Power above floor still scores.");
        model = new MachineModel {Id = "water_purifier", BasePower = 20};
        int[] thresholds = {-1,14,15,24,25,49,50,74,75,99,100,199,200,400};
        double[] caps = {99,99,99.15,99.15,99.35,99.35,99.65,99.65,99.8,99.8,99.9,99.9,100,100};
        for (int i = 0; i < thresholds.Length; i++)
            Check(model.Measure(new[] {0,0,thresholds[i]}).Quality == caps[i], "Purifier threshold mismatch.");
        model = new MachineModel {Id = "plant_dna_manipulator", QualityRatio = 2, DnaBaseChance = 75};
        for (int q = -220; q <= 220; q++)
            Check(model.Measure(new[] {0,0,q}).Quality == PlantGeneticsHelper.GetSuccessChance(4, q / 2), "Native DNA chance mismatch.");
        model = new MachineModel {Id = "furnace", BasePower = 20, FurnaceInputQuality = 3};
        Check(model.Measure(new[] {0,0,200}).Quality == 5 && model.Measure(new[] {0,0,400}).Quality == 5, "Ingot cap missing.");
        model.BlastFurnace = true;
        Check(model.Measure(new[] {0,0,200}).Quality == 4, "Blast purity penalty missing.");
        model.Flux = true;
        Check(model.Measure(new[] {0,0,-300}).Quality == 4, "Flux floor must apply before input quality addition.");
        model = new MachineModel {Id = "moisture_farm"};
        Check(model.Measure(new[] {0,0,149}).Quality == 4 && model.Measure(new[] {0,0,150}).Quality == 5, "Water tier threshold wrong.");
        Check(model.Measure(new[] {0,0,300}).Quality == 5, "Water cap missing.");
        model = new MachineModel {Id = "evaporator"};
        Check(model.Measure(new[] {0,0,200}).Quality == 100 && model.Measure(new[] {0,0,-30}).Quality == 0, "Recovery clamp wrong.");
        model = new MachineModel {Id = "hydroponic", QualityRatio = 2, HydroBaseHarvest = 4};
        Check(model.Measure(new[] {0,0,25}).Quality == 4 && model.Measure(new[] {0,0,26}).Quality == 5, "Harvest round-away boundary wrong.");
        model.HydroIgnoresQuality = true;
        Check(model.Measure(new[] {0,0,400}).Utility[2] == 0, "Hydroreed incorrectly benefits from quality.");
        model = new MachineModel {Id = "printer", QualityRatio = 4};
        Check(model.Measure(new[] {0,0,800}).Quality == 200, "Invented universal printer cap.");
        Console.WriteLine("Whole-bay adapter integration and native DNA/model cap thresholds passed.");
    }
}
