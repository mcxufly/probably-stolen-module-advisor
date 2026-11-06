using System.Diagnostics;
using ModuleAdvisor;

internal static class LayoutTests
{
    public static readonly (string Name, Action Run)[] Cases =
    {
        ("simulation support uses neighboring MODULE base, not NODE tag", Support),
        ("simulation receiver uses NODE tag and one integer division", Receiver),
        ("simulation negative feedback sums negative base per axis", NegativeFeedback),
        ("simulation directional and override effects retain tag order", EffectOrder),
        ("simulation nonrectangular orthogonal adjacency and row boundaries", Shapes),
        ("simulation resets base, totals and inputs remain unchanged", SimulationInvariance),
        ("dynamic planner adds node to improve existing module", ExistingReceiver),
        ("dynamic planner support node and new module interact", SupportPlan),
        ("dynamic planner two zero or negative singles activate together", Threshold),
        ("dynamic planner saturation removes redundant additions", Saturation),
        ("dynamic planner preserves spatial alternatives", SpatialAlternatives),
        ("dynamic planner uniqueness across bays and negative baseline", DynamicConstraints),
        ("dynamic planner invalid input and callback values", Validation),
        ("dynamic planner randomized determinism and nonredundancy", Randomized),
        ("dynamic planner hard callback budget retains best", Budget)
    };

    private const string Top = "MODULE_EFFECT_TOP_MOUNT";
    private const string Side = "MODULE_EFFECT_SIDE_MOUNT";
    private const string Receive = "MODULE_EFFECT_RECEIVER";
    private const string Negative = "MODULE_EFFECT_NEGATIVE_FEEDBACK";
    private const string NodeSupport = "NODE_EFFECT_SUPPORT";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static LayoutItem Item(int cell, int p, int e = 0, int q = 0, params string[] effects)
        => new LayoutItem { Cells = new[] { cell }, BaseStats = new[] { p, e, q }, Effects = effects };

    private static Placement P(int module, int cell, int bay = 0)
        => new Placement { Module = module, Bay = bay, Cells = new[] { cell } };

    private static void Equal(int[] actual, params int[] expected)
        => Check(actual.SequenceEqual(expected), $"Expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");

    private static void Support()
    {
        LayoutItem node = Item(4, 999, 999, 999, NodeSupport);
        node.IsNodeTag = true;
        LayoutItem module = Item(5, 11, -7, 29, Top);
        module.IsModuleType = true;
        LayoutItem otherNode = Item(3, 90, 90, 90);
        otherNode.IsNodeTag = true;
        var result = LayoutSimulation.Compute(3, new[] { node, module, otherNode });
        Equal(result[0], 2, -1, 5);
        Equal(result[1], 13, -8, 34);
        // SUPPORT excludes itself even if a custom item carries both types.
        node.IsModuleType = true;
        node.Cells = new[] { 3, 4 };
        Equal(LayoutSimulation.Compute(3, new[] { node, module })[0], 2, -1, 5);
    }

    private static void Receiver()
    {
        LayoutItem receiver = Item(4, 9, -9, 19, Receive);
        LayoutItem a = Item(1, 100), b = Item(5, 100), notNode = Item(7, 100);
        a.IsNodeTag = b.IsNodeTag = true;
        notNode.IsModuleType = true;
        Equal(LayoutSimulation.Compute(3, new[] { receiver, a, b, notNode })[0], 10, -10, 22);
        receiver.IsNodeTag = true;
        receiver.Cells = new[] { 3, 4 };
        Equal(LayoutSimulation.Compute(3, new[] { receiver })[0], 9, -9, 20);
    }

    private static void NegativeFeedback()
    {
        LayoutItem receiver = Item(4, 10, 10, -10, Negative);
        LayoutItem a = Item(1, -3, 100, -3), b = Item(5, -4, -7, 100);
        Equal(LayoutSimulation.Compute(3, new[] { receiver, a, b })[0], 9, 9, -10);
        receiver.Cells = new[] { 3, 4 };
        Equal(LayoutSimulation.Compute(3, new[] { receiver })[0], 10, 10, -10);
    }

    private static void EffectOrder()
    {
        LayoutItem item = Item(0, 9, -9, 19, Top, Side);
        Equal(LayoutSimulation.Compute(3, new[] { item })[0], 11, -11, 25);
        item.X = 1;
        Equal(LayoutSimulation.Compute(3, new[] { item })[0], 10, -10, 22);
        item.X = -1;
        item.Y = -1;
        Equal(LayoutSimulation.Compute(3, new[] { item })[0], 11, -11, 25);
        LayoutItem module = Item(1, 50, 30, 10);
        module.IsModuleType = true;
        item.Effects = new[] { Top, NodeSupport };
        Equal(LayoutSimulation.Compute(3, new[] { item, module })[0], 10, 6, 2);
        item.Effects = new[] { NodeSupport, Top };
        Equal(LayoutSimulation.Compute(3, new[] { item, module })[0], 11, 5, 5);
    }

    private static void Shapes()
    {
        LayoutItem lShape = Item(0, 100, 0, 0, Receive);
        lShape.Cells = new[] { 0, 1, 4 }; // Hole at 5, not its bounding rectangle.
        LayoutItem node = Item(6, 0);
        node.IsNodeTag = true;
        Equal(LayoutSimulation.Compute(4, new[] { lShape, node })[0], 100, 0, 0); // diagonal to 1; bounding-box hole 5 must not count
        node.Cells = new[] { 5 };
        Equal(LayoutSimulation.Compute(4, new[] { lShape, node })[0], 110, 0, 0); // two edges, one item
        lShape.Cells = new[] { 3 };
        node.Cells = new[] { 4 };
        Equal(LayoutSimulation.Compute(4, new[] { lShape, node })[0], 100, 0, 0); // no row wrapping
    }

    private static void SimulationInvariance()
    {
        LayoutItem item = Item(0, 15, -15, 9, Top, Side);
        var items = new[] { item };
        int[][] first = LayoutSimulation.Compute(3, items);
        Equal(first[0], 21, -21, 11);
        first[0][0] = 0;
        Equal(LayoutSimulation.Compute(3, items)[0], 21, -21, 11);
        Equal(item.BaseStats, 15, -15, 9);
        Equal(item.Cells, 0);
        Equal(LayoutSimulation.Totals(3, items), 21, -21, 11);
        Equal(LayoutSimulation.Totals(new[] { new[] { 2, 3, 4 }, new[] { -1, 5, 6 } }), 1, 8, 10);
        Equal(LayoutSimulation.Totals(3, Array.Empty<LayoutItem>()), 0, 0, 0);
    }

    private static void ExistingReceiver()
    {
        var existing = Item(4, 100, 0, 0, Receive);
        Func<IReadOnlyList<Placement>, double> utility = placements =>
        {
            var items = new List<LayoutItem> { existing };
            foreach (Placement p in placements)
            {
                var node = Item(p.Cells[0], 0);
                node.IsNodeTag = true;
                items.Add(node);
            }
            return LayoutSimulation.Totals(3, items)[0];
        };
        Placement[] options = { P(0, 0), P(0, 1), P(0, 8) };
        Plan plan = LayoutPlanner.Solve(1, options, utility, new HashSet<int> { 0 });
        Check(plan.Score == 110 && plan.Placements.Single().Cells[0] == 1, "Node must improve a pre-existing receiver.");
    }

    private static void SupportPlan()
    {
        Func<IReadOnlyList<Placement>, double> utility = placements =>
        {
            var items = new List<LayoutItem>();
            foreach (Placement p in placements)
            {
                var item = Item(p.Cells[0], p.Module == 0 ? 20 : 0);
                item.IsModuleType = p.Module == 0;
                item.IsNodeTag = p.Module == 1;
                if (p.Module == 1) item.Effects = new[] { NodeSupport };
                items.Add(item);
            }
            return LayoutSimulation.Totals(4, items)[0];
        };
        Placement[] options = { P(0, 0), P(1, 1), P(1, 3) };
        Plan plan = LayoutPlanner.Solve(2, options, utility, new HashSet<int> { 1 });
        Check(plan.Score == 24 && plan.Placements.Count == 2, "Module then support node should beat either standalone.");
    }

    private static void Threshold()
    {
        foreach (double standalone in new[] { 0.0, -1.0 })
        {
            Placement[] options = { P(0, 0), P(1, 1) };
            double Utility(IReadOnlyList<Placement> p) => p.Count == 2 ? 8 : p.Count == 0 ? 0 : standalone;
            Plan plan = LayoutPlanner.Solve(2, options, Utility, new HashSet<int>());
            Check(plan.Score == 8 && plan.Placements.Count == 2, "Do not prune nonpositive candidates before combination.");
            AssertDynamic(options, plan, Utility);
        }
    }

    private static void Saturation()
    {
        Placement[] options = Enumerable.Range(0, 10).Select(m => P(m, m)).ToArray();
        double Utility(IReadOnlyList<Placement> p) => 100 + Math.Min(10, p.Count * 10);
        Plan plan = LayoutPlanner.Solve(10, options, Utility, new HashSet<int>());
        Check(plan.Score == 110 && plan.Placements.Count == 1, "Capped score must not accumulate useless modules.");
        AssertDynamic(options, plan, Utility);
        // Deletions change other items' redundancy; cleanup must restart after each deletion.
        double Nonmonotonic(IReadOnlyList<Placement> p) => p.Count == 2 || p.Count == 3 ? 5 : 0;
        plan = LayoutPlanner.Solve(4, options.Take(4).ToArray(), Nonmonotonic, new HashSet<int>());
        Check(plan.Score == 5 && plan.Placements.Count == 2, "Keep useful pairs, remove redundant extras.");
    }

    private static void SpatialAlternatives()
    {
        Placement[] options = Enumerable.Range(0, 100).Select(c => P(0, c)).Append(P(1, 100)).ToArray();
        double Utility(IReadOnlyList<Placement> p)
        {
            if (p.Count == 2) return p.Any(x => x.Module == 0 && x.Cells[0] == 99) ? 100 : 1;
            return p.Count == 1 && p[0].Cells[0] == 0 ? 2 : 0;
        }
        Plan plan = LayoutPlanner.Solve(2, options, Utility, new HashSet<int> { 1 });
        Check(plan.Score == 100, "Keep an alternative position, not only the best standalone spot.");
    }

    private static void DynamicConstraints()
    {
        Placement[] options = { P(0, 0, 0), P(0, 0, 1), P(1, 0, 0), P(1, 0, 1) };
        double Utility(IReadOnlyList<Placement> p) => -20 + p.Count * 3;
        Plan plan = LayoutPlanner.Solve(2, options, Utility, new HashSet<int>());
        Check(plan.Score == -14 && plan.Placements.Count == 2, "Score is absolute utility, not a sum or a zero clamp.");
        AssertDynamic(options, plan, Utility);
        plan = LayoutPlanner.Solve(2, options, p => 7 - p.Count, new HashSet<int>());
        Check(plan.Score == 7 && plan.Placements.Count == 0, "Preserve the empty absolute baseline.");
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Validation()
    {
        var nodes = new HashSet<int>();
        Throws<ArgumentOutOfRangeException>(() => LayoutSimulation.Compute(0, Array.Empty<LayoutItem>()));
        Throws<ArgumentException>(() => LayoutSimulation.Compute(3, new[] { new LayoutItem { BaseStats = new int[2] } }));
        Throws<ArgumentOutOfRangeException>(() => LayoutPlanner.Solve(-1, Array.Empty<Placement>(), _ => 0, nodes));
        Throws<ArgumentNullException>(() => LayoutPlanner.Solve(1, null, _ => 0, nodes));
        Throws<ArgumentNullException>(() => LayoutPlanner.Solve(1, Array.Empty<Placement>(), null, nodes));
        Throws<ArgumentNullException>(() => LayoutPlanner.Solve(1, Array.Empty<Placement>(), _ => 0, null));
        Throws<ArgumentException>(() => LayoutPlanner.Solve(1, new[] { P(1, 0) }, _ => 0, nodes));
        Throws<ArgumentException>(() => LayoutPlanner.Solve(1, new[] { P(0, -1) }, _ => 0, nodes));
        Throws<ArgumentException>(() => LayoutPlanner.Solve(1, Array.Empty<Placement>(), _ => double.NaN, nodes));
        Throws<ArgumentException>(() => LayoutPlanner.Solve(1, Array.Empty<Placement>(), _ => double.PositiveInfinity, nodes));
    }

    private static string Signature(Plan plan)
        => plan.Score + ":" + string.Join(';', plan.Placements.Select(p => $"{p.Module}/{p.Bay}/{string.Join(',', p.Cells)}"));

    private static void AssertDynamic(IReadOnlyList<Placement> options, Plan plan,
        Func<IReadOnlyList<Placement>, double> utility)
    {
        var modules = new HashSet<int>();
        var occupied = new HashSet<(int, int)>();
        foreach (Placement p in plan.Placements)
        {
            Check(options.Contains(p) && modules.Add(p.Module), "Placement must come from input, once per physical module.");
            foreach (int cell in p.Cells) Check(occupied.Add((p.Bay, cell)), "Overlapping layout.");
            Check(utility(plan.Placements.Where(x => x != p).ToArray()) < plan.Score, "Removable redundant addition remains.");
        }
        Check(plan.Score == utility(plan.Placements), "Stored score does not equal full-layout evaluation.");
        Check(plan.Score >= utility(Array.Empty<Placement>()), "Plan regressed below empty baseline.");
    }

    private static void Randomized()
    {
        var random = new Random(7012);
        for (int trial = 0; trial < 80; trial++)
        {
            int count = random.Next(2, 12);
            var options = new List<Placement>();
            for (int m = 0; m < count; m++)
                for (int i = 0; i < 8; i++) options.Add(P(m, random.Next(20), random.Next(3)));
            string[] before = options.Select(p => $"{p.Module}/{p.Bay}/{string.Join(',', p.Cells)}").ToArray();
            double Utility(IReadOnlyList<Placement> p)
            {
                double value = -5;
                foreach (Placement a in p) value += a.Module % 3 - 1;
                foreach (Placement a in p)
                    foreach (Placement b in p)
                        if (a.Module < b.Module && a.Bay == b.Bay && Math.Abs(a.Cells[0] - b.Cells[0]) == 1) value += 4;
                return Math.Min(value, 12);
            }
            var nodes = new HashSet<int>(Enumerable.Range(0, count).Where(m => m % 3 == 0));
            Plan plan = LayoutPlanner.Solve(count, options, Utility, nodes);
            AssertDynamic(options, plan, Utility);
            string signature = Signature(plan);
            Check(signature == Signature(LayoutPlanner.Solve(count, options, Utility, nodes)), "Repeated solve differs.");
            var shuffled = options.OrderBy(_ => random.Next()).ToArray();
            Check(signature == Signature(LayoutPlanner.Solve(count, shuffled, Utility, nodes)), "Shuffled input changes deterministic result.");
            Check(before.SequenceEqual(options.Select(p => $"{p.Module}/{p.Bay}/{string.Join(',', p.Cells)}")), "Input was mutated.");
        }
    }

    private static void Budget()
    {
        const int modules = 60;
        var options = new List<Placement>();
        for (int m = 0; m < modules; m++)
            for (int c = 0; c < 1000; c++) options.Add(P(m, c, m));
        int calls = 0;
        double Utility(IReadOnlyList<Placement> p)
        {
            calls++;
            return p.Sum(x => 1.0 + x.Cells[0]);
        }
        var watch = Stopwatch.StartNew();
        Plan plan = LayoutPlanner.Solve(modules, options, Utility, new HashSet<int>());
        watch.Stop();
        int used = calls;
        Check(used <= 30000, "Hard callback budget exceeded: " + used);
        Check(plan.Score >= 1000 && plan.Placements.Count > 1, "Budget exhaustion lost evaluated improvements.");
        AssertDynamic(options, plan, Utility);
        Check(watch.Elapsed < TimeSpan.FromSeconds(20), "Bounded layout search took too long.");
        Console.WriteLine($"  dynamic search: {used:N0} evaluations, {watch.Elapsed.TotalMilliseconds:N0} ms");
    }
}
