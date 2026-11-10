using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>卡效果基类：默认什么都不做。</summary>
    internal abstract class CardEffectBase : ICardEffect
    {
        private readonly CardDefinition _definition;

        protected CardEffectBase(CardDefinition definition)
        {
            _definition = definition;
        }

        public string Id => _definition.Id;
        public string Name => _definition.Name;
        public string Text => _definition.Text;
        public CardKind Kind => _definition.Kind;

        public virtual void OnAcquire(RunState run) { }
        public virtual void OnRemove(RunState run) { }
        public virtual void Modify(PlacementContext ctx, List<ScoreContribution> contributions) { }
        public virtual void OnPlacementScored(PlacementContext ctx, ScoringResult result) { }
    }

    /// <summary>第三颗 ×2：DieIndex == 2 时把所有贡献翻倍（乘法，最后执行）。</summary>
    internal sealed class ThirdDoubleCard : CardEffectBase
    {
        public ThirdDoubleCard() : base(CardCatalog.ThirdDouble) { }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            if (ctx.DieIndex != 2) return;

            for (int i = 0; i < contributions.Count; i++)
            {
                ScoreContribution c = contributions[i];
                contributions[i] = new ScoreContribution(c.SourceCellId, c.RuleId, c.Amount * 2);
            }
        }
    }

    /// <summary>斜同数 +1：对角相邻且同数的邻居也计 1 分（新增规则）。</summary>
    internal sealed class DiagSameCard : CardEffectBase
    {
        public const string RuleId = "C.DiagSame";

        public DiagSameCard() : base(CardCatalog.DiagSame) { }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            foreach (Neighbor neighbor in ctx.Topology.Neighbors(ctx.CellId))
            {
                if (!neighbor.IsDiagonal) continue;
                int value = ctx.Run.ValueAt(neighbor.CellId);
                if (value == 0 || value != ctx.Value) continue;

                contributions.Add(new ScoreContribution(neighbor.CellId, RuleId, 1));
            }
        }
    }

    /// <summary>空手补偿：上一回合 0 分时，本回合第一次落子 +6。</summary>
    internal sealed class EmptyBonusCard : CardEffectBase
    {
        public const string RuleId = "C.EmptyBonus";
        public const int Bonus = 6;

        public EmptyBonusCard() : base(CardCatalog.EmptyBonus) { }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            if (ctx.DieIndex != 0) return;

            List<TurnRecord> history = ctx.Run.History;
            if (history.Count == 0) return;                       // 第一回合没有"上一回合"
            if (history[history.Count - 1].Score != 0) return;

            contributions.Add(new ScoreContribution(ctx.CellId, RuleId, Bonus));
        }
    }

    /// <summary>边同数 +1：R1 的每个贡献 +1。</summary>
    internal sealed class EdgePlusCard : CardEffectBase
    {
        public EdgePlusCard() : base(CardCatalog.EdgePlus) { }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            for (int i = 0; i < contributions.Count; i++)
            {
                if (contributions[i].RuleId != Scorer.RuleOrthoSame) continue;
                ScoreContribution c = contributions[i];
                contributions[i] = new ScoreContribution(c.SourceCellId, c.RuleId, c.Amount + 1);
            }
        }
    }

    /// <summary>连击：连续 2 次命中 R1 后，下一次 R1 +2（状态只在正式结算时更新）。</summary>
    internal sealed class ComboCard : CardEffectBase
    {
        public const int Bonus = 2;
        private int _streak;

        public ComboCard() : base(CardCatalog.Combo) { }

        public override void OnAcquire(RunState run) { _streak = 0; }
        public override void OnRemove(RunState run) { _streak = 0; }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            if (_streak < 2) return;

            for (int i = 0; i < contributions.Count; i++)
            {
                if (contributions[i].RuleId != Scorer.RuleOrthoSame) continue;
                ScoreContribution c = contributions[i];
                contributions[i] = new ScoreContribution(c.SourceCellId, c.RuleId, c.Amount + Bonus);
                return;
            }
        }

        public override void OnPlacementScored(PlacementContext ctx, ScoringResult result)
        {
            bool hit = false;
            for (int i = 0; i < result.Contributions.Count; i++)
            {
                if (result.Contributions[i].RuleId != Scorer.RuleOrthoSame) continue;
                hit = true;
                break;
            }

            if (!hit)
            {
                _streak = 0;
                return;
            }

            _streak = _streak >= 2 ? 0 : _streak + 1;
        }
    }

    /// <summary>首骰 +2：每回合第 1 颗骰子命中时额外 +2。</summary>
    internal sealed class FirstPlusCard : CardEffectBase
    {
        public const string RuleId = "C.FirstPlus";
        public const int Bonus = 2;

        public FirstPlusCard() : base(CardCatalog.FirstPlus) { }

        public override void Modify(PlacementContext ctx, List<ScoreContribution> contributions)
        {
            if (ctx.DieIndex != 0) return;

            for (int i = 0; i < contributions.Count; i++)
            {
                if (contributions[i].RuleId != Scorer.RuleOrthoSame) continue;
                contributions.Add(new ScoreContribution(ctx.CellId, RuleId, Bonus));
                return;
            }
        }
    }
}
