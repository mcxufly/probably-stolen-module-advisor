using System;
using System.Collections.Generic;

namespace ModuleAdvisor
{
    public sealed class TemporalItem
    {
        public LayoutItem Layout;
        // Persisted LEARNING_ALGO_MAX_* values; do not initialize again from current stats.
        public int[] LearningCaps = new int[3];
        public bool Counts = true;
        public int StableId;
        // Optional replacement snapshot. Without bayCells its occupied cells are used as-is;
        // null instead retains the original footprint (a declared approximation). With bayCells,
        // its normalized shape is first-fit reinserted; null uses the native three-cell L shape.
        public LayoutItem RuinedLayout;
        public bool RuinedCounts = true;
    }

    public sealed class ForecastResult
    {
        // Absolute utility, not utility delta. Use matching sample indices before subtracting a baseline.
        public double[][] SampleCumulative;
        public double[] MeanCumulative, LowerCumulative, ImmediateCurve;
        public double[][] FinalBaseMeans;
        public double[] Survival;
        public double AnyFailureProbability;
        public int Samples;
        public double MeanTotal, LowerTotal;
        public bool RuinedPlacementApproximation;
    }

    /// <summary>
    /// Conditional forecast for repeated use of the same bay, not a full stochastic game simulation.
    /// Learning/degradation are exact; mean and lower P10 utility are fixed-path sample estimates.
    /// No RNG state is read or changed. Utility must be deterministic and apply machine-specific caps.
    /// By default score precedes OnUsed. actionBeforeUtility scores all axes afterward;
    /// performanceAfterAction mixes post-action P with pre-action E/Q (desequencer).
    /// bayCells is the full legal bay mask, not its currently free cells. Null declares a
    /// fixed-snapshot/footprint approximation; empty explicitly forbids ruin reinsertion.
    /// Initial action order is StableId order; successful reinsertion sorts remaining items by
    /// Y/X then appends the ruin, while the current action continues its original item snapshot.
    /// </summary>
    public static class TemporalSimulation
    {
        private const string Learning = "MODULE_EFFECT_LEARNING_ALGORITHM";
        private const string Degrading = "MODULE_EFFECT_DEGRADING";
        private const string Overvolted = "MODULE_EFFECT_OVERVOLTED";

        public static ForecastResult Run(int width, IReadOnlyList<TemporalItem> items, int uses,
            Func<int[], double> utility, int sampleCount = 16,
            bool actionBeforeUtility = false, bool performanceAfterAction = false, int[] bayCells = null)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (utility == null) throw new ArgumentNullException(nameof(utility));
            if (uses < 1 || uses > 100) throw new ArgumentOutOfRangeException(nameof(uses));
            if (sampleCount < 1 || sampleCount > 65536) throw new ArgumentOutOfRangeException(nameof(sampleCount));
            HashSet<int> legal = null;
            int maxY = 0;
            if (bayCells != null)
            {
                legal = new HashSet<int>();
                foreach (int cell in bayCells)
                {
                    if (cell < 0 || !legal.Add(cell)) throw new ArgumentException("Bay cells must be nonnegative and distinct.", nameof(bayCells));
                    maxY = Math.Max(maxY, cell / width);
                }
            }

            var originals = new LayoutItem[items.Count];
            var ruins = new LayoutItem[items.Count];
            var rolls = new int[items.Count];
            var ids = new HashSet<int>();
            int rollsPerUse = 0;
            bool approximate = false;
            for (int i = 0; i < items.Count; i++)
            {
                TemporalItem item = items[i];
                if (item == null || item.LearningCaps == null || item.LearningCaps.Length != 3)
                    throw new ArgumentException("Items require three persisted learning caps.", nameof(items));
                if (!ids.Add(item.StableId))
                    throw new ArgumentException("StableId must identify each physical item uniquely within a bay.", nameof(items));
                originals[i] = Copy(item.Layout);
                foreach (string effect in originals[i].Effects)
                    if (effect == Overvolted) rolls[i]++;
                rollsPerUse = checked(rollsPerUse + rolls[i]);
                if (rolls[i] == 0) continue;
                if (item.RuinedLayout != null) ruins[i] = Copy(item.RuinedLayout);
                else
                {
                    approximate = legal == null;
                    ruins[i] = new LayoutItem
                    {
                        Cells = (int[])originals[i].Cells.Clone(), X = originals[i].X, Y = originals[i].Y,
                        IsModuleType = true
                    };
                }
            }
            // Also validates occupied cell encodings, including otherwise-unused replacements.
            LayoutSimulation.Compute(width, originals);
            for (int i = 0; i < ruins.Length; i++)
                if (ruins[i] != null) LayoutSimulation.Compute(width, new[] { ruins[i] });

            // Canonical action order keeps simultaneous replacements independent of candidate enumeration.
            var actionOrder = new int[items.Count];
            for (int i = 0; i < items.Count; i++) actionOrder[i] = i;
            Array.Sort(actionOrder, (a, b) => items[a].StableId.CompareTo(items[b].StableId));
            int samples = rollsPerUse == 0 ? 1 : sampleCount;
            var result = new ForecastResult
            {
                SampleCumulative = new double[samples][], MeanCumulative = new double[uses],
                LowerCumulative = new double[uses], ImmediateCurve = new double[uses],
                FinalBaseMeans = new double[items.Count][], Survival = new double[items.Count],
                AnyFailureProbability = 1 - Math.Pow(0.9, (double)uses * rollsPerUse),
                Samples = samples, RuinedPlacementApproximation = approximate
            };
            for (int i = 0; i < items.Count; i++)
            {
                result.FinalBaseMeans[i] = new double[3];
                result.Survival[i] = Math.Pow(0.9, (double)uses * rolls[i]);
            }

            for (int sample = 0; sample < samples; sample++)
            {
                var state = new LayoutItem[items.Count];
                var alive = new bool[items.Count];
                var failureRoll = new int[items.Count];
                for (int i = 0; i < items.Count; i++)
                {
                    state[i] = Copy(originals[i]);
                    alive[i] = true;
                    failureRoll[i] = rolls[i] == 0 ? int.MaxValue : FirstFailure(items[i].StableId, sample);
                }
                var order = new List<int>(actionOrder);
                double cumulative = 0;
                var curve = result.SampleCumulative[sample] = new double[uses];
                for (int use = 0; use < uses; use++)
                {
                    int[] total = actionBeforeUtility ? null : Totals(width, state, items, alive);
                    foreach (int i in order.ToArray())
                    {
                        if (!alive[i]) continue;
                        int roll = use * rolls[i];
                        // Native effects run in tag order, even if an earlier effect destroyed the
                        // original object. Later changes to that removed object cannot affect its ruin.
                        foreach (string effect in originals[i].Effects)
                        {
                            if (effect == Overvolted && ++roll == failureRoll[i])
                            {
                                alive[i] = false;
                                state[i] = legal == null ? Copy(ruins[i]) : PlaceRuin(width, ruins[i],
                                    items[i].RuinedLayout != null, state, i, legal, maxY);
                                order.Remove(i);
                                if (legal != null && state[i].Cells.Length != 0)
                                {
                                    // Native insertion sorts the remaining container before appending
                                    // the new object; this use still processes its original snapshot.
                                    order.Sort((a, b) => state[a].Y != state[b].Y
                                        ? state[a].Y.CompareTo(state[b].Y) : state[a].X.CompareTo(state[b].X));
                                    order.Add(i);
                                }
                            }
                            else if (alive[i] && effect == Learning)
                            {
                                for (int axis = 0; axis < 3; axis++)
                                    if (state[i].BaseStats[axis] < items[i].LearningCaps[axis]) state[i].BaseStats[axis]++;
                            }
                            else if (alive[i] && effect == Degrading)
                            {
                                for (int axis = 0; axis < 3; axis++)
                                    if (state[i].BaseStats[axis] > 0) state[i].BaseStats[axis]--;
                            }
                        }
                    }
                    if (actionBeforeUtility) total = Totals(width, state, items, alive);
                    else if (performanceAfterAction) total[0] = Totals(width, state, items, alive)[0];
                    double score = utility(total);
                    if (!Finite(score)) throw new ArgumentException("Utility must return a finite value.", nameof(utility));
                    cumulative += score;
                    if (!Finite(cumulative)) throw new ArgumentException("Cumulative utility must be finite.", nameof(utility));
                    curve[use] = cumulative;
                    result.MeanCumulative[use] += cumulative / samples;
                    result.ImmediateCurve[use] += score / samples;
                }
                for (int i = 0; i < state.Length; i++)
                    for (int axis = 0; axis < 3; axis++)
                        result.FinalBaseMeans[i][axis] += (double)state[i].BaseStats[axis] / samples;
            }
            var sorted = new double[samples];
            for (int use = 0; use < uses; use++)
            {
                for (int sample = 0; sample < samples; sample++) sorted[sample] = result.SampleCumulative[sample][use];
                Array.Sort(sorted);
                // Nearest-rank empirical P10; deliberately no hidden risk coefficient.
                result.LowerCumulative[use] = sorted[Math.Max(0, (int)Math.Ceiling(samples * 0.1) - 1)];
            }
            result.MeanTotal = result.MeanCumulative[uses - 1];
            result.LowerTotal = result.LowerCumulative[uses - 1];
            return result;
        }

        private static int[] Totals(int width, LayoutItem[] state, IReadOnlyList<TemporalItem> items, bool[] alive)
        {
            int[][] stats = LayoutSimulation.Compute(width, state);
            var total = new int[3];
            for (int i = 0; i < state.Length; i++)
                if (alive[i] ? items[i].Counts : items[i].RuinedCounts)
                    for (int axis = 0; axis < 3; axis++) total[axis] = checked(total[axis] + stats[i][axis]);
            return total;
        }

        // Native first-fit: horizontal flip false/true; orientations 0,3,2,1; then y,x.
        // bayCells contains ALL legal bay cells, including currently occupied cells.
        private static LayoutItem PlaceRuin(int width, LayoutItem ruin, bool customShape,
            LayoutItem[] state, int removed, HashSet<int> legal, int maxY)
        {
            var occupied = new HashSet<int>();
            for (int i = 0; i < state.Length; i++)
                if (i != removed) foreach (int cell in state[i].Cells) occupied.Add(cell);
            int[] xs = customShape ? new int[ruin.Cells.Length] : new[] { 0, 1, 1 };
            int[] ys = customShape ? new int[ruin.Cells.Length] : new[] { 0, 0, 1 };
            if (customShape)
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                for (int i = 0; i < xs.Length; i++)
                {
                    xs[i] = ruin.Cells[i] % width; ys[i] = ruin.Cells[i] / width;
                    minX = Math.Min(minX, xs[i]); minY = Math.Min(minY, ys[i]);
                }
                for (int i = 0; i < xs.Length; i++) { xs[i] -= minX; ys[i] -= minY; }
            }
            int w = 0, h = 0;
            for (int i = 0; i < xs.Length; i++) { w = Math.Max(w, xs[i] + 1); h = Math.Max(h, ys[i] + 1); }
            int[] orientations = { 0, 3, 2, 1 };
            for (int flip = 0; flip < 2; flip++)
            {
                foreach (int orientation in orientations)
                {
                    int rw = orientation % 2 == 0 ? w : h;
                    int rh = orientation % 2 == 0 ? h : w;
                    var dx = new int[xs.Length];
                    var dy = new int[xs.Length];
                    for (int i = 0; i < xs.Length; i++)
                    {
                        int x = flip == 0 ? xs[i] : w - 1 - xs[i], y = ys[i];
                        switch (orientation)
                        {
                            case 1: dx[i] = y; dy[i] = w - 1 - x; break;
                            case 2: dx[i] = w - 1 - x; dy[i] = h - 1 - y; break;
                            case 3: dx[i] = h - 1 - y; dy[i] = x; break;
                            default: dx[i] = x; dy[i] = y; break;
                        }
                    }
                    for (int y = 0; y <= maxY - rh + 1; y++)
                    {
                        for (int x = 0; x <= width - rw; x++)
                        {
                            var cells = new int[xs.Length];
                            bool fits = cells.Length != 0;
                            for (int i = 0; i < cells.Length && fits; i++)
                            {
                                long encoded = (long)(y + dy[i]) * width + x + dx[i];
                                if (encoded > int.MaxValue) { fits = false; break; }
                                cells[i] = (int)encoded;
                                fits = legal.Contains(cells[i]) && !occupied.Contains(cells[i]);
                            }
                            if (!fits) continue;
                            LayoutItem placed = Copy(ruin);
                            placed.Cells = cells; placed.X = x; placed.Y = y;
                            return placed;
                        }
                    }
                }
            }
            // Failed reinsertion moves the ruin outside this bay; no automatic replacement.
            return new LayoutItem { Cells = Array.Empty<int>() };
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static LayoutItem Copy(LayoutItem item)
        {
            if (item == null || item.Cells == null || item.BaseStats == null || item.BaseStats.Length != 3 || item.Effects == null)
                throw new ArgumentException("Layouts require cells, three base stats and ordered effects.");
            return new LayoutItem
            {
                Cells = (int[])item.Cells.Clone(), BaseStats = (int[])item.BaseStats.Clone(),
                Effects = (string[])item.Effects.Clone(), X = item.X, Y = item.Y,
                IsNodeTag = item.IsNodeTag, IsModuleType = item.IsModuleType
            };
        }

        // A single geometric draw: P(first failed roll > n) = 0.9^n. The same
        // (physical id, sample) uses the same lifetime in every layout and sample-count prefix.
        private static int FirstFailure(int stableId, int sample)
        {
            unchecked
            {
                ulong x = ((ulong)(uint)stableId << 32) | (uint)sample;
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                x ^= x >> 31;
                double uniform = ((x >> 11) + 0.5) / 9007199254740992.0;
                return 1 + (int)(Math.Log(uniform) / Math.Log(0.9));
            }
        }
    }
}
