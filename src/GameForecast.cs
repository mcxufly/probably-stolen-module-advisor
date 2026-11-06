using System;
using System.Collections.Generic;
using System.Linq;

namespace ModuleAdvisor
{
    internal sealed class TemporalSummary
    {
        internal double[] Mean, Lower;
        internal double AnyFailureProbability;
        internal int Samples;
        internal bool Approximation;
        internal double Score(int mode) => mode == 1 ? Lower[Lower.Length - 1]
            : mode == 3 ? Mean[Math.Min(5, Mean.Length) - 1] : Mean[Mean.Length - 1];
    }

    internal sealed partial class GameEvaluation
    {
        private readonly Dictionary<string, ForecastResult> forecastCache = new Dictionary<string, ForecastResult>();

        private TemporalItem ToTemporal(GameItem source, LayoutItem layout, bool counts)
        {
            return new TemporalItem { Layout = layout, Counts = counts, StableId = stableIds[source],
                LearningCaps = new[] {
                    RawCap(source, ModuleEffectHelper.LEARNING_ALGO_MAX_PERFORMANCE_INT),
                    RawCap(source, ModuleEffectHelper.LEARNING_ALGO_MAX_EFFICIENCY_INT),
                    RawCap(source, ModuleEffectHelper.LEARNING_ALGO_MAX_QUALITY_INT) } };
        }
        private static int RawCap(GameItem source, string key) =>
            source.modifiedState.dict.TryGetValue(key, out var tag) ? tag.GetInt() : 0;

        internal ForecastResult ForecastBay(int bay, IReadOnlyList<Placement> placements, int uses, int samples)
        {
            var additions = placements.Where(p => p.Bay == bay).OrderBy(p => p.Module).ToList();
            string key = bay + ":" + uses + ":" + samples + ":" + string.Join(";", additions.Select(p =>
                p.Module + "," + p.X + "," + p.Y + "," + p.Rotation + "," + p.Flipped));
            if (forecastCache.TryGetValue(key, out var cached)) return cached;
            var items = new List<TemporalItem>();
            for (int i = 0; i < existing[bay].Count; i++)
                items.Add(ToTemporal(existingSources[bay][i], existing[bay][i], counted[bay][i]));
            foreach (var p in additions) items.Add(ToTemporal(candidateSources[p], candidates[p], true));
            if (bays[bay].Machine.identifier == "deal_maker")
                foreach (var item in items)
                {
                    var layout = item.Layout;
                    item.Layout = new LayoutItem { Cells = layout.Cells, X = layout.X, Y = layout.Y,
                        BaseStats = layout.BaseStats, IsNodeTag = layout.IsNodeTag, IsModuleType = layout.IsModuleType,
                        Effects = layout.Effects.Where(e => e != ModuleEffectHelper.MODULE_EFFECT_LEARNING_ALGORITHM
                            && e != ModuleEffectHelper.MODULE_EFFECT_DEGRADING && e != ModuleEffectHelper.MODULE_EFFECT_OVERVOLTED).ToArray() };
                }
            var legal = new List<int>();
            string machineId = bays[bay].Machine.identifier;
            // Working growth/printing phases have a locked module bay; completed cycles can differ.
            bool insertionLocked = machineId == "hydroponic" || machineId == "printer" || machineId == "plant_dna_manipulator"
                || bays[bay].Grid.IsInsertLocked();
            if (!insertionLocked)
                for (int y = 0; y < bays[bay].Grid.inventoryShape.height; y++)
                    for (int x = 0; x < bays[bay].Grid.inventoryShape.width; x++)
                        if (bays[bay].Grid.inventoryShape.Get(x, y) == 0) legal.Add(y * bays[bay].Grid.inventoryShape.width + x);
            var forecast = TemporalSimulation.Run(bays[bay].Grid.inventoryShape.width, items, uses,
                total => models[bay].Measure(total).Score(preference), samples,
                performanceAfterAction: machineId == "desequencer" || machineId == "security_alarm", bayCells: legal.ToArray());
            if ((machineId == "hydroponic" || machineId == "printer") && forecast.AnyFailureProbability > 0)
                forecast.RuinedPlacementApproximation = true;
            // Search holds a bounded number of layouts; never keep an unbounded global cache.
            if (forecastCache.Count > 1200) forecastCache.Clear();
            forecastCache[key] = forecast;
            return forecast;
        }

        internal TemporalSummary Forecast(IReadOnlyList<Placement> placements, int uses, int samples = 16)
        {
            var paths = new double[samples][];
            for (int s = 0; s < samples; s++) paths[s] = new double[uses];
            int actualSamples = 1;
            bool approximate = false;
            double allSurvive = 1;
            for (int b = 0; b < bays.Count; b++)
            {
                if (GameAdapter.Profile(bays[b].Machine) == null) continue;
                var after = ForecastBay(b, placements, uses, samples);
                var before = ForecastBay(b, Array.Empty<Placement>(), uses, samples);
                actualSamples = Math.Max(actualSamples, Math.Max(after.Samples, before.Samples));
                approximate |= after.RuinedPlacementApproximation;
                allSurvive *= 1 - after.AnyFailureProbability;
                for (int s = 0; s < samples; s++)
                    for (int u = 0; u < uses; u++)
                        paths[s][u] += after.SampleCumulative[s % after.Samples][u] - before.SampleCumulative[s % before.Samples][u];
            }
            var mean = new double[uses];
            var lower = new double[uses];
            for (int u = 0; u < uses; u++)
            {
                var values = paths.Select(p => p[u]).OrderBy(v => v).ToArray();
                mean[u] = values.Average();
                lower[u] = values[(int)Math.Floor((samples - 1) * .1)];
            }
            return new TemporalSummary {Mean = mean, Lower = lower, Samples = actualSamples,
                AnyFailureProbability = 1 - allSurvive, Approximation = approximate};
        }
    }
}
