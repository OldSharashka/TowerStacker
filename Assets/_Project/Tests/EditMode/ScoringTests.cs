using NUnit.Framework;
using Tower.Core;

namespace Tower.Tests
{
    /// <summary>计分规则验收（对应开发文档 2.4 / 2.6）。</summary>
    public class ScoringTests
    {
        private BoardTopology _topo;

        [SetUp]
        public void SetUp()
        {
            _topo = BoardTopology.Build();
        }

        private static int C(FaceId face, int row, int col) => Board.CellId(face, row, col);

        [Test]
        public void Example_WithEvenConfig_Gives4()
        {
            // 文档 2.6 的演算：1×K_edge + 3×K_diag
            var run = new RunState();
            var cfg = ScoringConfig.Even;

            run.Place(C(FaceId.Front, 0, 0), 1);
            run.Place(C(FaceId.Front, 0, 1), 2);
            run.Place(C(FaceId.Front, 0, 2), 1);
            run.Place(C(FaceId.Front, 1, 0), 3);
            run.Place(C(FaceId.Front, 2, 0), 1);
            run.Place(C(FaceId.Front, 2, 1), 2);

            var p1 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 2), 3, cfg);
            Assert.AreEqual(2, p1.Total, "放置 1：两个对角异数");
            run.Place(C(FaceId.Front, 1, 2), 3);

            var p2 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 2, 2), 2, cfg);
            Assert.AreEqual(1, p2.Total, "放置 2：一个正交同数");
            run.Place(C(FaceId.Front, 2, 2), 2);

            var p3 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, cfg);
            Assert.AreEqual(1, p3.Total, "放置 3：一个对角异数（三个对角同数不计）");

            Assert.AreEqual(4, p1.Total + p2.Total + p3.Total, "本回合合计");
        }

        [Test]
        public void Example_WithDefaultConfig_Gives5()
        {
            var run = new RunState();
            var cfg = ScoringConfig.Default; // 2:1

            run.Place(C(FaceId.Front, 0, 0), 1);
            run.Place(C(FaceId.Front, 0, 1), 2);
            run.Place(C(FaceId.Front, 0, 2), 1);
            run.Place(C(FaceId.Front, 1, 0), 3);
            run.Place(C(FaceId.Front, 2, 0), 1);
            run.Place(C(FaceId.Front, 2, 1), 2);

            var p1 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 2), 3, cfg);
            Assert.AreEqual(2, p1.Total);
            run.Place(C(FaceId.Front, 1, 2), 3);

            var p2 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 2, 2), 2, cfg);
            Assert.AreEqual(2, p2.Total, "一个正交同数 ×2");
            run.Place(C(FaceId.Front, 2, 2), 2);

            var p3 = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, cfg);
            Assert.AreEqual(1, p3.Total);

            Assert.AreEqual(5, p1.Total + p2.Total + p3.Total);
        }

        [Test]
        public void OrthoSame_GivesPoints()
        {
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 1), 2);

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 2, ScoringConfig.Even);

            Assert.AreEqual(1, result.Total);
            Assert.AreEqual(1, result.Contributions.Count);
            Assert.AreEqual(Scorer.RuleOrthoSame, result.Contributions[0].RuleId);
            Assert.AreEqual(C(FaceId.Front, 0, 1), result.Contributions[0].SourceCellId);
        }

        [Test]
        public void OrthoDifferent_GivesZero()
        {
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 1), 3);

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Even);

            Assert.AreEqual(0, result.Total, "正交但数字不同，不给分");
            Assert.AreEqual(0, result.Contributions.Count);
        }

        [Test]
        public void DiagSame_GivesZero()
        {
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 0), 1);

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Even);

            Assert.AreEqual(0, result.Total, "对角但数字相同，不给分");
        }

        [Test]
        public void DiagDifferent_GivesPoints()
        {
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 0), 2);

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Even);

            Assert.AreEqual(1, result.Total);
            Assert.AreEqual(Scorer.RuleDiagDiff, result.Contributions[0].RuleId);
        }

        [Test]
        public void EmptyNeighbors_AreIgnored()
        {
            var run = new RunState();

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Even);

            Assert.AreEqual(0, result.Total);
        }

        [Test]
        public void MaxScore_IsFourOrthPlusFourDiag()
        {
            // 中心格八邻全满：4 个正交放同数、4 个对角放异数 → 4×K_edge + 4×K_diag
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 1), 1); // 正交
            run.Place(C(FaceId.Front, 1, 0), 1); // 正交
            run.Place(C(FaceId.Front, 1, 2), 1); // 正交
            run.Place(C(FaceId.Front, 2, 1), 1); // 正交
            run.Place(C(FaceId.Front, 0, 0), 2); // 对角
            run.Place(C(FaceId.Front, 0, 2), 2); // 对角
            run.Place(C(FaceId.Front, 2, 0), 2); // 对角
            run.Place(C(FaceId.Front, 2, 2), 2); // 对角

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Even);

            Assert.AreEqual(8, result.Total);
            Assert.AreEqual(8, result.Contributions.Count);
        }

        [Test]
        public void VertexPair_CountsAsEdge_NotDiagonal()
        {
            // Front(0,0) 与 Left(0,2) 在顶点处既共棱又共点：必须按「边」判定
            var run = new RunState();

            // 同数 → 应当拿到正交分
            run.Place(C(FaceId.Left, 0, 2), 2);
            var same = Scorer.Evaluate(_topo, run, C(FaceId.Front, 0, 0), 2, ScoringConfig.Even);
            Assert.AreEqual(1, same.Total, "顶点邻居同数应给正交分");
            Assert.AreEqual(Scorer.RuleOrthoSame, same.Contributions[0].RuleId);

            // 异数 → 不该给对角分
            var run2 = new RunState();
            run2.Place(C(FaceId.Left, 0, 2), 3);
            var diff = Scorer.Evaluate(_topo, run2, C(FaceId.Front, 0, 0), 1, ScoringConfig.Even);
            Assert.AreEqual(0, diff.Total, "顶点邻居异数不能按对角的规则给分");
        }

        [Test]
        public void Score_IsSumOfContributions()
        {
            var run = new RunState();
            run.Place(C(FaceId.Front, 0, 0), 2);
            run.Place(C(FaceId.Front, 2, 2), 3);
            run.Place(C(FaceId.Front, 1, 0), 1);

            var result = Scorer.Evaluate(_topo, run, C(FaceId.Front, 1, 1), 1, ScoringConfig.Default);

            int sum = 0;
            foreach (var c in result.Contributions) sum += c.Amount;
            Assert.AreEqual(sum, result.Total);
        }
    }
}
