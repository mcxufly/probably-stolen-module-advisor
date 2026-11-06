using System;
using System.Collections.Generic;

namespace ModuleAdvisor
{
    /// <summary>Bounded deterministic heuristic for interacting modules, not an optimal solver.</summary>
    public static class LayoutPlanner
    {
        private const int BeamWidth = 32;
        private const int EvaluationBudget = 30000;
        private const int SearchBudget = 22000;

        /// <summary>
        /// evaluate returns absolute whole-layout utility, including existing items and caps.
        /// It must be deterministic, finite, and must not mutate the supplied placements.
        /// Zero/negative standalone options are retained. Inputs and placement metadata are not changed.
        /// At most 30000 callback calls include the empty baseline and redundancy cleanup.
        /// Candidate sampling and the beam are finite; a global optimum is not promised.
        /// </summary>
        public static Plan Solve(int moduleCount, IReadOnlyList<Placement> options,
            Func<IReadOnlyList<Placement>, double> evaluate, ISet<int> nodeModules)
        {
            if (moduleCount < 0) throw new ArgumentOutOfRangeException(nameof(moduleCount));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            if (nodeModules == null) throw new ArgumentNullException(nameof(nodeModules));
            var byModule = new Dictionary<int, List<Placement>>();
            foreach (Placement option in options)
            {
                Validate(option, moduleCount);
                if (!byModule.TryGetValue(option.Module, out List<Placement> list))
                {
                    list = new List<Placement>();
                    byModule.Add(option.Module, list);
                }
                list.Add(option);
            }
            var groups = new List<List<Placement>>();
            foreach (List<Placement> list in byModule.Values)
            {
                list.Sort(CompareOptions);
                groups.Add(Sample(list, 64));
            }
            groups.Sort((a, b) =>
            {
                int order = nodeModules.Contains(a[0].Module).CompareTo(nodeModules.Contains(b[0].Module));
                if (order == 0) order = a.Count.CompareTo(b.Count);
                return order != 0 ? order : a[0].Module.CompareTo(b[0].Module);
            });

            var runner = new Evaluator(evaluate);
            State best = runner.Compute(new List<Placement>());
            var beam = new List<State> { best };
            for (int g = 0; g < groups.Count; g++)
            {
                var next = new List<State>(beam);
                // Share the search budget across all modules instead of spending it on the first bay.
                int allowance = Math.Max(1, (SearchBudget - runner.Calls) / (groups.Count - g));
                int end = Math.Min(SearchBudget, runner.Calls + allowance);
                List<Placement> candidates = Sample(groups[g], Math.Max(1, allowance / beam.Count));
                // Option-major traversal gives each spatial alternative some exposure.
                foreach (Placement option in candidates)
                {
                    foreach (State state in beam)
                    {
                        if (runner.Calls >= end) break;
                        if (!Fits(state.Items, option)) continue;
                        var items = new List<Placement>(state.Items) { option };
                        if (!runner.CanCompute(items.Count)) continue;
                        State child = runner.Compute(items);
                        next.Add(child);
                        if (Better(child, best)) best = child;
                    }
                }
                next.Sort(CompareStates);
                beam = KeepDiverse(next);
            }

            // Nodes are processed after modules above, then every module (including unused ones)
            // gets two opportunities to improve its location against the complete current layout.
            for (int round = 0; round < 2; round++)
            {
                bool changed = false;
                foreach (List<Placement> group in groups)
                {
                    var rest = new List<Placement>();
                    foreach (Placement p in best.Items)
                        if (p.Module != group[0].Module) rest.Add(p);
                    State local = best;
                    if (rest.Count != best.Items.Count && runner.CanCompute(rest.Count))
                    {
                        State removed = runner.Compute(rest);
                        if (Better(removed, local)) local = removed;
                    }
                    foreach (Placement option in group)
                    {
                        if (!Fits(rest, option) || !runner.CanCompute(rest.Count + 1)) continue;
                        var items = new List<Placement>(rest) { option };
                        State candidate = runner.Compute(items);
                        if (Better(candidate, local)) local = candidate;
                    }
                    if (Better(local, best)) { best = local; changed = true; }
                }
                if (!changed) break;
            }

            // Reserve a complete restart-on-removal pass even if search hit its budget.
            // A removal can change another item's usefulness, so one forward pass is insufficient.
            int index = 0;
            while (index < best.Items.Count)
            {
                var items = new List<Placement>(best.Items);
                items.RemoveAt(index);
                State candidate = runner.Compute(items);
                if (candidate.Score >= best.Score) { best = candidate; index = 0; }
                else index++;
            }
            return new Plan { Score = best.Score, Placements = new List<Placement>(best.Items) };
        }

        private static bool Better(State a, State b)
        {
            return a.Score > b.Score || (a.Score == b.Score && a.Items.Count < b.Items.Count);
        }

        private static int CompareStates(State a, State b)
        {
            int order = b.Score.CompareTo(a.Score);
            return order != 0 ? order : a.Serial.CompareTo(b.Serial);
        }

        private static List<State> KeepDiverse(List<State> states)
        {
            if (states.Count <= BeamWidth) return states;
            var kept = states.GetRange(0, 24);
            // Preserve lower-scoring/zero branches for thresholds and future adjacency activation.
            for (int i = 0; i < 8; i++)
                kept.Add(states[24 + (int)((long)i * (states.Count - 25) / 7)]);
            return kept;
        }

        private static List<Placement> Sample(List<Placement> options, int count)
        {
            count = Math.Min(64, count);
            if (options.Count <= count) return options;
            var sample = new List<Placement>(count);
            for (int i = 0; i < count; i++)
                sample.Add(options[(int)((long)i * (options.Count - 1) / Math.Max(1, count - 1))]);
            return sample;
        }

        private static bool Fits(List<Placement> items, Placement option)
        {
            foreach (Placement p in items)
            {
                if (p.Module == option.Module) return false;
                if (p.Bay != option.Bay) continue;
                foreach (int a in p.Cells)
                    foreach (int b in option.Cells)
                        if (a == b) return false;
            }
            return true;
        }

        private static int CompareOptions(Placement a, Placement b)
        {
            int order = a.Bay.CompareTo(b.Bay);
            if (order == 0) order = a.X.CompareTo(b.X);
            if (order == 0) order = a.Y.CompareTo(b.Y);
            if (order == 0) order = a.Rotation.CompareTo(b.Rotation);
            if (order == 0) order = a.Flipped.CompareTo(b.Flipped);
            if (order == 0) order = a.Cells.Length.CompareTo(b.Cells.Length);
            for (int i = 0; order == 0 && i < a.Cells.Length; i++) order = a.Cells[i].CompareTo(b.Cells[i]);
            if (order == 0) order = b.Score.CompareTo(a.Score);
            return order;
        }

        private static void Validate(Placement p, int moduleCount)
        {
            if (p == null || p.Module < 0 || p.Module >= moduleCount || p.Bay < 0 ||
                p.Cells == null || p.Cells.Length == 0 || double.IsNaN(p.Score) || double.IsInfinity(p.Score))
                throw new ArgumentException("Invalid placement.", "options");
            var cells = new HashSet<int>();
            foreach (int cell in p.Cells)
                if (cell < 0 || !cells.Add(cell)) throw new ArgumentException("Invalid occupied cells.", "options");
        }

        private sealed class State
        {
            public List<Placement> Items;
            public double Score;
            public int Serial;
        }

        private sealed class Evaluator
        {
            private readonly Func<IReadOnlyList<Placement>, double> evaluate;
            public int Calls;
            private int reservedItems;
            public Evaluator(Func<IReadOnlyList<Placement>, double> evaluate) { this.evaluate = evaluate; }
            public bool CanCompute(int count)
            {
                // n + (n-1) + ... + 1 is the worst-case final cleanup cost.
                count = Math.Max(count, reservedItems);
                return Calls + 1L + (long)count * (count + 1) / 2 <= EvaluationBudget;
            }
            public State Compute(List<Placement> items)
            {
                if (Calls >= EvaluationBudget) throw new InvalidOperationException("Layout evaluation budget exhausted.");
                double score = evaluate(items.AsReadOnly());
                Calls++;
                reservedItems = Math.Max(reservedItems, items.Count);
                if (double.IsNaN(score) || double.IsInfinity(score))
                    throw new ArgumentException("Layout utility must be finite.", "evaluate");
                return new State { Items = items, Score = score, Serial = Calls };
            }
        }
    }
}
