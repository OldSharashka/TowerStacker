using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tower.Core;

namespace Tower.Tests
{
    /// <summary>回合流程与整局闭环（对应开发文档 2.3 / 7.1）。</summary>
    public class FlowTests
    {
        private static TowerRun NewRun(ulong seed = 42UL)
        {
            return new TowerRun(
                seed,
                BoardTopology.Build(),
                ScoringConfig.Default,
                new RunSettings { TargetScore = 170 });
        }

        /// <summary>用「每次填该面第一个空格」的固定策略把一局打完。</summary>
        private static void PlayWholeRun(TowerRun run)
        {
            int guard = 0;
            while (!run.IsFinished)
            {
                if (++guard > 200) throw new InvalidOperationException("回合没有收敛，可能死循环");

                List<int> cells = run.EmptyCellsOnCurrentFace();
                Assert.GreaterOrEqual(cells.Count, 1, "当前面没有可放置的格");
                run.Place(cells[0]);
            }
        }

        [Test]
        public void Settings_TurnsAndCellsMatchBoard()
        {
            var settings = new RunSettings();
            Assert.AreEqual(18, settings.TurnsPerRun);
            Assert.AreEqual(54, settings.TotalCells);
        }

        [Test]
        public void Start_EntersPlacingPhase_WithThreeDice()
        {
            TowerRun run = NewRun();
            run.Start();

            Assert.AreEqual(TurnPhase.Placing, run.Phase);
            Assert.AreEqual(3, run.Dice.Count);
            Assert.AreEqual(1, run.RerollsLeft);
            Assert.AreEqual(0, run.PlacedThisTurn);
        }

        [Test]
        public void FullRun_FillsAll54Cells_AndEnds()
        {
            TowerRun run = NewRun();
            run.Start();
            PlayWholeRun(run);

            Assert.AreEqual(TurnPhase.RunEnded, run.Phase);
            Assert.AreEqual(54, run.State.PlacedCount, "必须刚好填满 54 格");
            Assert.IsTrue(run.State.IsBoardFull);
            Assert.AreEqual(18, run.State.History.Count, "应当有 18 个回合记录");
        }

        [Test]
        public void EachFace_IsUsedExactlyThreeTimes()
        {
            TowerRun run = NewRun();
            run.Start();
            PlayWholeRun(run);

            var counts = new Dictionary<FaceId, int>();
            foreach (TurnRecord record in run.State.History)
            {
                counts.TryGetValue(record.Face, out int n);
                counts[record.Face] = n + 1;
            }

            Assert.AreEqual(6, counts.Count, "6 个面都应该被用到");
            foreach (var pair in counts)
            {
                Assert.AreEqual(3, pair.Value, $"面 {pair.Key} 应当被轮到 3 次");
            }
        }

        [Test]
        public void EveryTurn_HasExactlyThreePlacements()
        {
            TowerRun run = NewRun();
            run.Start();
            PlayWholeRun(run);

            foreach (TurnRecord record in run.State.History)
            {
                Assert.AreEqual(3, record.PlacedCells.Count, $"回合 {record.TurnIndex} 放置数不对");
                Assert.AreEqual(3, record.PlacedValues.Count);
            }
        }

        [Test]
        public void TotalScore_EqualsSumOfTurnScores()
        {
            TowerRun run = NewRun();
            run.Start();
            PlayWholeRun(run);

            int sum = 0;
            foreach (TurnRecord record in run.State.History) sum += record.Score;

            Assert.AreEqual(sum, run.State.TotalScore);
        }

        [Test]
        public void Dice_AlwaysWithinRange()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                TowerRun run = NewRun(seed);
                run.Start();
                PlayWholeRun(run);

                foreach (TurnRecord record in run.State.History)
                {
                    foreach (int d in record.Dice)
                    {
                        Assert.GreaterOrEqual(d, 1);
                        Assert.LessOrEqual(d, 3);
                    }
                }
            }
        }

        [Test]
        public void SameSeed_ProducesIdenticalRun()
        {
            TowerRun a = NewRun(12345UL);
            a.Start();
            PlayWholeRun(a);

            TowerRun b = NewRun(12345UL);
            b.Start();
            PlayWholeRun(b);

            Assert.AreEqual(a.State.TotalScore, b.State.TotalScore, "同种子必须同分");

            for (int i = 0; i < a.State.History.Count; i++)
            {
                TurnRecord ra = a.State.History[i];
                TurnRecord rb = b.State.History[i];
                Assert.AreEqual(ra.Face, rb.Face, $"第 {i} 回合的面不同");
                CollectionAssert.AreEqual(ra.Dice, rb.Dice, $"第 {i} 回合的骰子不同");
                CollectionAssert.AreEqual(ra.PlacedCells, rb.PlacedCells, $"第 {i} 回合的落点不同");
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentRun()
        {
            TowerRun a = NewRun(1UL);
            a.Start();
            PlayWholeRun(a);

            TowerRun b = NewRun(2UL);
            b.Start();
            PlayWholeRun(b);

            bool anyDifference = a.State.TotalScore != b.State.TotalScore;
            for (int i = 0; i < a.State.History.Count && !anyDifference; i++)
            {
                if (!Equals(a.State.History[i].Face, b.State.History[i].Face)) anyDifference = true;
                else if (!ListEquals(a.State.History[i].Dice, b.State.History[i].Dice)) anyDifference = true;
            }

            Assert.IsTrue(anyDifference, "不同种子应当产生不同的一局");
        }

        private static bool ListEquals(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        [Test]
        public void Reroll_IsAllowedOncePerTurn()
        {
            TowerRun run = NewRun();
            run.Start();

            Assert.IsTrue(run.Reroll(0b111), "第一次重掷应当成功");
            Assert.AreEqual(0, run.RerollsLeft);
            Assert.IsFalse(run.Reroll(0b111), "同一回合不能重掷第二次");
        }

        [Test]
        public void Reroll_AfterPlacing_IsRejected()
        {
            TowerRun run = NewRun();
            run.Start();
            run.Place(run.EmptyCellsOnCurrentFace()[0]);

            Assert.IsFalse(run.Reroll(0b111), "已经放了骰子就不该再重掷");
        }

        [Test]
        public void Reroll_RestoredOnNextTurn()
        {
            TowerRun run = NewRun();
            run.Start();
            List<int> cells = run.EmptyCellsOnCurrentFace();
            for (int i = 0; i < 3; i++) run.Place(cells[i]);

            Assert.AreEqual(1, run.RerollsLeft, "新回合应当重置重掷次数");
        }

        [Test]
        public void Place_OnWrongFace_Throws()
        {
            TowerRun run = NewRun();
            run.Start();

            int wrongFaceCell = -1;
            for (int cell = 0; cell < Board.CellCount; cell++)
            {
                if (Board.FaceOf(cell) != run.CurrentFace) { wrongFaceCell = cell; break; }
            }

            Assert.Throws<InvalidOperationException>(() => run.Place(wrongFaceCell));
        }

        [Test]
        public void Place_OnOccupiedCell_Throws()
        {
            TowerRun run = NewRun();
            run.Start();

            List<int> cells = run.EmptyCellsOnCurrentFace();
            run.Place(cells[0]);

            if (run.Phase == TurnPhase.Placing)
            {
                Assert.Throws<InvalidOperationException>(() => run.Place(cells[0]));
            }
        }

        [Test]
        public void Place_BeforeStart_Throws()
        {
            TowerRun run = NewRun();
            Assert.Throws<InvalidOperationException>(() => run.Place(0));
        }

        [Test]
        public void Start_Twice_Throws()
        {
            TowerRun run = NewRun();
            run.Start();
            Assert.Throws<InvalidOperationException>(() => run.Start());
        }

        [Test]
        public void PlacementResult_CarriesContributionBreakdown()
        {
            TowerRun run = NewRun();
            run.Start();

            List<int> cells = run.EmptyCellsOnCurrentFace();
            PlacementResult first = run.Place(cells[0]);

            Assert.AreEqual(cells[0], first.CellId);
            Assert.AreEqual(run.State.ValueAt(cells[0]), first.Value);
            Assert.IsNotNull(first.Contributions);
            Assert.AreEqual(first.Score, Sum(first.Contributions));
        }

        private static int Sum(IReadOnlyList<ScoreContribution> list)
        {
            int total = 0;
            for (int i = 0; i < list.Count; i++) total += list[i].Amount;
            return total;
        }
    }
}
