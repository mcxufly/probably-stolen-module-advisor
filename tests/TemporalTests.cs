using ModuleAdvisor;

internal static class TemporalTests
{
    public static readonly (string Name, Action Run)[] Cases =
    {
        ("temporal unchanged exact accumulation and Counts", Unchanged),
        ("temporal persisted learning caps, negative axes and existing growth", LearningCaps),
        ("temporal degrading floors and ordered effects", DegradingOrder),
        ("temporal before, after and mixed action timing", Timing),
        ("temporal geometric survival and sample convergence", Survival),
        ("temporal failure updates support and receiver topology", FailureTopology),
        ("temporal injected ruined snapshot and counting", InjectedRuin),
        ("temporal native ruined first-fit, orientation and no-space removal", NativeRuinPlacement),
        ("temporal deterministic sample prefixes and candidate order", Determinism),
        ("temporal machine caps prevent growth score inflation", MachineCaps),
        ("temporal input snapshots remain unchanged", SourceUnchanged),
        ("temporal callback and parameter validation", Validation)
    };

    private const string Learn = "MODULE_EFFECT_LEARNING_ALGORITHM";
    private const string Degrade = "MODULE_EFFECT_DEGRADING";
    private const string Overvolt = "MODULE_EFFECT_OVERVOLTED";
    private const string Receive = "MODULE_EFFECT_RECEIVER";
    private const string Support = "NODE_EFFECT_SUPPORT";

    private static TemporalItem Item(int id, int cell, int p, int e = 0, int q = 0, params string[] effects)
        => new TemporalItem
        {
            StableId = id,
            Layout = new LayoutItem { Cells = new[] { cell }, BaseStats = new[] { p, e, q }, Effects = effects }
        };

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Near(double actual, double expected, double tolerance = 1e-9)
        => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");

    private static void Vector(double[] actual, params double[] expected)
    {
        Check(actual.Length == expected.Length, "Vector lengths differ.");
        for (int i = 0; i < actual.Length; i++) Near(actual[i], expected[i]);
    }

    private static void Unchanged()
    {
        var a = Item(1, 0, 12, -3, 4);
        var b = Item(2, 1, 999);
        b.Counts = false;
        var result = TemporalSimulation.Run(3, new[] { a, b }, 5, s => s.Sum());
        Check(result.Samples == 1, "No random failures must use exactly one path.");
        Vector(result.MeanCumulative, 13, 26, 39, 52, 65);
        Vector(result.LowerCumulative, 13, 26, 39, 52, 65);
        Vector(result.ImmediateCurve, 13, 13, 13, 13, 13);
        Near(result.MeanTotal, 65);
        Near(result.LowerTotal, 65);
        Near(result.AnyFailureProbability, 0);
        Vector(result.Survival, 1, 1);
        var empty = TemporalSimulation.Run(3, Array.Empty<TemporalItem>(), 3, _ => -7);
        Vector(empty.MeanCumulative, -7, -14, -21);
    }

    private static void LearningCaps()
    {
        var item = Item(1, 0, 3, -2, 9, Learn);
        item.LearningCaps = new[] { 5, 0, 8 };
        var result = TemporalSimulation.Run(3, new[] { item }, 4, s => s.Sum());
        Vector(result.ImmediateCurve, 10, 12, 14, 14);
        Vector(result.FinalBaseMeans[0], 5, 0, 9); // Never reset existing growth or shrink above-cap values.
        var zero = Item(2, 1, 0, -3, 0, Learn);
        result = TemporalSimulation.Run(3, new[] { zero }, 5, s => s.Sum());
        Vector(result.ImmediateCurve, -3, -2, -1, 0, 0);
        Vector(result.FinalBaseMeans[0], 0, 0, 0);
    }

    private static void DegradingOrder()
    {
        var item = Item(1, 0, 2, -3, 0, Degrade);
        var result = TemporalSimulation.Run(3, new[] { item }, 4, s => s.Sum());
        Vector(result.ImmediateCurve, -1, -2, -3, -3);
        Vector(result.FinalBaseMeans[0], 0, -3, 0);
        item = Item(1, 0, 2, 0, 0, Learn, Degrade);
        item.LearningCaps[0] = 2;
        result = TemporalSimulation.Run(3, new[] { item }, 2, s => s[0]);
        Vector(result.FinalBaseMeans[0], 1, 0, 0);
        item.Layout.Effects = new[] { Degrade, Learn };
        result = TemporalSimulation.Run(3, new[] { item }, 2, s => s[0]);
        Vector(result.FinalBaseMeans[0], 2, 0, 0);
    }

    private static void Timing()
    {
        var item = Item(1, 0, 2, 3, 4, Learn);
        item.LearningCaps = new[] { 10, 10, 10 };
        var before = TemporalSimulation.Run(3, new[] { item }, 2, s => s.Sum());
        var after = TemporalSimulation.Run(3, new[] { item }, 2, s => s.Sum(), actionBeforeUtility: true);
        var mixed = TemporalSimulation.Run(3, new[] { item }, 2, s => s.Sum(), performanceAfterAction: true);
        Vector(before.ImmediateCurve, 9, 12);
        Vector(after.ImmediateCurve, 12, 15);
        Vector(mixed.ImmediateCurve, 10, 13);
    }

    private static void Survival()
    {
        var a = Item(321, 0, 100, 0, 0, Overvolt);
        var b = Item(123, 1, 100, 0, 0, Overvolt);
        var result = TemporalSimulation.Run(3, new[] { a, b }, 5, s => s[0], 16384, actionBeforeUtility: true);
        Vector(result.Survival, Math.Pow(0.9, 5), Math.Pow(0.9, 5));
        Near(result.AnyFailureProbability, 1 - Math.Pow(0.9, 10));
        Near(result.ImmediateCurve[4], 200 * Math.Pow(0.9, 5), 2);
        Near(result.MeanTotal, 200 * Enumerable.Range(1, 5).Sum(n => Math.Pow(0.9, n)), 5);
        Check(result.Samples == 16384 && result.RuinedPlacementApproximation, "Report estimate size and footprint approximation.");
        a.Layout.Effects = new[] { Overvolt, Overvolt };
        result = TemporalSimulation.Run(3, new[] { a }, 5, s => s[0], 16);
        Near(result.Survival[0], Math.Pow(0.9, 10));
        Near(result.AnyFailureProbability, 1 - Math.Pow(0.9, 10));
    }

    private static void FailureTopology()
    {
        var victim = Item(7, 0, 100, 0, 0, Overvolt);
        victim.Layout.IsModuleType = victim.Layout.IsNodeTag = true;
        victim.Counts = false;
        var receiver = Item(8, 1, 100, 0, 0, Receive);
        var support = Item(9, 3, 0, 0, 0, Support);
        support.Layout.IsNodeTag = true;
        var result = TemporalSimulation.Run(3, new[] { victim, receiver, support }, 100, s => s[0], 32, actionBeforeUtility: true);
        bool sawAlive = false, sawRuined = false;
        foreach (double[] path in result.SampleCumulative)
        {
            double previous = 0;
            foreach (double cumulative in path)
            {
                double score = cumulative - previous;
                Check(score == 130 || score == 100, "Failure must remove NODE receiver bonus and MODULE support contribution.");
                sawAlive |= score == 130;
                sawRuined |= score == 100;
                previous = cumulative;
            }
        }
        Check(sawAlive && sawRuined, "Both intact and ruined graph states should be exercised.");
    }

    private static void InjectedRuin()
    {
        var item = Item(7, 0, 100, 0, 0, Overvolt);
        item.Counts = false;
        item.RuinedLayout = new LayoutItem { Cells = new[] { 5 }, BaseStats = new[] { 7, 0, 0 }, IsModuleType = true };
        var result = TemporalSimulation.Run(3, new[] { item }, 100, s => s[0], 16, actionBeforeUtility: true);
        Check(!result.RuinedPlacementApproximation, "An explicit replacement snapshot should not be marked as inferred footprint.");
        Check(result.ImmediateCurve[99] > 6, "RuinedCounts defaults true independently of original Counts.");
        item.RuinedCounts = false;
        result = TemporalSimulation.Run(3, new[] { item }, 100, s => s[0], 16, actionBeforeUtility: true);
        Near(result.MeanTotal, 0);
    }

    private static void NativeRuinPlacement()
    {
        var victim = Item(7, 8, 100, 0, 0, Overvolt);
        victim.RuinedLayout = new LayoutItem
        {
            Cells = new[] { 0, 1, 4 }, BaseStats = new[] { 100, 0, 0 },
            Effects = new[] { "MODULE_EFFECT_TOP_MOUNT", "MODULE_EFFECT_SIDE_MOUNT" }, IsModuleType = true
        };
        var obstacle = Item(9, 0, 0);
        var result = TemporalSimulation.Run(3, new[] { victim, obstacle }, 100, s => s[0], 16,
            actionBeforeUtility: true, bayCells: Enumerable.Range(0, 9).ToArray());
        // Original (8) is released; native orientation0 first fits at x1,y0: top only.
        Near(result.ImmediateCurve[99], 120);
        Check(!result.RuinedPlacementApproximation, "Full legal mask enables first-fit replacement.");
        result = TemporalSimulation.Run(3, new[] { victim }, 100, s => s[0], 16,
            actionBeforeUtility: true, bayCells: new[] { 0, 3, 4 });
        // Only orientation2 fits this mask, at x0,y0; both directional effects apply.
        Near(result.ImmediateCurve[99], 140);
        result = TemporalSimulation.Run(3, new[] { victim }, 100, s => s[0], 16,
            actionBeforeUtility: true, bayCells: Array.Empty<int>());
        Near(result.ImmediateCurve[99], 0); // A locked/no-space bay receives no replacement.
        Near(result.FinalBaseMeans[0][0], 0);
    }

    private static void Determinism()
    {
        var a = Item(-21, 0, 100, 0, 0, Overvolt);
        var b = Item(999, 1, 40, 0, 0, Overvolt);
        var first = TemporalSimulation.Run(3, new[] { a, b }, 20, s => s[0], 16);
        var second = TemporalSimulation.Run(3, new[] { b, a }, 20, s => s[0], 32);
        for (int sample = 0; sample < 16; sample++)
            Vector(second.SampleCumulative[sample], first.SampleCumulative[sample]);
        // Relocating the same physical item does not change its first-failure path.
        var only = TemporalSimulation.Run(3, new[] { a }, 20, s => s[0], 16);
        a.Layout.Cells[0] = 8;
        var moved = TemporalSimulation.Run(3, new[] { a }, 20, s => s[0], 16);
        for (int sample = 0; sample < 16; sample++) Vector(moved.SampleCumulative[sample], only.SampleCumulative[sample]);
        var sorted = first.SampleCumulative.Select(p => p[19]).OrderBy(x => x).ToArray();
        Near(first.LowerTotal, sorted[1]); // Nearest-rank P10 of 16 paths.
    }

    private static void MachineCaps()
    {
        var model = new MachineModel { Id = "security_alarm" };
        var item = Item(1, 0, 100, 0, 0, Learn);
        item.LearningCaps[0] = 200;
        var result = TemporalSimulation.Run(3, new[] { item }, 100, s => model.Measure(s).Score(0));
        Near(result.MeanTotal, 10000);
        Near(result.FinalBaseMeans[0][0], 200);
    }

    private static void SourceUnchanged()
    {
        var item = Item(1, 0, 3, -2, 4, Learn, Overvolt);
        item.LearningCaps = new[] { 7, 0, 8 };
        item.RuinedLayout = new LayoutItem { Cells = new[] { 5 }, BaseStats = new[] { 0, 0, 0 } };
        var result = TemporalSimulation.Run(3, new[] { item }, 100, s => { double score = s.Sum(); s[0] = 9999; return score; }, 32);
        Check(item.Layout.BaseStats.SequenceEqual(new[] { 3, -2, 4 }), "Source base mutated.");
        Check(item.Layout.Cells.SequenceEqual(new[] { 0 }) && item.RuinedLayout.Cells[0] == 5, "Source shape mutated.");
        Check(item.LearningCaps.SequenceEqual(new[] { 7, 0, 8 }), "Source caps mutated.");
        Check(item.Layout.Effects.SequenceEqual(new[] { Learn, Overvolt }), "Source effects mutated.");
        result.FinalBaseMeans[0][0] = 999;
        Check(item.Layout.BaseStats[0] == 3, "Result aliases input.");
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Validation()
    {
        var items = new[] { Item(1, 0, 10) };
        Throws<ArgumentOutOfRangeException>(() => TemporalSimulation.Run(0, items, 1, _ => 0));
        Throws<ArgumentNullException>(() => TemporalSimulation.Run(3, null, 1, _ => 0));
        Throws<ArgumentNullException>(() => TemporalSimulation.Run(3, items, 1, null));
        foreach (int uses in new[] { 0, 101 })
            Throws<ArgumentOutOfRangeException>(() => TemporalSimulation.Run(3, items, uses, _ => 0));
        foreach (int samples in new[] { 0, -1, 65537 })
            Throws<ArgumentOutOfRangeException>(() => TemporalSimulation.Run(3, items, 1, _ => 0, samples));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => double.NaN));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => double.PositiveInfinity));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 2, _ => double.MaxValue));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, new[] { items[0], items[0] }, 1, _ => 0));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => 0, bayCells: new[] { -1 }));
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => 0, bayCells: new[] { 0, 0 }));
        items[0].LearningCaps = new int[2];
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => 0));
        items[0].LearningCaps = new int[3];
        items[0].Layout.Cells = new[] { -1 };
        Throws<ArgumentException>(() => TemporalSimulation.Run(3, items, 1, _ => 0));
    }
}
