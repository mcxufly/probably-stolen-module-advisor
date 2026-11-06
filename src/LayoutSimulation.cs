using System;
using System.Collections.Generic;

namespace ModuleAdvisor
{
    /// <summary>A snapshot of one item's occupied cells and already-rolled base stats.</summary>
    public sealed class LayoutItem
    {
        public int[] Cells = Array.Empty<int>();
        public int X, Y;
        public int[] BaseStats = new int[3];
        public bool IsNodeTag, IsModuleType;
        public string[] Effects = Array.Empty<string>();
    }

    public static class LayoutSimulation
    {
        /// <summary>
        /// Computes P/E/Q temp stats for one bay without changing the snapshots.
        /// Cells encode y * width + x; only orthogonal occupied edges are adjacent.
        /// Effects are the ordered values of the game's two effect tags.
        /// Callers must pass ordinary (value 1) occupied shape cells, not bounding boxes.
        /// </summary>
        public static int[][] Compute(int width, IReadOnlyList<LayoutItem> items)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (items == null) throw new ArgumentNullException(nameof(items));
            var occupied = new HashSet<int>[items.Count];
            var result = new int[items.Count][];
            for (int i = 0; i < items.Count; i++)
            {
                LayoutItem item = items[i];
                if (item == null || item.Cells == null || item.BaseStats == null ||
                    item.BaseStats.Length != 3 || item.Effects == null)
                    throw new ArgumentException("Items require cells, three base stats and ordered effects.", nameof(items));
                occupied[i] = new HashSet<int>();
                foreach (int cell in item.Cells)
                    if (cell < 0 || !occupied[i].Add(cell))
                        throw new ArgumentException("Cells must be nonnegative and distinct.", nameof(items));
                result[i] = (int[])item.BaseStats.Clone();
            }

            for (int i = 0; i < items.Count; i++)
            {
                LayoutItem item = items[i];
                int nodeCount = 0;
                var negative = new long[3];
                var support = new long[3];
                for (int j = 0; j < items.Count; j++)
                {
                    if (!Adjacent(width, occupied[i], occupied[j])) continue;
                    // RECEIVER's native tag query can include itself for a multi-cell shape.
                    if (items[j].IsNodeTag) nodeCount++;
                    if (i == j) continue;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int value = items[j].BaseStats[axis];
                        if (value < 0) negative[axis] += value;
                        if (items[j].IsModuleType) support[axis] += value;
                    }
                }
                foreach (string effect in item.Effects)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        long basis = item.BaseStats[axis];
                        switch (effect)
                        {
                            case "MODULE_EFFECT_TOP_MOUNT":
                                if (item.Y <= 0) result[i][axis] += (int)(basis * 20 / 100);
                                break;
                            case "MODULE_EFFECT_SIDE_MOUNT":
                                if (item.X <= 0) result[i][axis] += (int)(basis * 20 / 100);
                                break;
                            case "MODULE_EFFECT_RECEIVER":
                                result[i][axis] += (int)(basis * (10L * nodeCount) / 100);
                                break;
                            case "MODULE_EFFECT_NEGATIVE_FEEDBACK":
                                result[i][axis] += (int)(negative[axis] / 4);
                                break;
                            case "NODE_EFFECT_SUPPORT":
                                result[i][axis] = (int)(support[axis] / 5);
                                break;
                        }
                    }
                }
            }
            return result;
        }

        public static int[] Totals(int width, IReadOnlyList<LayoutItem> items)
        {
            return Totals(Compute(width, items));
        }

        /// <summary>Sum supplied temp stats; the caller controls which items qualify for totals.</summary>
        public static int[] Totals(IReadOnlyList<int[]> stats)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            var total = new int[3];
            foreach (int[] item in stats)
            {
                if (item == null || item.Length != 3)
                    throw new ArgumentException("Each stat vector must contain P/E/Q.", nameof(stats));
                for (int axis = 0; axis < 3; axis++) total[axis] += item[axis];
            }
            return total;
        }

        private static bool Adjacent(int width, HashSet<int> a, HashSet<int> b)
        {
            foreach (int cell in a)
            {
                int x = cell % width;
                if ((x > 0 && b.Contains(cell - 1)) ||
                    (x + 1 < width && cell < int.MaxValue && b.Contains(cell + 1)) ||
                    (cell >= width && b.Contains(cell - width)) ||
                    (cell <= int.MaxValue - width && b.Contains(cell + width))) return true;
            }
            return false;
        }
    }
}
