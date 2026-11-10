using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>白盒卡池：6 张。新增卡只需在这里登记 + 在 Create 里加一个分支。</summary>
    public static class CardCatalog
    {
        public const string ThirdDoubleId = "third_double";
        public const string DiagSameId = "diag_same";
        public const string EmptyBonusId = "empty_bonus";
        public const string EdgePlusId = "edge_plus";
        public const string ComboId = "combo";
        public const string FirstPlusId = "first_plus";

        public static readonly CardDefinition ThirdDouble = new CardDefinition(
            ThirdDoubleId, "第三颗 ×2", "每回合第 3 颗骰子的命中分 ×2", CardKind.Multiply);

        public static readonly CardDefinition DiagSame = new CardDefinition(
            DiagSameId, "斜同数 +1", "对角相邻且数字相同的邻居也 +1", CardKind.AddRule);

        public static readonly CardDefinition EmptyBonus = new CardDefinition(
            EmptyBonusId, "空手补偿", "上一回合 0 分时，本回合首次落子 +6", CardKind.Delayed);

        public static readonly CardDefinition EdgePlus = new CardDefinition(
            EdgePlusId, "边同数 +1", "R1 的每个邻居额外 +1（2 → 3）", CardKind.ModifyRule);

        public static readonly CardDefinition Combo = new CardDefinition(
            ComboId, "连击", "连续 2 次命中 R1 后，下一次 R1 +2", CardKind.ModifyRule);

        public static readonly CardDefinition FirstPlus = new CardDefinition(
            FirstPlusId, "首骰 +2", "每回合第 1 颗骰子命中时 +2", CardKind.ModifyRule);

        public static readonly IReadOnlyList<CardDefinition> All = new List<CardDefinition>
        {
            ThirdDouble,
            DiagSame,
            EmptyBonus,
            EdgePlus,
            Combo,
            FirstPlus,
        };

        public static ICardEffect Create(string id)
        {
            switch (id)
            {
                case ThirdDoubleId: return new ThirdDoubleCard();
                case DiagSameId: return new DiagSameCard();
                case EmptyBonusId: return new EmptyBonusCard();
                case EdgePlusId: return new EdgePlusCard();
                case ComboId: return new ComboCard();
                case FirstPlusId: return new FirstPlusCard();
                default: return null;
            }
        }

        public static CardDefinition Find(string id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id) return All[i];
            }
            return null;
        }
    }
}
