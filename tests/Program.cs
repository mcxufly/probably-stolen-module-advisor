using System.Diagnostics;
using ModuleAdvisor;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("empty input and incompatible modules", EmptyAndIncompatible),
            ("one placement per physical module", ModuleUniqueness),
            ("cells only conflict within the same bay", BayIsolation),
            ("overlapping multi-cell shapes", Overlap),
            ("nonpositive scores and skip branches", LeaveEmpty),
            ("constrained modules keep their only location", ConstrainedFirst),
            ("deterministic results without input mutation", Determinism),
            ("invalid input rejected", Validation),
            ("200 randomized invariant checks", Randomized),
            ("large candidate budget preserves and completes plans", Budget)
        }.Concat(LayoutTests.Cases).ToArray();
        try
        {
            foreach (var test in tests)
            {
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }
            Console.WriteLine($"All {tests.Length} tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Placement P(int module, int bay, double score, params int[] cells)
        => new Placement { Module = module, Bay = bay, Score = score, Cells = cells };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void EmptyAndIncompatible()
    {
        Check(Planner.Solve(0, Array.Empty<Placement>()).Placements.Count == 0, "Empty plan expected.");
        Plan empty = Planner.Solve(4, Array.Empty<Placement>());
        Check(empty.Score == 0 && empty.Placements.Count == 0, "Incompatible modules have no candidates.");
        Placement[] options = { P(2, 0, 3, 0) };
        Plan plan = Planner.Solve(4, options);
        AssertValid(4, options, plan);
        Check(plan.Placements.Single().Module == 2, "Modules without compatible machines must stay absent.");
    }

    private static void ModuleUniqueness()
    {
        Placement[] options = { P(0, 0, 2, 0), P(0, 1, 7, 0), P(0, 2, 5, 1) };
        Plan plan = Planner.Solve(1, options);
        AssertValid(1, options, plan);
        Check(plan.Placements.Count == 1 && plan.Score == 7, "Choose the best single placement.");
    }

    private static void BayIsolation()
    {
        Placement[] options = { P(0, 0, 2, 0, 1), P(1, 1, 3, 0, 1) };
        Plan plan = Planner.Solve(2, options);
        AssertValid(2, options, plan);
        Check(plan.Placements.Count == 2 && plan.Score == 5, "Different machines have independent cells.");
    }

    private static void Overlap()
    {
        Placement[] options = { P(0, 0, 4, 0, 1, 4), P(1, 0, 8, 1, 2), P(2, 0, 3, 5, 6) };
        Plan plan = Planner.Solve(3, options);
        AssertValid(3, options, plan);
        Check(plan.Score == 11, "A shared cell must exclude the lower scoring shape.");
    }

    private static void LeaveEmpty()
    {
        Placement[] options = { P(0, 0, -4, 0), P(1, 0, 0, 1) };
        Plan plan = Planner.Solve(2, options);
        AssertValid(2, options, plan);
        Check(plan.Score == 0 && plan.Placements.Count == 0, "Empty is better than nonpositive score.");
        options = new[] { P(0, 0, 1, 0), P(1, 0, 10, 0) };
        plan = Planner.Solve(2, options);
        AssertValid(2, options, plan);
        Check(plan.Score == 10 && plan.Placements.Single().Module == 1,
            "Retain a skip branch even when an earlier placement fits.");
    }

    private static void ConstrainedFirst()
    {
        Placement[] options = { P(0, 0, 10, 0), P(0, 0, 9, 1), P(1, 0, 8, 0) };
        Plan plan = Planner.Solve(2, options);
        AssertValid(2, options, plan);
        Check(plan.Score == 17 && plan.Placements.Count == 2,
            "A flexible module must use its alternative and leave the unique cell available.");
        Check(plan.Placements[0].Module == 1, "The constrained module should be processed first.");
    }

    private static string Signature(Plan plan)
        => plan.Score + ":" + string.Join(";", plan.Placements.Select(p =>
            $"{p.Module}/{p.Bay}/{p.X}/{p.Y}/{p.Rotation}/{p.Flipped}/{string.Join(',', p.Cells)}"));

    private static void Determinism()
    {
        var options = new List<Placement>
        {
            P(0, 1, 2, 1), P(0, 0, 2, 0), P(1, 0, 2, 0), P(1, 1, 2, 1),
            P(2, 1, 1, 4, 2), P(2, 0, 1, 4, 2)
        };
        options[0].X = 4;
        options[0].Y = 9;
        options[0].Rotation = 270;
        options[0].Flipped = true;
        Placement[] original = options.ToArray();
        int[][] originalCells = options.Select(p => p.Cells.ToArray()).ToArray();
        string expected = Signature(Planner.Solve(3, options));
        var random = new Random(912);
        for (int i = 0; i < 30; i++)
        {
            Check(Signature(Planner.Solve(3, original)) == expected, "Repeated calls must agree.");
            var shuffled = original.OrderBy(_ => random.Next()).ToArray();
            Check(Signature(Planner.Solve(3, shuffled)) == expected, "Input ordering must not change ties.");
        }
        Check(options.SequenceEqual(original), "Do not reorder the caller's list.");
        for (int i = 0; i < original.Length; i++)
            Check(original[i].Cells.SequenceEqual(originalCells[i]), "Do not reorder cells.");
        Check(options[0].Rotation == 270 && options[0].Flipped, "Preserve opaque metadata.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void Validation()
    {
        Throws<ArgumentOutOfRangeException>(() => Planner.Solve(-1, Array.Empty<Placement>()));
        Throws<ArgumentNullException>(() => Planner.Solve(1, null));
        Throws<ArgumentException>(() => Planner.Solve(1, new Placement[] { null }));
        foreach (Placement invalid in new[]
        {
            P(-1, 0, 1, 0), P(1, 0, 1, 0), P(0, -1, 1, 0), P(0, 0, 1, -1),
            P(0, 0, 1), P(0, 0, 1, 0, 0), P(0, 0, double.NaN, 0),
            P(0, 0, double.PositiveInfinity, 0), P(0, 0, double.NegativeInfinity, 0),
            new Placement { Cells = null, Score = 1 }, P(-1, 0, -5, 0)
        })
            Throws<ArgumentException>(() => Planner.Solve(1, new[] { invalid }));
        Placement[] sparse = { P(0, int.MaxValue, 1, int.MaxValue), P(1, 0, 2, int.MaxValue) };
        Plan plan = Planner.Solve(int.MaxValue, sparse);
        AssertValid(int.MaxValue, sparse, plan);
        Check(plan.Score == 3, "Sparse module and bay ids must not allocate dense arrays.");
    }

    // This verifier knows only the public contract, not search ordering or beam internals.
    private static void AssertValid(int moduleCount, IReadOnlyList<Placement> options, Plan plan)
    {
        var modules = new HashSet<int>();
        var occupied = new HashSet<(int Bay, int Cell)>();
        double sum = 0;
        foreach (Placement p in plan.Placements)
        {
            Check(options.Any(candidate => ReferenceEquals(candidate, p)), "Chosen placement must come from input.");
            Check(p.Module >= 0 && p.Module < moduleCount && modules.Add(p.Module), "Module selected twice or out of range.");
            Check(p.Score > 0 && double.IsFinite(p.Score), "Only finite positive gains can be selected.");
            foreach (int cell in p.Cells)
                Check(occupied.Add((p.Bay, cell)), "Selected placements overlap in one bay.");
            sum += p.Score;
        }
        Check(plan.Score >= 0 && Math.Abs(sum - plan.Score) <= 1e-9 * Math.Max(1, sum), "Plan score must equal its placements.");
    }

    private static void Randomized()
    {
        var random = new Random(55281);
        for (int trial = 0; trial < 200; trial++)
        {
            int moduleCount = random.Next(1, 13);
            var options = new List<Placement>();
            for (int module = 0; module < moduleCount; module++)
            {
                int count = random.Next(0, 10);
                for (int i = 0; i < count; i++)
                {
                    int[] cells = Enumerable.Range(0, 20).OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToArray();
                    options.Add(P(module, random.Next(3), random.Next(-5, 21) / 2.0, cells));
                }
            }
            Plan plan = Planner.Solve(moduleCount, options);
            AssertValid(moduleCount, options, plan);
            Check(Signature(Planner.Solve(moduleCount, options)) == Signature(plan), "Random input must remain deterministic.");
        }
    }

    private static void Budget()
    {
        const int modules = 60;
        const int perModule = 5000;
        var options = new List<Placement>(modules * perModule);
        for (int module = 0; module < modules; module++)
            for (int location = 0; location < perModule; location++)
                options.Add(P(module, module, location + 1, location));
        var watch = Stopwatch.StartNew();
        Plan plan = Planner.Solve(modules, options);
        watch.Stop();
        AssertValid(modules, options, plan);
        Check(plan.Placements.Count == modules && plan.Score == modules * perModule,
            "Budget fallback must retain the best prefix and complete every remaining module.");
        Check(watch.Elapsed < TimeSpan.FromSeconds(20), "Large candidate search exceeded a generous 20 second bound.");
        Console.WriteLine($"  {options.Count:N0} candidates in {watch.Elapsed.TotalMilliseconds:N0} ms");
    }
}
