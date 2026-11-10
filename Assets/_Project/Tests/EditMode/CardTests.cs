using System.Collections.Generic;
using NUnit.Framework;
using Tower.Core;

namespace Tower.Tests
{
    /// <summary>卡牌系统验收（文档 §13）：抽卡时机、三选一、替换、六张卡的效果。</summary>
    public class CardTests
    {
        private BoardTopology _topo;

        [SetUp]
        public void SetUp()
        {
            _topo = BoardTopology.Build();
        }

        private TowerRun NewRun(bool cards, ulong seed = 42UL)
        {
            return new TowerRun(seed, _topo, ScoringConfig.Default,
                new RunSettings { EnableCards = cards });
        }

        private static ICardEffect[] Cards(params string[] ids)
        {
            var list = new ICardEffect[ids.Length];
            for (int i = 0; i < ids.Length; i++) list[i] = CardCatalog.Create(ids[i]);
            return list;
        }

        private void PlayUntilChoice(TowerRun run)
        {
            int guard = 0;
            while (!run.HasPendingCardChoice && !run.IsFinished && guard++ < 400)
            {
                List<int> cells = run.EmptyCellsOnCurrentFace();
                if (cells.Count == 0) break;
                run.Place(cells[0]);
            }
        }

        [Test]
        public void CardsDisabled_NoChoice()
        {
            TowerRun run = NewRun(false);
            run.Start();
            Assert.IsFalse(run.HasPendingCardChoice);
            Assert.AreEqual(0, run.CardChoicesMade);
        }

        [Test]
        public void CardsEnabled_DrawsThreeDistinctAtTurnStart()
        {
            TowerRun run = NewRun(true);
            run.Start();

            Assert.IsTrue(run.HasPendingCardChoice);
            Assert.AreEqual(3, run.OfferedCards.Count);

            var ids = new HashSet<string>();
            foreach (CardDefinition card in run.OfferedCards) ids.Add(card.Id);
            Assert.AreEqual(3, ids.Count, "一次三选一里不应有重复卡");
        }

        [Test]
        public void SameSeed_DrawsSameCards()
        {
            TowerRun a = NewRun(true, 777UL);
            TowerRun b = NewRun(true, 777UL);
            a.Start();
            b.Start();

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(a.OfferedCards[i].Id, b.OfferedCards[i].Id);
            }
        }

        [Test]
        public void ChooseCard_FillsFirstEmptySlot()
        {
            TowerRun run = NewRun(true);
            run.Start();

            Assert.IsTrue(run.ChooseCard(0, -1));
            Assert.IsFalse(run.HasPendingCardChoice);
            Assert.IsNotNull(run.CardSlots[0]);
            Assert.IsNull(run.CardSlots[1]);
            Assert.AreEqual(1, run.CardChoicesMade);
            Assert.AreEqual(1, run.State.CardHistory.Count);
        }

        [Test]
        public void FullSlots_RequireReplacementSlot()
        {
            TowerRun run = NewRun(true);
            run.Start();
            run.ChooseCard(0, -1);
            PlayUntilChoice(run);
            run.ChooseCard(0, -1);
            PlayUntilChoice(run);
            run.ChooseCard(0, -1);
            PlayUntilChoice(run);

            Assert.IsFalse(run.ChooseCard(0, -1), "槽位已满时不给槽位应当失败");

            string oldSlot1 = run.CardSlots[1].Id;
            int offered = -1;
            for (int i = 0; i < run.OfferedCards.Count; i++)
            {
                if (run.OfferedCards[i].Id != oldSlot1) { offered = i; break; }
            }
            Assert.GreaterOrEqual(offered, 0, "三选一里应当有与旧卡不同的卡");

            Assert.IsTrue(run.ChooseCard(offered, 1), "指定槽位应当替换成功");
            Assert.AreNotEqual(oldSlot1, run.CardSlots[1].Id);
            Assert.AreEqual(oldSlot1, run.State.CardHistory[run.State.CardHistory.Count - 1].Replaced);
        }

        [Test]
        public void CardsAreDrawnAtTurns_0_3_6_9_12_15()
        {
            TowerRun run = NewRun(true);
            run.Start();

            PlayUntilChoice(run);
            while (!run.IsFinished)
            {
                if (run.HasPendingCardChoice) run.ChooseCard(0, run.CardSlots[2] == null ? -1 : 0);
                PlayUntilChoice(run);
                if (run.HasPendingCardChoice && run.State.TurnIndex > 15) break;
            }

            var turns = new List<int>();
            foreach (CardChoiceRecord record in run.State.CardHistory) turns.Add(record.TurnIndex);

            CollectionAssert.AreEqual(new[] { 0, 3, 6, 9, 12, 15 }, turns);
        }

        [Test]
        public void ThirdDouble_DoublesThirdDieOnly()
        {
            var state = new RunState();
            state.Place(Board.CellId(FaceId.Front, 0, 1), 2);
            state.Place(Board.CellId(FaceId.Front, 1, 0), 2);

            int cell = Board.CellId(FaceId.Front, 1, 1);
            ICardEffect[] cards = Cards(CardCatalog.ThirdDoubleId);

            int first = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, cards).Total;
            int third = ScoringPipeline.Evaluate(_topo, state, cell, 2, 2, ScoringConfig.Default, cards).Total;

            Assert.AreEqual(4, first, "第一颗不变");
            Assert.AreEqual(8, third, "第三颗翻倍");
        }

        [Test]
        public void DiagSame_AddsOneForSameDiagonal()
        {
            var state = new RunState();
            state.Place(Board.CellId(FaceId.Front, 0, 0), 2);

            int cell = Board.CellId(FaceId.Front, 1, 1);
            int without = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, null).Total;
            int with = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default,
                Cards(CardCatalog.DiagSameId)).Total;

            Assert.AreEqual(0, without, "对角同数本来不给分");
            Assert.AreEqual(1, with);
        }

        [Test]
        public void EdgePlus_AddsOnePerOrthoSame()
        {
            var state = new RunState();
            state.Place(Board.CellId(FaceId.Front, 0, 1), 2);

            int cell = Board.CellId(FaceId.Front, 1, 1);
            int without = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, null).Total;
            int with = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default,
                Cards(CardCatalog.EdgePlusId)).Total;

            Assert.AreEqual(2, without);
            Assert.AreEqual(3, with);
        }

        [Test]
        public void EmptyBonus_OnlyWhenPreviousTurnWasZero()
        {
            var state = new RunState();
            state.History.Add(new TurnRecord { TurnIndex = 0, Score = 0 });

            int cell = Board.CellId(FaceId.Front, 1, 1);
            ICardEffect[] cards = Cards(CardCatalog.EmptyBonusId);

            int firstDie = ScoringPipeline.Evaluate(_topo, state, cell, 1, 0, ScoringConfig.Default, cards).Total;
            int secondDie = ScoringPipeline.Evaluate(_topo, state, cell, 1, 1, ScoringConfig.Default, cards).Total;

            Assert.AreEqual(6, firstDie, "本回合第一次落子给 +6");
            Assert.AreEqual(0, secondDie, "只给第一次落子");

            var noHistory = new RunState();
            int none = ScoringPipeline.Evaluate(_topo, noHistory, cell, 1, 0, ScoringConfig.Default, cards).Total;
            Assert.AreEqual(0, none, "第一回合没有上一回合");
        }

        [Test]
        public void FirstPlus_OnlyFirstDieWithR1Hit()
        {
            var state = new RunState();
            state.Place(Board.CellId(FaceId.Front, 0, 1), 2);

            int cell = Board.CellId(FaceId.Front, 1, 1);
            ICardEffect[] cards = Cards(CardCatalog.FirstPlusId);

            int first = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, cards).Total;
            int second = ScoringPipeline.Evaluate(_topo, state, cell, 2, 1, ScoringConfig.Default, cards).Total;

            Assert.AreEqual(4, first, "第一颗：R1 2 分 + 卡 2 分");
            Assert.AreEqual(2, second, "第二颗不触发");
        }

        [Test]
        public void Combo_TriggersAfterTwoConsecutiveR1Hits()
        {
            var state = new RunState();
            state.Place(Board.CellId(FaceId.Front, 0, 1), 2);

            int cell = Board.CellId(FaceId.Front, 1, 1);
            ICardEffect combo = CardCatalog.Create(CardCatalog.ComboId);
            var cards = new[] { combo };
            var context = new PlacementContext(_topo, state, cell, 2, 0);

            // 第一次、第二次命中 R1：只累计连击，没有加成
            for (int i = 0; i < 2; i++)
            {
                ScoringResult result = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, cards);
                Assert.AreEqual(2, result.Total);
                combo.OnPlacementScored(context, result);
            }

            // 第三次命中：+2
            ScoringResult third = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, cards);
            Assert.AreEqual(4, third.Total, "连续 2 次后，下一次 R1 +2");
            combo.OnPlacementScored(context, third);

            // 触发后重置
            ScoringResult fourth = ScoringPipeline.Evaluate(_topo, state, cell, 2, 0, ScoringConfig.Default, cards);
            Assert.AreEqual(2, fourth.Total);
        }

        [Test]
        public void TwoDiagSame_StackWithBaseRules()
        {
            // 复现截图里的局面：2 张「斜同数 +1」+ 1 张「第三颗 ×2」，
            // 放的是第 2 颗骰子（×2 不生效）。
            var state = new RunState();
            int cell = Board.CellId(FaceId.Front, 1, 1);
            state.Place(Board.CellId(FaceId.Front, 0, 1), 3);   // 正交同数 +2
            state.Place(Board.CellId(FaceId.Front, 0, 0), 3);   // 对角同数：每张卡 +1
            state.Place(Board.CellId(FaceId.Front, 2, 2), 1);   // 对角异数 +1

            ICardEffect[] cards = Cards(
                CardCatalog.DiagSameId,
                CardCatalog.DiagSameId,
                CardCatalog.ThirdDoubleId);

            ScoringResult result = ScoringPipeline.Evaluate(
                _topo, state, cell, 3, 1, ScoringConfig.Default, cards);

            Assert.AreEqual(5, result.Total, "2 + 2×1 + 1 = 5");
        }

        [Test]
        public void Preview_EqualsActualWithCards()
        {
            TowerRun run = NewRun(true);
            run.Start();
            run.ChooseCard(0, -1);

            int guard = 0;
            while (!run.IsFinished && guard++ < 200)
            {
                List<int> cells = run.EmptyCellsOnCurrentFace();
                int preview = -1;
                int chosen = -1;

                foreach (int cell in cells)
                {
                    int value = run.Dice[run.PlacedThisTurn];
                    int total = run.PreviewPlacement(cell).Total;
                    if (total > preview)
                    {
                        preview = total;
                        chosen = cell;
                    }
                }

                if (preview > 0)
                {
                    int actual = run.Place(chosen).Score;
                    Assert.AreEqual(preview, actual, "预览必须等于实际得分（含卡牌加成）");
                    return;
                }

                run.Place(cells[0]);
            }

            Assert.Fail("一整局没找到有分的落点");
        }
    }
}
