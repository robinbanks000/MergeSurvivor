using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MergeSurvivor.Core.Combat;
using MergeSurvivor.Core.Merge;
using MergeSurvivor.Core.Rng;
using MergeSurvivor.Core.Spawning;

namespace MergeSurvivor.Sim
{
    /// <summary>
    /// The T3 harness behind gate G4.
    ///
    /// What it deliberately does NOT measure: win rate, run length, softlocks or
    /// dominant strategies. Those need a combat model — enemy health, damage
    /// application, a lose condition — and Core has none of that yet. Inventing
    /// thresholds for them now would produce numbers that look like decisions
    /// nobody made, which is worse than an honest gap.
    ///
    /// What it does measure are structural invariants that hold regardless of
    /// design taste, and that would silently break the game if violated.
    /// </summary>
    public static class Program
    {
        private const float SimulatedSeconds = 120f;
        private const float SpawnInterval = 2f;
        private const float FirstSpawnDelay = 1f;
        private const float SpawnHalfWidth = 8f;

        public static int Main(string[] args)
        {
            int seed = ArgInt(args, "--seed", 12345);
            int runs = ArgInt(args, "--runs", 1000);
            string outPath = ArgString(args, "--out", "Studio/evidence/sims/metrics.json");

            var metrics = new SortedDictionary<string, double>();
            var violations = new List<string>();

            CheckWeaponCurve(metrics, violations);
            CheckMergePremium(metrics, violations);
            CheckSchedulerDeterminism(seed, runs, metrics, violations);
            CheckFrameRateIndependence(seed, metrics, violations);

            metrics["seedsSwept"] = runs;
            metrics["simulatedSecondsPerSeed"] = SimulatedSeconds;

            WriteMetrics(outPath, seed, metrics, violations);

            Console.WriteLine($"MergeSurvivor.Sim — seed {seed}, {runs} seeds swept");
            foreach (KeyValuePair<string, double> m in metrics)
            {
                Console.WriteLine($"  {m.Key,-32} {m.Value.ToString("0.######", CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine();
            if (violations.Count == 0)
            {
                Console.WriteLine($"G4 STRUCTURAL INVARIANTS PASSED — metrics written to {outPath}");
                return 0;
            }

            Console.WriteLine("G4 FAILED:");
            foreach (string v in violations)
            {
                Console.WriteLine($"  - {v}");
            }

            return 1;
        }

        /// <summary>Damage, fire rate and DPS must rise with every tier and stay finite.</summary>
        private static void CheckWeaponCurve(IDictionary<string, double> metrics, ICollection<string> violations)
        {
            double minDpsGrowth = double.MaxValue;

            for (int tier = Weapon.MinTier; tier <= Weapon.MaxTier; tier++)
            {
                var weapon = new Weapon(tier);
                float damage = WeaponStats.DamageFor(weapon);
                float rate = WeaponStats.FireRateFor(weapon);
                float dps = WeaponStats.DpsFor(weapon);

                foreach ((string name, float value) in new[]
                         {
                             ($"damage@T{tier}", damage),
                             ($"fireRate@T{tier}", rate),
                             ($"dps@T{tier}", dps)
                         })
                {
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        violations.Add($"{name} is not a finite number ({value}).");
                    }
                }

                if (tier > Weapon.MinTier)
                {
                    var previous = new Weapon(tier - 1);
                    if (dps <= WeaponStats.DpsFor(previous))
                    {
                        violations.Add(
                            $"DPS does not increase from tier {tier - 1} to {tier}; merging up would be a downgrade.");
                    }

                    minDpsGrowth = Math.Min(minDpsGrowth, dps / WeaponStats.DpsFor(previous));
                }
            }

            metrics["dpsTier1"] = WeaponStats.DpsFor(new Weapon(Weapon.MinTier));
            metrics["dpsTierMax"] = WeaponStats.DpsFor(new Weapon(Weapon.MaxTier));
            metrics["minDpsGrowthPerTier"] = minDpsGrowth;
        }

        /// <summary>
        /// The core promise of a merge game: combining two weapons must beat keeping
        /// the pair. Below 1.0 the rational player never merges and the loop dies.
        /// </summary>
        private static void CheckMergePremium(IDictionary<string, double> metrics, ICollection<string> violations)
        {
            double worst = double.MaxValue;

            for (int tier = Weapon.MinTier; tier < Weapon.MaxTier; tier++)
            {
                var pair = new Weapon(tier);
                MergeResult merged = MergeSystem.Merge(pair, pair);

                if (!merged.Success)
                {
                    violations.Add($"Two tier-{tier} weapons failed to merge below the cap.");
                    continue;
                }

                double premium = WeaponStats.DpsFor(merged.Merged) / (2.0 * WeaponStats.DpsFor(pair));
                worst = Math.Min(worst, premium);

                if (premium <= 1.0)
                {
                    violations.Add(
                        $"Merging two tier-{tier} weapons yields {premium:0.###}x the pair's DPS — merging is a loss.");
                }
            }

            metrics["worstMergePremium"] = worst;
        }

        /// <summary>
        /// Determinism is what makes a seeded bug report reproducible and two
        /// simulation runs comparable. If this breaks, every other metric here
        /// becomes noise.
        /// </summary>
        private static void CheckSchedulerDeterminism(
            int baseSeed, int runs, IDictionary<string, double> metrics, ICollection<string> violations)
        {
            long totalSpawns = 0;
            int divergences = 0;

            for (int i = 0; i < runs; i++)
            {
                uint seed = unchecked((uint)(baseSeed + i));

                List<SpawnRequest> first = RunSchedule(seed, 1f / 60f);
                List<SpawnRequest> second = RunSchedule(seed, 1f / 60f);

                if (first.Count != second.Count)
                {
                    divergences++;
                    continue;
                }

                for (int s = 0; s < first.Count; s++)
                {
                    if (first[s].X != second[s].X)
                    {
                        divergences++;
                        break;
                    }
                }

                totalSpawns += first.Count;

                foreach (SpawnRequest request in first)
                {
                    if (request.X < -SpawnHalfWidth || request.X >= SpawnHalfWidth)
                    {
                        violations.Add($"Seed {seed} spawned outside the arena at x={request.X}.");
                        break;
                    }
                }
            }

            if (divergences > 0)
            {
                violations.Add($"{divergences} of {runs} seeds produced different results on a second run.");
            }

            metrics["meanSpawnsPerRun"] = runs == 0 ? 0 : (double)totalSpawns / runs;
            metrics["spawnsPerMinute"] = runs == 0 ? 0 : (double)totalSpawns / runs / (SimulatedSeconds / 60.0);
            metrics["determinismDivergences"] = divergences;
        }

        /// <summary>
        /// A player on a 30fps phone must see the same amount of content as one on a
        /// 240fps desktop. This is the invariant the old InvokeRepeating broke.
        /// </summary>
        private static void CheckFrameRateIndependence(
            int seed, IDictionary<string, double> metrics, ICollection<string> violations)
        {
            int atThirty = RunSchedule(unchecked((uint)seed), 1f / 30f).Count;
            int atTwoForty = RunSchedule(unchecked((uint)seed), 1f / 240f).Count;

            if (atThirty != atTwoForty)
            {
                violations.Add(
                    $"Frame rate changes content: {atThirty} spawns at 30fps versus {atTwoForty} at 240fps.");
            }

            metrics["spawnsAt30fps"] = atThirty;
            metrics["spawnsAt240fps"] = atTwoForty;
        }

        private static List<SpawnRequest> RunSchedule(uint seed, float dt)
        {
            var scheduler = new WaveScheduler(
                new XorShiftRng(seed), FirstSpawnDelay, SpawnInterval, SpawnHalfWidth);

            var spawns = new List<SpawnRequest>(128);
            float elapsed = 0f;

            while (elapsed < SimulatedSeconds)
            {
                scheduler.Tick(dt, spawns);
                elapsed += dt;
            }

            return spawns;
        }

        private static void WriteMetrics(
            string outPath, int seed, IDictionary<string, double> metrics, IReadOnlyCollection<string> violations)
        {
            string directory = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"seed\": {seed},");
            json.AppendLine($"  \"verdict\": \"{(violations.Count == 0 ? "pass" : "fail")}\",");
            json.AppendLine("  \"metrics\": {");

            string body = string.Join("," + Environment.NewLine, metrics.Select(m =>
                $"    \"{m.Key}\": {m.Value.ToString("0.######", CultureInfo.InvariantCulture)}"));
            json.AppendLine(body);

            json.AppendLine("  },");
            json.AppendLine("  \"violations\": [");
            json.AppendLine(string.Join("," + Environment.NewLine,
                violations.Select(v => $"    \"{v.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"")));
            json.AppendLine("  ]");
            json.AppendLine("}");

            File.WriteAllText(outPath, json.ToString());
        }

        private static int ArgInt(string[] args, string name, int fallback)
        {
            string raw = ArgString(args, name, null);
            return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        private static string ArgString(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return fallback;
        }
    }
}
