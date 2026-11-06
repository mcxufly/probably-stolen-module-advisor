using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModuleAdvisor
{
    internal static class AdvisorUI
    {
        private const string WindowId = ModEntry.Id + ".advisor";
        private static readonly HashSet<GameItem> machines = new HashSet<GameItem>();
        private static readonly HashSet<GameItem> modules = new HashSet<GameItem>();
        private static bool initialized;
        private static int preference;
        private static CustomUIWindow window;
        private static RectTransform results;
        internal static bool IsOpen => window != null && window.IsAlive && window.IsVisible;
        internal static void Close()
        {
            if (window != null && window.IsAlive) window.Close();
            window = null;
            results = null;
        }
        internal static void Reset()
        {
            Close();
            initialized = false;
            machines.Clear();
            modules.Clear();
        }

        internal static void Open()
        {
            if (PlayerStore.Instance == null || CustomUIManager.Instance == null) return;
            var snapshot = GameAdapter.Scan();
            var availableMachines = snapshot.Bays.Select(b => b.Machine).Distinct().ToList();
            if (!initialized)
            {
                machines.UnionWith(availableMachines);
                modules.UnionWith(snapshot.Modules);
                initialized = true;
            }
            machines.IntersectWith(availableMachines);
            modules.IntersectWith(snapshot.Modules);
            Close();
            var builder = CustomUIManager.Window(WindowId, L.Text("title"), CustomUIManager.LAYER_PAPER)
                .SetSize(760, 620).Center().SetDraggable(true);
            builder.AddLabel(L.Text("intro"))
                .BeginRow()
                .AddButton(L.Text("refresh"), Open)
                .AddButton(L.Text("generate"), Generate)
                .AddButton(L.Text("close"), Close).End()
                .AddDropdown(new[] { L.Text("balanced"), L.Text("performance"), L.Text("efficiency"), L.Text("quality") },
                    preference, value => { preference = value; Invalidate(); })
                .BeginTabs().BeginTab(L.Text("machines", availableMachines.Count));
            builder.BeginRow().AddButton(L.Text("all"), () => {
                machines.UnionWith(availableMachines); Open();
            }).AddButton(L.Text("none"), () => { machines.Clear(); Open(); }).End().BeginScroll(360);
            foreach (var machine in availableMachines)
            {
                var bays = snapshot.Bays.Where(b => b.Machine == machine).ToList();
                int free = bays.Sum(b => FreeCells(b.Grid));
                bool unknown = GameAdapter.Profile(machine) == null;
                builder.AddToggle(GameAdapter.Name(machine), machines.Contains(machine), value => {
                    if (value) machines.Add(machine); else machines.Remove(machine); Invalidate();
                }).WithTooltip(L.Text("machine_info", free, bays.Sum(b => b.Grid.childItems.Count))
                    + "\n" + L.Text("location", GameAdapter.Location(machine))
                    + (unknown ? "\n" + L.Text("manual_machine") : ""));
            }
            if (availableMachines.Count == 0) builder.AddLabel(L.Text("no_machines"));
            builder.End().End().BeginTab(L.Text("modules", snapshot.Modules.Count))
                .BeginRow().AddButton(L.Text("all"), () => {
                    modules.UnionWith(snapshot.Modules); Open();
                }).AddButton(L.Text("none"), () => { modules.Clear(); Open(); }).End().BeginScroll(360);
            foreach (var module in snapshot.Modules)
            {
                builder.AddToggle(GameAdapter.Name(module) + (GameAdapter.ManualOnly(module) ? " · " + L.Text("manual") : ""),
                    modules.Contains(module), value => {
                        if (value) modules.Add(module); else modules.Remove(module); Invalidate();
                    }).WithTooltip(GameAdapter.Details(module));
            }
            if (snapshot.Modules.Count == 0) builder.AddLabel(L.Text("no_modules"));
            builder.End().End().BeginTab(L.Text("results")).BeginScroll(360);
            results = builder.CurrentContainer;
            builder.AddLabel(L.Text("press_generate"));
            builder.End().End().EndTabs();
            window = builder.Show();
        }

        private static int FreeCells(GameGridInventory grid)
        {
            int count = 0;
            for (int y = 0; y < grid.inventoryShape.height; y++)
                for (int x = 0; x < grid.inventoryShape.width; x++)
                    if (grid.inventoryShape.Get(x, y) == 0
                        && !grid.childItems.Any(i => i.modifiedShape.Get(x, y) == 1)) count++;
            return count;
        }

        private static CustomUIBuilder ResultBuilder()
        {
            CustomUIBuilder.ClearContainer(results);
            return new CustomUIBuilder(CustomUIManager.Instance, window, results);
        }
        private static void Invalidate()
        {
            if (results != null && window != null) ResultBuilder().AddLabel(L.Text("press_generate"));
        }

        private static void Generate()
        {
            if (!IsOpen || results == null) return;
            try
            {
                var snapshot = GameAdapter.Scan();
                var selectedBays = snapshot.Bays.Where(b => machines.Contains(b.Machine)).ToList();
                var selectedModules = snapshot.Modules.Where(modules.Contains).ToList();
                var output = ResultBuilder();
                if (selectedBays.Count == 0 || selectedModules.Count == 0)
                {
                    output.AddLabel(L.Text("select_first"));
                    return;
                }
                long candidateBudget = (long)selectedModules.Count * selectedBays.Sum(b =>
                    (long)b.Grid.inventoryShape.width * b.Grid.inventoryShape.height * 8);
                if (candidateBudget > 80000)
                {
                    output.AddWarning(L.Text("too_large"));
                    return;
                }
                var options = GameAdapter.Options(selectedModules, selectedBays, preference);
                var plan = Planner.Solve(selectedModules.Count, options);
                output.AddHeader(L.Text("plan_count", plan.Placements.Count));
                output.AddLabel(L.Text("plan_note"));
                var used = new HashSet<int>();
                foreach (var group in plan.Placements.GroupBy(p => p.Bay))
                {
                    var bay = selectedBays[group.Key];
                    output.AddHeader(GameAdapter.Name(bay.Machine) + " · " + L.Text("bay", bay.Number));
                    foreach (var placement in group)
                    {
                        used.Add(placement.Module);
                        var module = selectedModules[placement.Module];
                        GridShape shape = new GridShapeBuilder(module.modifiedShape).SetTransform(
                            placement.X, placement.Y, placement.Flipped, placement.Rotation);
                        int[] stats = GameAdapter.EstimatedStats(module, shape);
                        output.AddLabel(L.Text("placement", GameAdapter.Name(module), placement.X + 1,
                            placement.Y + 1, placement.Rotation * 90, placement.Flipped ? L.Text("yes") : L.Text("no")))
                            .WithTooltip(GameAdapter.Details(module));
                        output.AddLabel(L.Text("estimate", stats[0], stats[1], stats[2], placement.Score.ToString("0.##")));
                    }
                    output.AddSeparator();
                }
                output.AddHeader(L.Text("unassigned"));
                for (int m = 0; m < selectedModules.Count; m++)
                {
                    if (used.Contains(m)) continue;
                    var module = selectedModules[m];
                    var compatible = new List<string>();
                    bool unknownCompatible = false;
                    foreach (var bay in selectedBays)
                    {
                        // Four chemical upgrades are the useful native cap; do not propose a fifth.
                        if (module.IsTag(ModuleHelper.MODULE_TYPE_CHEM)
                            && bay.Grid.childItems.Count(i => i.IsTag(ModuleHelper.MODULE_TAG)) >= 4) continue;
                        if (GameAdapter.Shapes(module, bay.Grid).Any())
                        {
                            compatible.Add(GameAdapter.Name(bay.Machine));
                            unknownCompatible |= GameAdapter.Profile(bay.Machine) == null;
                        }
                    }
                    bool manual = GameAdapter.ManualOnly(module) || unknownCompatible;
                    output.AddLabel(GameAdapter.Name(module)).WithTooltip(GameAdapter.Details(module));
                    output.AddLabel(compatible.Count == 0 ? L.Text("no_fit") : manual
                        ? L.Text("optional", string.Join(" / ", compatible.Distinct()))
                        : L.Text("not_chosen"));
                }
                if (used.Count == selectedModules.Count) output.AddLabel(L.Text("all_assigned"));
                output.AddLabel(L.Text("refresh_note"));
                CustomUIBuilder.RebuildLayout(results);
            }
            catch (Exception error)
            {
                ResultBuilder().AddWarning(L.Text("error"));
                Debug.LogError("[ModuleAdvisor] " + error);
            }
        }
    }
}
