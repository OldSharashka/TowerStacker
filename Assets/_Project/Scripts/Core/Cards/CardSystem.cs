using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>卡牌类型：新增规则 / 修改数值 / 乘区 / 延迟收益。</summary>
    public enum CardKind
    {
        AddRule,
        ModifyRule,
        Multiply,
        Delayed,
    }

    /// <summary>一张卡的静态定义（白盒：只有 id、名字、一句话说明）。</summary>
    public sealed class CardDefinition
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Text;
        public readonly CardKind Kind;

        public CardDefinition(string id, string name, string text, CardKind kind)
        {
            Id = id;
            Name = name;
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>一次落子的上下文，交给卡牌修改得分用。预览和正式结算共用。</summary>
    public readonly struct PlacementContext
    {
        public readonly BoardTopology Topology;
        public readonly RunState Run;
        public readonly int CellId;
        public readonly int Value;
        public readonly int DieIndex;

        public PlacementContext(BoardTopology topology, RunState run, int cellId, int value, int dieIndex)
        {
            Topology = topology;
            Run = run;
            CellId = cellId;
            Value = value;
            DieIndex = dieIndex;
        }
    }

    /// <summary>
    /// 卡效果：放进槽位后常驻，直到被替换。
    /// Modify 只读状态（预览会调用它），状态更新放在 OnPlacementScored。
    /// </summary>
    public interface ICardEffect
    {
        string Id { get; }
        string Name { get; }
        string Text { get; }
        CardKind Kind { get; }

        void OnAcquire(RunState run);
        void OnRemove(RunState run);

        /// <summary>向基础贡献里追加/修改；不得修改自身状态。</summary>
        void Modify(PlacementContext ctx, List<ScoreContribution> contributions);

        /// <summary>正式落子结算后更新状态（连击计数等）。预览不会调用。</summary>
        void OnPlacementScored(PlacementContext ctx, ScoringResult result);
    }

    /// <summary>一次三选一的记录，供复现与导出。</summary>
    public sealed class CardChoiceRecord
    {
        public int TurnIndex;
        public string[] Offered;
        public string Chosen;
        public string Replaced;
    }
}
