using System;
using System.Collections.Generic;
using System.Linq;

namespace ModuleAdvisor
{
    internal sealed class Bay
    {
        internal GameItem Machine;
        internal GameGridInventory Grid;
        internal int Number;
    }

    internal sealed class Snapshot
    {
        internal List<Bay> Bays = new List<Bay>();
        internal List<GameItem> Modules = new List<GameItem>();
    }

    internal static class GameAdapter
    {
        internal static List<GameGridInventory> ModuleBays(GameItem machine)
        {
            var result = new List<GraphNodeStorage>();
            if (machine.contentWindow?.childElement is MachineryPanelRow row)
                row.GetPanel("module")?.CollectStorage(result);
            return result.OfType<GameGridInventory>().Distinct().ToList();
        }

        internal static Snapshot Scan()
        {
            var snapshot = new Snapshot();
            var items = PlayerStore.Instance.FindAllItem();
            // Also recognize bays inside containers we do not own: their modules are not free.
            var installed = new HashSet<GameInventory>();
            foreach (var item in PlayerStore.Instance.FindAllItem(false))
                foreach (var grid in ModuleBays(item)) installed.Add(grid);
            foreach (var machine in items.Where(GeneralHelper.IsItemEffectivelyOwned))
            {
                int number = 0;
                foreach (var grid in ModuleBays(machine))
                    snapshot.Bays.Add(new Bay { Machine = machine, Grid = grid, Number = ++number });
            }
            snapshot.Modules = items.Where(item => item.IsTag(ModuleHelper.MODULE_TAG)
                && GeneralHelper.IsItemEffectivelyOwned(item)
                && item.parentInventory != null && !installed.Contains(item.parentInventory)
                && !item.IsTag(ModuleHelper.MODULE_STUCK_TAG) && item.MayRemove()
                && item.MaxNumRemove() > 0).ToList();
            return snapshot;
        }

        internal static string Name(GameItem item) => item.GetDisplayName(true) + " [" + item.uniqueId + "]";
        internal static string Location(GameItem item)
        {
            var container = item.parentInventory?.GetParentItem();
            return container != null ? Name(container) : item.parentInventory?.identifier ?? L.Text("unknown_location");
        }

        internal static bool ManualOnly(GameItem item) => item.IsTag(ModuleHelper.MODULE_TYPE_CHEM)
            || item.IsGameItemType("NODE") || item.IsTag(ModuleHelper.ADDITIONAL_EFFECT_STRING);

        private static string StringTag(GameItem item, string key) => item.modifiedState.dict.TryGetValue(key, out var tag)
            ? tag.GetString() : null;

        internal static string[] Effects(GameItem item) => new[] {
            StringTag(item, ModuleEffectHelper.MODULE_EFFECT1_TAG),
            StringTag(item, ModuleEffectHelper.MODULE_EFFECT2_TAG)
        };

        internal static string Details(GameItem item)
        {
            var lines = new List<string> { L.Text("stats", Base(item, 0), Base(item, 1), Base(item, 2)), L.Text("location", Location(item)) };
            if (item.IsTag(ModuleHelper.ADDITIONAL_EFFECT_STRING))
                lines.Add(StringTag(item, ModuleHelper.ADDITIONAL_EFFECT_STRING));
            foreach (var key in Effects(item))
                if (key != null && ModuleEffectHelper.moduleEffects.TryGetValue(key, out var effect))
                    lines.Add(effect.displayName + ": " + effect.description);
            if (item.IsTag(ModuleHelper.MODULE_TYPE_CHEM)) lines.Add(L.Text("permanent"));
            return string.Join("\n", lines);
        }

        private static int Base(GameItem item, int stat)
        {
            string key = new[] { ModuleHelper.BONUS_PERCENTAGE_PERFORMANCE_INT,
                ModuleHelper.BONUS_PERCENTAGE_EFFICIENCY_INT, ModuleHelper.BONUS_PERCENTAGE_QUALITY_INT }[stat];
            return item.modifiedState.dict.TryGetValue(key, out var tag) ? tag.GetInt() : 0;
        }

        // Known consumers from the installed build. Unknown machines remain manual suggestions.
        internal static double[] Profile(GameItem machine)
        {
            switch (machine.identifier)
            {
                case "hydroponic": return new[] { .5, 1.0, .5 };
                case "plant_dna_manipulator": return new[] { .5, 1.0, .5 };
                case "printer": return new[] { 1.0, 1.0, .25 };
                case "desequencer": case "security_alarm": return new[] { 1.0, 1.0, 0.0 };
                case "furnace": return new[] { 0.0, 1.0, 1.0 };
                case "moisture_farm": case "water_purifier": case "evaporator": return new[] { 1.0, 1.0, 1.0 };
                case "mirage_projector": return new[] { .25, 1.0, .5 };
                case "deal_maker": return new[] { 1.0, 1.0, .1 };
                default: return null;
            }
        }

        internal static int[] EstimatedStats(GameItem module, GridShape shape)
        {
            var stats = new[] { Base(module, 0), Base(module, 1), Base(module, 2) };
            foreach (var effect in Effects(module))
            {
                bool boosted = (effect == ModuleEffectHelper.MODULE_EFFECT_TOP_MOUNT && shape.minY <= 0)
                    || (effect == ModuleEffectHelper.MODULE_EFFECT_SIDE_MOUNT && shape.minX <= 0);
                if (boosted)
                    for (int i = 0; i < 3; i++) stats[i] += Base(module, i) * 20 / 100;
            }
            return stats;
        }

        internal static double Score(GameItem module, Bay bay, GridShape shape, int preference)
        {
            var profile = Profile(bay.Machine);
            if (profile == null || ManualOnly(module)) return 0;
            var stats = EstimatedStats(module, shape);
            double score = 0;
            for (int i = 0; i < 3; i++)
            {
                double weight = preference == i + 1 ? 3 : 1;
                // Always account for drawbacks, even outside the chosen priority.
                score += stats[i] * profile[i] * weight;
            }
            return score;
        }

        internal static List<Placement> Options(List<GameItem> modules, List<Bay> bays, int preference)
        {
            var options = new List<Placement>();
            for (int m = 0; m < modules.Count; m++)
                for (int b = 0; b < bays.Count; b++)
                {
                    if (ManualOnly(modules[m]) || Profile(bays[b].Machine) == null) continue;
                    foreach (var shape in Shapes(modules[m], bays[b].Grid))
                    {
                        double score = Score(modules[m], bays[b], shape, preference);
                        if (score <= 0) continue;
                        var cells = new List<int>();
                        for (int y = shape.minY; y <= shape.maxY; y++)
                            for (int x = shape.minX; x <= shape.maxX; x++)
                                if (shape.Get(x, y) == 1) cells.Add(y * bays[b].Grid.inventoryShape.width + x);
                        if (cells.Count > 0)
                            options.Add(new Placement { Module = m, Bay = b, Cells = cells.ToArray(), Score = score,
                                X = shape.minX, Y = shape.minY, Rotation = shape.orientation, Flipped = shape.flipped });
                    }
                }
            return options;
        }

        internal static IEnumerable<GridShape> Shapes(GameItem module, GameGridInventory grid)
        {
            if (!grid.MayHaveValidInventorySlot(module)) yield break;
            var seen = new HashSet<string>();
            for (int flip = 0; flip < 2; flip++)
                for (int rotation = 0; rotation < 4; rotation++)
                    for (int y = 0; y < grid.inventoryShape.height; y++)
                        for (int x = 0; x < grid.inventoryShape.width; x++)
                        {
                            GridShape shape = new GridShapeBuilder(module.modifiedShape).SetTransform(x, y, flip != 0, rotation);
                            var slot = grid.TryInventorySlot(module, 1, shape);
                            if (slot == null || slot.numTransfer < 1) continue;
                            var cells = new List<string>();
                            for (int sy = shape.minY; sy <= shape.maxY; sy++)
                                for (int sx = shape.minX; sx <= shape.maxX; sx++)
                                    if (shape.Get(sx, sy) == 1) cells.Add(sx + ":" + sy);
                            if (seen.Add(string.Join(";", cells))) yield return shape;
                        }
        }
    }
}
