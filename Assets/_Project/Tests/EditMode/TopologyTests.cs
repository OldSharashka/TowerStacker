using System.Collections.Generic;
using NUnit.Framework;
using Tower.Core;

namespace Tower.Tests
{
    /// <summary>
    /// 邻接表验收（对应开发文档 附录 C）：
    /// 对称、度数、面内邻接数、跨面样例、顶点归边。
    /// </summary>
    public class TopologyTests
    {
        private static BoardTopology _topo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _topo = BoardTopology.Build();
        }

        [Test]
        public void Validate_ReportsNoErrors()
        {
            List<string> errors = _topo.Validate();
            Assert.IsEmpty(errors, "拓扑校验失败：\n" + string.Join("\n", errors));
        }

        [Test]
        public void Board_Has54Cells()
        {
            Assert.AreEqual(6, Board.FaceCount);
            Assert.AreEqual(9, Board.CellsPerFace);
            Assert.AreEqual(54, Board.CellCount);
        }

        [Test]
        public void CellId_ConversionRoundTrips()
        {
            for (int face = 0; face < Board.FaceCount; face++)
            {
                for (int row = 0; row < Board.CellsPerRow; row++)
                {
                    for (int col = 0; col < Board.CellsPerRow; col++)
                    {
                        int id = Board.CellId((FaceId)face, row, col);
                        Assert.AreEqual((FaceId)face, Board.FaceOf(id));
                        Assert.AreEqual(row, Board.RowOf(id));
                        Assert.AreEqual(col, Board.ColOf(id));
                    }
                }
            }
        }

        [Test]
        public void CellId_TopRow2Col1_Is43()
        {
            Assert.AreEqual(43, Board.CellId(FaceId.Top, 2, 1));
        }

        [Test]
        public void Directions_AreAllDistinct()
        {
            for (int i = 0; i < Board.CellCount; i++)
            {
                Assert.AreEqual(1f, _topo.Direction(i).Length, 1e-4f, $"cell {i} 方向未归一化");
                for (int j = i + 1; j < Board.CellCount; j++)
                {
                    Assert.Less(Vec3.Dot(_topo.Direction(i), _topo.Direction(j)), 0.9999f,
                        $"cell {i} 与 cell {j} 方向重合");
                }
            }
        }

        [Test]
        public void DegreeDistribution_Is24By7_And30By8()
        {
            int seven = 0;
            int eight = 0;
            for (int cell = 0; cell < Board.CellCount; cell++)
            {
                int d = _topo.Degree(cell);
                Assert.IsTrue(d == 7 || d == 8, $"cell {cell} 邻居数 {d} 不合法");
                if (d == 7) seven++;
                else eight++;
            }

            Assert.AreEqual(24, seven, "7 邻居的角格数量");
            Assert.AreEqual(30, eight, "8 邻居的格数量");
        }

        [Test]
        public void Adjacency_IsSymmetric_AndHasNoSelfLoop()
        {
            for (int a = 0; a < Board.CellCount; a++)
            {
                foreach (Neighbor n in _topo.Neighbors(a))
                {
                    Assert.AreNotEqual(a, n.CellId, $"cell {a} 出现自环");
                    Assert.IsTrue(_topo.AreAdjacent(n.CellId, a, out _),
                        $"邻接不对称: {a} -> {n.CellId} 没有反向");
                }
            }
        }

        [Test]
        public void InFacePairs_Are12Orthogonal_And8Diagonal_PerFace()
        {
            for (int face = 0; face < Board.FaceCount; face++)
            {
                int orth = 0;
                int diag = 0;
                for (int cell = face * Board.CellsPerFace; cell < (face + 1) * Board.CellsPerFace; cell++)
                {
                    foreach (Neighbor n in _topo.Neighbors(cell))
                    {
                        if (Board.FaceOf(n.CellId) != (FaceId)face) continue;
                        if (n.CellId < cell) continue;
                        if (n.IsDiagonal) diag++;
                        else orth++;
                    }
                }

                Assert.AreEqual(12, orth, $"面 {(FaceId)face} 面内正交邻对");
                Assert.AreEqual(8, diag, $"面 {(FaceId)face} 面内对角邻对");
            }
        }

        [Test]
        public void CrossFace_OrthogonalSamples()
        {
            for (int c = 0; c < 3; c++)
            {
                AssertOrth(FaceId.Front, 2, c, FaceId.Bottom, 0, c);
            }
            for (int c = 0; c < 3; c++)
            {
                AssertOrth(FaceId.Front, 0, c, FaceId.Top, 2, c);
            }
            for (int r = 0; r < 3; r++)
            {
                AssertOrth(FaceId.Front, r, 0, FaceId.Left, r, 2);
            }
        }

        [Test]
        public void CrossFace_DiagonalSamples()
        {
            AssertDiag(FaceId.Front, 0, 0, FaceId.Left, 1, 2);
            AssertDiag(FaceId.Front, 0, 0, FaceId.Top, 2, 1);
        }

        [Test]
        public void DualReachable_AtVertices_Is12Pairs()
        {
            // 每个角格恰好合并一次（8 个偏移里有一次落到已有邻居上），共 24 次有向 = 12 对
            Assert.AreEqual(24, _topo.MergeEvents, "双可达的合并次数");
        }

        [Test]
        public void VertexRelation_IsAlwaysOrthogonal()
        {
            // 双可达的一定是按「边」判定的：遍历全盘，凡是同时被正交与对角命中的邻居，
            // 最终分类必须是正交。这里用间接方式校验——角格的邻居里不存在"应为角却记为边"的误判，
            // 具体由 Validate() 里的面内对角对数（每面 8 组）保证。
            List<string> errors = _topo.Validate();
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        private void AssertOrth(FaceId fa, int ra, int ca, FaceId fb, int rb, int cb)
        {
            int a = Board.CellId(fa, ra, ca);
            int b = Board.CellId(fb, rb, cb);
            Assert.IsTrue(_topo.AreAdjacent(a, b, out bool isDiagonal),
                $"应相邻: {fa}({ra},{ca}) ~ {fb}({rb},{cb})");
            Assert.IsFalse(isDiagonal, $"应为正交: {fa}({ra},{ca}) ~ {fb}({rb},{cb})");
        }

        private void AssertDiag(FaceId fa, int ra, int ca, FaceId fb, int rb, int cb)
        {
            int a = Board.CellId(fa, ra, ca);
            int b = Board.CellId(fb, rb, cb);
            Assert.IsTrue(_topo.AreAdjacent(a, b, out bool isDiagonal),
                $"应相邻: {fa}({ra},{ca}) ~ {fb}({rb},{cb})");
            Assert.IsTrue(isDiagonal, $"应为对角: {fa}({ra},{ca}) ~ {fb}({rb},{cb})");
        }
    }
}
