using System;
using System.Collections.Generic;
using System.Linq;

namespace ModuleAdvisor
{
    internal sealed class BayPrediction
    {
        internal int[] Before, After;
        internal int[][] ItemStats;
        internal List<LayoutItem> Items;
        internal MachineMetrics BeforeMetrics, AfterMetrics;
        internal int ExistingCount;
        internal MachineModel Model;
    }

    internal sealed class GameEvaluation
    {
        private readonly IReadOnlyList<Bay> bays;
        private readonly List<LayoutItem>[] existing;
        private readonly bool[][] counted;
        private readonly Dictionary<Placement, LayoutItem> candidates = new Dictionary<Placement, LayoutItem>();
        private readonly MachineModel[] models;
        private readonly int preference;
        private readonly double[] baseline;

        internal GameEvaluation(IReadOnlyList<Bay> bays, IReadOnlyList<GameItem> modules,
            IReadOnlyList<Placement> options, int preference)
        {
            this.bays = bays;
            this.preference = preference;
            existing = new List<LayoutItem>[bays.Count];
            counted = new bool[bays.Count][];
            models = new MachineModel[bays.Count];
            baseline = new double[bays.Count];
            for (int b = 0; b < bays.Count; b++)
            {
                int width = bays[b].Grid.inventoryShape.width;
                var installed = bays[b].Grid.childItems;
                existing[b] = installed.Select(i => GameAdapter.ToLayout(i, i.modifiedShape, width)).ToList();
                counted[b] = installed.Select(i => i.IsTag(ModuleHelper.MODULE_TAG) || i.IsGameItemType("NODE")).ToArray();
                models[b] = ReadModel(bays[b].Machine);
                baseline[b] = models[b].Measure(Sum(b, existing[b])).Score(preference);
            }
            foreach (var option in options)
            {
                var module = modules[option.Module];
                var item = GameAdapter.ToLayout(module, module.modifiedShape, bays[option.Bay].Grid.inventoryShape.width);
                item.Cells = option.Cells;
                item.X = option.X;
                item.Y = option.Y;
                candidates.Add(option, item);
            }
        }

        private int[] Sum(int bay, IReadOnlyList<LayoutItem> items)
        {
            var stats = LayoutSimulation.Compute(bays[bay].Grid.inventoryShape.width, items);
            return SumStats(bay, stats);
        }

        private int[] SumStats(int bay, IReadOnlyList<int[]> stats)
        {
            var total = new int[3];
            for (int i = 0; i < stats.Count; i++)
                if (i >= counted[bay].Length || counted[bay][i])
                    for (int s = 0; s < 3; s++) total[s] += stats[i][s];
            return total;
        }

        internal double Evaluate(IReadOnlyList<Placement> placements)
        {
            double result = 0;
            for (int b = 0; b < bays.Count; b++)
            {
                if (GameAdapter.Profile(bays[b].Machine) == null) continue;
                var layout = new List<LayoutItem>(existing[b]);
                foreach (var placement in placements)
                    if (placement.Bay == b) layout.Add(candidates[placement]);
                if (layout.Count == existing[b].Count) continue;
                result += models[b].Measure(Sum(b, layout)).Score(preference) - baseline[b];
            }
            return result;
        }

        internal BayPrediction Predict(int bay, IReadOnlyList<Placement> placements)
        {
            var layout = new List<LayoutItem>(existing[bay]);
            foreach (var placement in placements)
                if (placement.Bay == bay) layout.Add(candidates[placement]);
            var stats = LayoutSimulation.Compute(bays[bay].Grid.inventoryShape.width, layout);
            var before = Sum(bay, existing[bay]);
            var after = SumStats(bay, stats);
            return new BayPrediction { Before = before, After = after, Items = layout, ItemStats = stats,
                ExistingCount = existing[bay].Count, Model = models[bay], BeforeMetrics = models[bay].Measure(before), AfterMetrics = models[bay].Measure(after) };
        }

        internal static MachineModel ReadModel(GameItem machine)
        {
            var model = new MachineModel { Id = machine.identifier,
                BasePower = GameAdapter.IntTag(machine, "MACHINERY_DRAWN_POWER_BASE"),
                BaseSpeed = GameAdapter.IntTag(machine, "BASE_PROCESSING_SPEED_TAG"),
                HasPerformanceRatio = machine.IsTag(MachineHelper.MACHINE_PERFORMANCE_RATIO_INT),
                HasEfficiencyRatio = machine.IsTag(MachineHelper.MACHINE_EFFICIENCY_RATIO_INT),
                PerformanceRatio = GameAdapter.IntTag(machine, MachineHelper.MACHINE_PERFORMANCE_RATIO_INT, 1),
                EfficiencyRatio = GameAdapter.IntTag(machine, MachineHelper.MACHINE_EFFICIENCY_RATIO_INT, 1),
                QualityRatio = GameAdapter.IntTag(machine, "MACHINE_QUALITY_RATIO_INT"),
                PerformanceReducesPower = machine.IsTag(MachineHelper.MACHINE_PERFORMANCE_REDUCES_POWER_TAG)
            };
            if (machine.identifier == "hydroponic")
            {
                var seed = MachineHydroponic.GetInputSlot(machine)?.childItem;
                if (seed != null)
                {
                    model.HydroBaseHarvest = HydroponicHelper.GetBaseHarvestQuantity(seed);
                    model.HydroIgnoresQuality = HydroponicHelper.GetPlantId(seed) == "hydroreed_seed";
                }
            }
            if (machine.identifier == "furnace")
            {
                var installed = GameAdapter.ModuleBays(machine).SelectMany(g => g.childItems);
                model.BlastFurnace = installed.Any(i => i.identifier == "furnace_module_blast");
                var input = MachineHelper.GetBodyStorage(machine, 2);
                var scrap = GraphUtils.FindAllChildrenType<GameItem>(input, i => i.identifier == "scrap_metal");
                var ore = GraphUtils.FindAllChildrenType<GameItem>(input, i => i.identifier == "common_ore");
                int needed = model.BlastFurnace ? 3 : 2;
                var materials = scrap.Concat(ore).ToList();
                if (materials.Count >= needed)
                {
                    int oreCount = materials.Skip(materials.Count - needed).Count(i => i.identifier == "common_ore");
                    // Native purity levels5..10 normalized to diagram's0..5 scale.
                    model.FurnaceInputQuality = 1 + 2 * oreCount / needed;
                }
                model.Flux = GraphUtils.FindAllChildrenType<GameItem>(input,
                    i => i.identifier == "flux_agent" || i.identifier == "advanced_flux_agent").Count > 0;
            }
            if (machine.identifier == "plant_dna_manipulator")
            {
                var seed = MachineDnaManipulator.GetSeedSlot(machine)?.childItem;
                if (seed != null)
                {
                    int complexity = PlantGeneticsHelper.GetEffectiveComplexity(seed);
                    model.DnaBaseChance = 95 - 5 * Math.Max(0, complexity);
                }
            }
            return model;
        }
    }
}
