using System;
using System.Collections.Generic;

namespace ModuleAdvisor
{
    public sealed class Placement
    {
        public int Module;
        public int Bay;
        public int[] Cells = Array.Empty<int>();
        public double Score;
        public int X, Y, Rotation;
        public bool Flipped;
    }

    public sealed class Plan
    {
        public List<Placement> Placements = new List<Placement>();
        public double Score;
    }

    /// <summary>
    /// Deterministic placement heuristic; it does not promise a globally optimal plan.
    /// Bay compatibility, grid dimensions and existing occupancy belong to the caller.
    /// </summary>
    public static class Planner
    {
        private const int BeamWidth = 48;
        private const int EvaluationBudget = 200000;

        /// <summary>
        /// Selects at most one positive-score placement per physical module.
        /// Module must be in [0, moduleCount); Bay and cell codes must be nonnegative.
        /// Cells must be nonempty and distinct; scores must be finite, including skipped scores.
        /// X, Y, Rotation and Flipped are opaque metadata. Inputs are never modified.
        /// The beam evaluates at most 200000 candidates; fallback scans remaining candidates
        /// greedily, so validation, sorting and fallback still scale with the input size.
        /// </summary>
        public static Plan Solve(int moduleCount, IReadOnlyList<Placement> options)
        {
            if (moduleCount < 0)
                throw new ArgumentOutOfRangeException(nameof(moduleCount));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var byModule = new Dictionary<int, Group>();
            for (int i = 0; i < options.Count; i++)
            {
                Placement option = options[i];
                Validate(option, moduleCount);
                if (option.Score <= 0)
                    continue;
                if (!byModule.TryGetValue(option.Module, out Group group))
                {
                    group = new Group(option.Module);
                    byModule.Add(option.Module, group);
                }
                group.Options.Add(option);
            }

            var groups = new List<Group>(byModule.Values);
            foreach (Group group in groups)
                group.Options.Sort(CompareOptions);
            groups.Sort((a, b) =>
            {
                int count = a.Options.Count.CompareTo(b.Options.Count);
                return count != 0 ? count : a.Module.CompareTo(b.Module);
            });

            var beam = new List<State> { new State() };
            int evaluations = 0;
            long serial = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                Group group = groups[g];
                // Every state keeps a skip branch, including when the budget ends mid-module.
                var next = new List<State>(beam);
                foreach (State state in beam)
                {
                    foreach (Placement option in group.Options)
                    {
                        if (evaluations == EvaluationBudget)
                            return FinishGreedy(next[0], groups, g);
                        evaluations++;
                        double score = state.Score + option.Score;
                        // Existing states win ties; later candidates cannot change this ordering.
                        if (next.Count == BeamWidth && score <= next[next.Count - 1].Score)
                            continue;
                        if (Overlaps(state.Occupied, option))
                            continue;

                        var child = new State(state, option, score, ++serial);
                        int insertion = next.BinarySearch(child, StateComparer.Instance);
                        next.Insert(insertion < 0 ? ~insertion : insertion, child);
                        if (next.Count > BeamWidth)
                            next.RemoveAt(next.Count - 1);
                    }
                }
                beam = next;
            }
            return ToPlan(beam[0]);
        }

        private static void Validate(Placement option, int moduleCount)
        {
            if (option == null)
                throw new ArgumentException("A placement cannot be null.", "options");
            if (option.Module < 0 || option.Module >= moduleCount)
                throw new ArgumentException("A placement module is outside moduleCount.", "options");
            if (option.Bay < 0)
                throw new ArgumentException("A placement bay cannot be negative.", "options");
            if (double.IsNaN(option.Score) || double.IsInfinity(option.Score))
                throw new ArgumentException("A placement score must be finite.", "options");
            if (option.Cells == null || option.Cells.Length == 0)
                throw new ArgumentException("A placement must occupy at least one cell.", "options");
            var seen = new HashSet<int>();
            foreach (int cell in option.Cells)
                if (cell < 0 || !seen.Add(cell))
                    throw new ArgumentException("Cell codes must be nonnegative and distinct.", "options");
        }

        private static int CompareOptions(Placement a, Placement b)
        {
            int result = b.Score.CompareTo(a.Score);
            if (result == 0) result = a.Bay.CompareTo(b.Bay);
            if (result == 0) result = a.X.CompareTo(b.X);
            if (result == 0) result = a.Y.CompareTo(b.Y);
            if (result == 0) result = a.Rotation.CompareTo(b.Rotation);
            if (result == 0) result = a.Flipped.CompareTo(b.Flipped);
            if (result == 0) result = a.Cells.Length.CompareTo(b.Cells.Length);
            for (int i = 0; result == 0 && i < a.Cells.Length; i++)
                result = a.Cells[i].CompareTo(b.Cells[i]);
            return result;
        }

        private static long CellKey(int bay, int cell)
        {
            return ((long)bay << 32) | (uint)cell;
        }

        private static bool Overlaps(HashSet<long> occupied, Placement option)
        {
            foreach (int cell in option.Cells)
                if (occupied.Contains(CellKey(option.Bay, cell)))
                    return true;
            return false;
        }

        private static void Occupy(HashSet<long> occupied, Placement option)
        {
            foreach (int cell in option.Cells)
                occupied.Add(CellKey(option.Bay, cell));
        }

        private static Plan FinishGreedy(State best, List<Group> groups, int start)
        {
            Plan plan = ToPlan(best);
            var occupied = new HashSet<long>(best.Occupied);
            for (int g = start; g < groups.Count; g++)
            {
                Group group = groups[g];
                // The best partial beam may already include this module when budget ran out.
                if (best.Tail != null && best.Tail.Placement.Module == group.Module)
                    continue;
                foreach (Placement option in group.Options)
                {
                    if (Overlaps(occupied, option))
                        continue;
                    plan.Placements.Add(option);
                    plan.Score += option.Score;
                    Occupy(occupied, option);
                    break;
                }
            }
            return plan;
        }

        private static Plan ToPlan(State state)
        {
            var plan = new Plan { Score = state.Score };
            for (Choice node = state.Tail; node != null; node = node.Parent)
                plan.Placements.Add(node.Placement);
            plan.Placements.Reverse();
            return plan;
        }

        private sealed class Group
        {
            public readonly int Module;
            public readonly List<Placement> Options = new List<Placement>();
            public Group(int module) { Module = module; }
        }

        // History stores choices only, allowing obsolete occupancy sets to be collected.
        private sealed class Choice
        {
            public readonly Choice Parent;
            public readonly Placement Placement;
            public Choice(Choice parent, Placement placement) { Parent = parent; Placement = placement; }
        }

        private sealed class State
        {
            public readonly Choice Tail;
            public readonly double Score;
            public readonly long Serial;
            public readonly HashSet<long> Occupied;

            public State() { Occupied = new HashSet<long>(); }
            public State(State parent, Placement placement, double score, long serial)
            {
                Tail = new Choice(parent.Tail, placement);
                Score = score;
                Serial = serial;
                Occupied = new HashSet<long>(parent.Occupied);
                Occupy(Occupied, placement);
            }
        }

        private sealed class StateComparer : IComparer<State>
        {
            public static readonly StateComparer Instance = new StateComparer();
            public int Compare(State a, State b)
            {
                int score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : a.Serial.CompareTo(b.Serial);
            }
        }
    }
}
