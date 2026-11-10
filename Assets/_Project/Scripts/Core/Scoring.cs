using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>两条计分规则的系数。默认 2:1，依据见开发文档 2.7 的模拟。</summary>
    public sealed class ScoringConfig
    {
        /// <summary>R1：正交相邻且数字相同。</summary>
        public int EdgeSamePoints = 2;

        /// <summary>R2：对角相邻且数字不同。</summary>
        public int DiagDiffPoints = 1;

        public static ScoringConfig Default => new ScoringConfig();

        public static ScoringConfig Even => new ScoringConfig { EdgeSamePoints = 1, DiagDiffPoints = 1 };
    }

    /// <summary>
    /// 一次计分的单条来源。按邻居逐条给出，
    /// 这是「相邻格逐个跳出得分」效果的数据基础（见文档 3.7.4）。
    /// </summary>
    public readonly struct ScoreContribution
    {
        /// <summary>给出这个分的邻居格。</summary>
        public readonly int SourceCellId;

        public readonly string RuleId;
        public readonly int Amount;

        public ScoreContribution(int sourceCellId, string ruleId, int amount)
        {
            SourceCellId = sourceCellId;
            RuleId = ruleId;
            Amount = amount;
        }

        public override string ToString() => $"{RuleId}+{Amount}@{SourceCellId}";
    }

    public sealed class ScoringResult
    {
        public readonly int Total;
        public readonly IReadOnlyList<ScoreContribution> Contributions;

        public ScoringResult(int total, IReadOnlyList<ScoreContribution> contributions)
        {
            Total = total;
            Contributions = contributions;
        }
    }

    /// <summary>
    /// 计分：只看刚放下那一格自己的一圈，逐个邻居按关系给分。
    /// 正交 + 同数 = R1；对角 + 异数 = R2；其余 0 分。
    /// </summary>
    public static class Scorer
    {
        public const string RuleOrthoSame = "R1.OrthoSame";
        public const string RuleDiagDiff = "R2.DiagDiff";

        public static ScoringResult Evaluate(
            BoardTopology topology,
            RunState run,
            int cellId,
            int value,
            ScoringConfig config)
        {
            var contributions = new List<ScoreContribution>(BoardTopology.MaxNeighbors);
            int total = 0;

            foreach (Neighbor neighbor in topology.Neighbors(cellId))
            {
                int neighborValue = run.ValueAt(neighbor.CellId);
                if (neighborValue == FaceState.Empty) continue;

                if (!neighbor.IsDiagonal)
                {
                    if (neighborValue == value)
                    {
                        contributions.Add(new ScoreContribution(neighbor.CellId, RuleOrthoSame, config.EdgeSamePoints));
                        total += config.EdgeSamePoints;
                    }
                }
                else
                {
                    if (neighborValue != value)
                    {
                        contributions.Add(new ScoreContribution(neighbor.CellId, RuleDiagDiff, config.DiagDiffPoints));
                        total += config.DiagDiffPoints;
                    }
                }
            }

            return new ScoringResult(total, contributions);
        }
    }
}
