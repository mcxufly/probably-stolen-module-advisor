using System;

namespace ModuleAdvisor
{
    // Pure current-operation model. Only real limits are applied; unknown recipe-dependent
    // limits remain explicitly unbounded rather than inventing a universal 100% cap.
    public sealed class MachineModel
    {
        public string Id;
        public int BasePower, BaseSpeed, PerformanceRatio = 1, EfficiencyRatio = 1, QualityRatio;
        public bool HasPerformanceRatio, HasEfficiencyRatio, PerformanceReducesPower;
        public int? DnaBaseChance, FurnaceInputQuality, HydroBaseHarvest;
        public bool HydroIgnoresQuality;
        public bool BlastFurnace, Flux;

        public MachineMetrics Measure(int[] total)
        {
            int p = total[0], e = total[1], q = total[2];
            double power = BasePower - BasePower * (long)e / 100;
            if (HasEfficiencyRatio && EfficiencyRatio > 0)
                power = (int)((float)BasePower - (float)(BasePower * (long)e / EfficiencyRatio) / 100f);
            if (PerformanceReducesPower && HasPerformanceRatio && PerformanceRatio > 0)
                power -= (int)((float)(BasePower * (long)p / PerformanceRatio) / 100f);
            if (Id == "deal_maker") power = BasePower - BasePower * (long)(p + e) / 100;
            power = Math.Max(1, power);
            double speed = BaseSpeed + BaseSpeed * (long)p / 100;
            if (HasPerformanceRatio && PerformanceRatio != 0)
                speed = (int)((float)BaseSpeed + (float)(BaseSpeed * (long)p / PerformanceRatio) / 100f);
            int adjustedQ = QualityRatio > 0 ? q / QualityRatio : 0;
            var result = new MachineMetrics { Power = power, PowerFloor = power <= 1 };
            result.Utility[1] = BasePower > 0 ? 100 * (BasePower - power) / BasePower : 0;
            result.Performance = speed;
            result.Utility[0] = BaseSpeed > 0 ? 100 * (speed - BaseSpeed) / BaseSpeed : 0;
            result.Quality = adjustedQ;
            result.Utility[2] = adjustedQ;
            switch (Id)
            {
                case "hydroponic":
                    if (HydroIgnoresQuality) result.Quality = result.Utility[2] = 0;
                    else if (HydroBaseHarvest.HasValue)
                    {
                        result.Quality = Math.Round(HydroBaseHarvest.Value * (1 + adjustedQ / 100.0), MidpointRounding.AwayFromZero);
                        result.Utility[2] = HydroBaseHarvest.Value > 0 ? result.Quality / HydroBaseHarvest.Value * 100 : 0;
                    }
                    break;
                case "security_alarm":
                    result.Performance = Clamp(Math.Max(0, 50L + p) + 1, 0, 100);
                    result.Utility[0] = result.Performance;
                    result.PerformanceCapped = result.Performance >= 100;
                    result.Quality = result.Utility[2] = 0;
                    break;
                case "desequencer": result.Quality = result.Utility[2] = 0; break;
                case "furnace":
                    result.Performance = result.Utility[0] = 0;
                    int tier = q / 100 - (BlastFurnace ? 1 : 0);
                    if (Flux) tier = Math.Max(1, tier);
                    if (FurnaceInputQuality.HasValue)
                    {
                        tier = (int)Clamp(FurnaceInputQuality.Value + tier, 0, 5);
                        result.QualityCapped = tier >= 5;
                    }
                    result.Quality = tier;
                    result.Utility[2] = tier * 100;
                    break;
                case "moisture_farm":
                    result.Performance = Math.Max(0, 850000L * (100L + p) / 100) / 1000.0;
                    result.Utility[0] = result.Performance / 850 * 100;
                    int water = q < -50 ? 0 : q < 0 ? 1 : q < 50 ? 2 : q < 100 ? 3 : q < 150 ? 4 : 5;
                    result.Quality = water;
                    result.Utility[2] = water * 50;
                    result.QualityCapped = water == 5;
                    break;
                case "water_purifier":
                    // Reference dirt multiplier=1 (purity >=98%); actual volume/dirt changes the budget.
                    result.Performance = Math.Max(0, 100 + p);
                    result.Utility[0] = result.Performance;
                    int purity = q >= 200 ? 10000 : q >= 100 ? 9990 : q >= 75 ? 9980 : q >= 50 ? 9965
                        : q >= 25 ? 9935 : q >= 15 ? 9915 : 9900;
                    result.Quality = purity / 100.0;
                    result.Utility[2] = purity - 9900;
                    result.QualityCapped = purity == 10000;
                    break;
                case "evaporator":
                    result.Performance = Math.Round(300000f * Math.Max(0f, 1f + p / 100f)) / 1000.0;
                    result.Utility[0] = result.Performance / 300 * 100;
                    result.Quality = Clamp(q, 0, 100);
                    result.Utility[2] = result.Quality;
                    result.QualityCapped = q >= 100;
                    break;
                case "plant_dna_manipulator":
                    if (DnaBaseChance.HasValue)
                    {
                        result.Quality = Clamp(DnaBaseChance.Value + adjustedQ, 5, 100);
                        result.Utility[2] = result.Quality;
                        result.QualityCapped = result.Quality >= 100;
                    }
                    break;
                case "mirage_projector":
                    result.Performance = Math.Max(0, 100 + q / Math.Max(1, QualityRatio) + p / Math.Max(1, PerformanceRatio));
                    // Both P and Q contribute to a single attraction outcome; count it once.
                    result.Utility[0] = Math.Max(0, 100 + p / Math.Max(1, PerformanceRatio));
                    result.Quality = result.Performance;
                    result.Utility[2] = result.Performance - result.Utility[0];
                    break;
                case "deal_maker":
                    result.Performance = result.Utility[0] = 0;
                    result.Quality = Math.Max(0, 10 + adjustedQ);
                    result.Utility[2] = result.Quality;
                    break;
            }
            return result;
        }

        private static double Clamp(long value, long min, long max) => Math.Min(max, Math.Max(min, value));
    }

    public sealed class MachineMetrics
    {
        public double Performance, Power, Quality;
        public bool PerformanceCapped, PowerFloor, QualityCapped;
        public double[] Utility = new double[3];
        public double Score(int preference)
        {
            double result = 0;
            for (int i = 0; i < 3; i++) result += Utility[i] * (preference == i + 1 ? 3 : 1);
            return result;
        }
    }
}
