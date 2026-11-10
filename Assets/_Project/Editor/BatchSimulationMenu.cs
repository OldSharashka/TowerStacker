using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Tower.Core;
using UnityEditor;
using UnityEngine;

namespace Tower.EditorTools
{
    /// <summary>
    /// 编辑器批量模拟（文档 3-3 / 验收门 4）：随机与贪心两种策略各跑 N 局，
    /// 输出均值/标准差/p5–p95/最高分，并和 §2.7 的表对照；结果同时落到 Logs/batch_sim_*.csv。
    /// 纯 Core 逻辑，不依赖场景，几秒内跑完。
    /// </summary>
    public static class BatchSimulationMenu
    {
        [MenuItem("Tower/批量模拟 200 局（随机 + 贪心）")]
        private static void Run200()
        {
            Run(200);
        }

        [MenuItem("Tower/批量模拟 800 局（随机 + 贪心）")]
        private static void Run800()
        {
            Run(800);
        }

        private static void Run(int games)
        {
            BoardTopology topology = BoardTopology.Build();
            var settings = new RunSettings();

            List<int> random = Simulate(games, false, topology, settings);
            List<int> greedy = Simulate(games, true, topology, settings);

            string report = BuildReport(games, random, greedy);
            Debug.Log(report);

            string path = WriteCsv(games, random, greedy);
            Debug.Log("[Tower] 批量模拟明细已写入：" + path);
        }

        private static List<int> Simulate(int games, bool greedyPolicy, BoardTopology topology, RunSettings settings)
        {
            var scores = new List<int>(games);
            ScoringConfig scoring = ScoringConfig.Default;

            for (int g = 0; g < games; g++)
            {
                var run = new TowerRun((ulong)(g + 1), topology, scoring, settings);
                var rng = new DeterministicRandom((ulong)(g * 7919 + 17));
                run.Start();

                while (!run.IsFinished)
                {
                    List<int> cells = run.EmptyCellsOnCurrentFace();
                    int value = run.Dice[run.PlacedThisTurn];
                    int chosen = cells[0];

                    if (greedyPolicy)
                    {
                        int best = int.MinValue;
                        foreach (int cell in cells)
                        {
                            int score = Scorer.Evaluate(topology, run.State, cell, value, scoring).Total;
                            if (score > best)
                            {
                                best = score;
                                chosen = cell;
                            }
                        }
                    }
                    else
                    {
                        chosen = cells[rng.NextInt(cells.Count)];
                    }

                    run.Place(chosen);
                }

                scores.Add(run.State.TotalScore);
            }

            return scores;
        }

        private static string BuildReport(int games, List<int> random, List<int> greedy)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Tower] 批量模拟 {games} 局（18 回合、无重掷、无卡牌、K=2:1）");
            sb.AppendLine("策略        均值    标准差   p5    p95    最高   §2.7 参考");
            sb.AppendLine($"随机       {Describe(random),-42} 135.6 / 10.7");
            sb.AppendLine($"贪心       {Describe(greedy),-42} 173.9 / 5.7");

            double randomMean = Mean(random);
            double greedyMean = Mean(greedy);
            sb.AppendLine();
            sb.AppendLine($"与 §2.7 偏差：随机 {Error(randomMean, 135.6):P1}，贪心 {Error(greedyMean, 173.9):P1}（门 4 要求 < 3%）");
            return sb.ToString();
        }

        private static string Describe(List<int> values)
        {
            List<int> sorted = new List<int>(values);
            sorted.Sort();

            return string.Format(CultureInfo.InvariantCulture,
                "{0,6:F1}  {1,6:F1}  {2,4}  {3,4}  {4,4}",
                Mean(values), StandardDeviation(values),
                Percentile(sorted, 0.05), Percentile(sorted, 0.95), sorted[sorted.Count - 1]);
        }

        private static double Mean(List<int> values)
        {
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return values.Count > 0 ? sum / values.Count : 0;
        }

        private static double StandardDeviation(List<int> values)
        {
            if (values.Count < 2) return 0;
            double mean = Mean(values);
            double sum = 0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                sum += d * d;
            }
            return Math.Sqrt(sum / (values.Count - 1));
        }

        private static int Percentile(List<int> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int index = (int)Math.Round(p * (sorted.Count - 1));
            return sorted[Mathf.Clamp(index, 0, sorted.Count - 1)];
        }

        private static double Error(double actual, double expected)
        {
            return expected == 0 ? 0 : Math.Abs(actual - expected) / expected;
        }

        private static string WriteCsv(int games, List<int> random, List<int> greedy)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(dir);

            string path = Path.Combine(dir,
                $"batch_sim_{games}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("game,random,greedy");
            for (int i = 0; i < games; i++)
            {
                sb.Append(i + 1).Append(',')
                  .Append(random[i]).Append(',')
                  .Append(greedy[i]).AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("strategy,mean,sd,p5,p95,max");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "random,{0:F2},{1:F2},{2},{3},{4}", Mean(random), StandardDeviation(random),
                Percentile(Sorted(random), 0.05), Percentile(Sorted(random), 0.95), Max(random)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "greedy,{0:F2},{1:F2},{2},{3},{4}", Mean(greedy), StandardDeviation(greedy),
                Percentile(Sorted(greedy), 0.05), Percentile(Sorted(greedy), 0.95), Max(greedy)));

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static List<int> Sorted(List<int> values)
        {
            var copy = new List<int>(values);
            copy.Sort();
            return copy;
        }

        private static int Max(List<int> values)
        {
            int max = int.MinValue;
            for (int i = 0; i < values.Count; i++) if (values[i] > max) max = values[i];
            return max;
        }
    }
}
