using System;
using System.Collections.Generic;
using System.Linq;

namespace ModuleAdvisor
{
    // A small second-stage refinement: expensive temporal scoring is not run 30000 times.
    internal static class TemporalSearch
    {
        internal static Plan Improve(IReadOnlyList<Placement> options, Plan immediate,
            Func<IReadOnlyList<Placement>, double> evaluate, int budget = 400)
        {
            if (budget < 2) throw new ArgumentOutOfRangeException(nameof(budget));
            int searchLimit = Math.Max(2, budget * 3 / 4);
            int calls = 0;
            Func<List<Placement>, Plan> score = items => {
                calls++;
                double value = evaluate(items);
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("Nonfinite temporal utility.");
                return new Plan { Placements = items, Score = value };
            };
            var empty = score(new List<Placement>());
            var start = score(new List<Placement>(immediate.Placements));
            var best = Better(start, empty) ? start : empty;
            var groups = options.GroupBy(p => p.Module).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            // Start from both instantaneous plan and empty: long-lived modules can be absent from instant plan.
            foreach (var seed in new[] {start, empty})
            {
                var current = seed;
                for (int round = 0; round < 2; round++)
                {
                    foreach (var group in groups)
                    {
                        if (calls >= searchLimit) break;
                        var rest = current.Placements.Where(p => p.Module != group[0].Module).ToList();
                        var local = current;
                        if (rest.Count != current.Placements.Count && calls < searchLimit)
                        {
                            var removed = score(rest);
                            if (Better(removed, local)) local = removed;
                        }
                        // Distribute finite work across all modules and all four passes.
                        int samples = Math.Max(1, Math.Min(8, budget / Math.Max(1, groups.Count * 4)));
                        for (int j = 0; j < Math.Min(samples, group.Count) && calls < searchLimit; j++)
                        {
                            var p = group[(int)((long)j * (group.Count - 1) / Math.Max(1, Math.Min(samples, group.Count) - 1))];
                            if (!Fits(rest, p)) continue;
                            var candidate = score(new List<Placement>(rest) {p});
                            if (Better(candidate, local)) local = candidate;
                        }
                        current = local;
                        if (Better(current, best)) best = current;
                    }
                }
            }
            // Reserve a quarter of the hard budget for removing newly redundant additions.
            // Very large plans may stop cleanup early rather than freeze the game UI.
            int index = 0;
            while (index < best.Placements.Count && calls < budget)
            {
                var items = new List<Placement>(best.Placements);
                items.RemoveAt(index);
                var candidate = score(items);
                if (candidate.Score >= best.Score) {best = candidate; index = 0;}
                else index++;
            }
            return best;
        }
        private static bool Better(Plan a, Plan b) => a.Score > b.Score || (a.Score == b.Score && a.Placements.Count < b.Placements.Count);
        private static bool Fits(IReadOnlyList<Placement> chosen, Placement addition)
        {
            foreach (var p in chosen)
            {
                if (p.Module == addition.Module) return false;
                if (p.Bay == addition.Bay && p.Cells.Intersect(addition.Cells).Any()) return false;
            }
            return true;
        }
    }
}
