using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>
    /// 计分管线：基础 R1/R2 → 加法类卡（含新规则/延迟）→ 乘法类卡 → 单次上限。
    /// 顺序与槽位位置无关（按 Kind 分两遍），所以卡槽顺序不影响结果。
    /// 预览与正式结算必须都走这里。
    /// </summary>
    public static class ScoringPipeline
    {
        public static ScoringResult Evaluate(
            BoardTopology topology,
            RunState run,
            int cellId,
            int value,
            int dieIndex,
            ScoringConfig config,
            IReadOnlyList<ICardEffect> cards)
        {
            ScoringResult baseResult = Scorer.Evaluate(topology, run, cellId, value, config);
            var contributions = new List<ScoreContribution>(baseResult.Contributions);
            var context = new PlacementContext(topology, run, cellId, value, dieIndex);

            if (cards != null)
            {
                // 先算所有加法/规则类，再统一乘，避免槽位顺序影响结果
                for (int i = 0; i < cards.Count; i++)
                {
                    ICardEffect card = cards[i];
                    if (card != null && card.Kind != CardKind.Multiply) card.Modify(context, contributions);
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    ICardEffect card = cards[i];
                    if (card != null && card.Kind == CardKind.Multiply) card.Modify(context, contributions);
                }
            }

            int total = 0;
            for (int i = 0; i < contributions.Count; i++) total += contributions[i].Amount;

            int cap = 3 * (4 * config.EdgeSamePoints + 4 * config.DiagDiffPoints);
            if (cap > 0 && total > cap) TrimToCap(contributions, ref total, cap);

            return new ScoringResult(total, contributions);
        }

        /// <summary>超上限时从最后一条（卡牌加成）开始削减，保证之和仍等于返回值。</summary>
        private static void TrimToCap(List<ScoreContribution> contributions, ref int total, int cap)
        {
            for (int i = contributions.Count - 1; i >= 0 && total > cap; i--)
            {
                ScoreContribution c = contributions[i];
                int cut = total - cap;
                if (cut > c.Amount) cut = c.Amount;
                if (cut <= 0) continue;

                contributions[i] = new ScoreContribution(c.SourceCellId, c.RuleId, c.Amount - cut);
                total -= cut;
            }

            for (int i = contributions.Count - 1; i >= 0; i--)
            {
                if (contributions[i].Amount <= 0) contributions.RemoveAt(i);
            }
        }
    }
}
