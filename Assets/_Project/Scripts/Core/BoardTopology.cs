using System;
using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>一个邻居，以及它是「边相邻」还是「角相邻」。</summary>
    public readonly struct Neighbor
    {
        public readonly int CellId;
        public readonly bool IsDiagonal;

        public Neighbor(int cellId, bool isDiagonal)
        {
            CellId = cellId;
            IsDiagonal = isDiagonal;
        }

        public override string ToString() =>
            $"{(IsDiagonal ? "diag" : "orth")}->{CellId}";
    }

    /// <summary>
    /// 54 格的邻接表。规则见开发文档 2.2：
    /// 8 个偏移 + 几何最近格 + 同一邻居既边又角时按「边」（正交）判定。
    /// </summary>
    public sealed class BoardTopology
    {
        public const int MaxNeighbors = 8;

        private readonly Neighbor[][] _adj;
        private readonly int[] _degree;
        private readonly Vec3[] _directions;

        /// <summary>有多少次「同一邻居被两个偏移命中」的合并（有向），应为 24（=12 对）。</summary>
        public int MergeEvents { get; }

        public IReadOnlyList<Neighbor> Neighbors(int cellId) => _adj[cellId];
        public int Degree(int cellId) => _degree[cellId];
        public Vec3 Direction(int cellId) => _directions[cellId];
        public int Count => _adj.Length;

        private BoardTopology(Vec3[] directions, Neighbor[][] adj, int[] degree, int mergeEvents)
        {
            _directions = directions;
            _adj = adj;
            _degree = degree;
            MergeEvents = mergeEvents;
        }

        public static BoardTopology Build(float cubeHalf = 1f, float cellSize = 2f / 3f)
        {
            var directions = new Vec3[Board.CellCount];
            for (int face = 0; face < Board.FaceCount; face++)
            {
                for (int row = 0; row < Board.CellsPerRow; row++)
                {
                    for (int col = 0; col < Board.CellsPerRow; col++)
                    {
                        int id = Board.CellId((FaceId)face, row, col);
                        directions[id] = FaceFrames
                            .CellCenter((FaceId)face, row, col, cubeHalf, cellSize)
                            .Normalized;
                    }
                }
            }

            var adj = new Neighbor[Board.CellCount][];
            var degree = new int[Board.CellCount];
            int mergeEvents = 0;

            for (int cellId = 0; cellId < Board.CellCount; cellId++)
            {
                FaceId face = Board.FaceOf(cellId);
                int row = Board.RowOf(cellId);
                int col = Board.ColOf(cellId);
                Vec3 center = FaceFrames.CellCenter(face, row, col, cubeHalf, cellSize);
                FaceFrame frame = FaceFrames.Get(face);

                var merged = new Dictionary<int, bool>(MaxNeighbors);

                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;

                        // 沿面的切平面走一步，可能越出本面
                        Vec3 probe = center
                                     + frame.Right * (dc * cellSize)
                                     + frame.Up * (-dr * cellSize);

                        int nearest = FindNearest(probe.Normalized, directions);
                        bool isDiagonal = dr != 0 && dc != 0;

                        if (merged.TryGetValue(nearest, out bool alreadyDiagonal))
                        {
                            // 合并：正交优先（false 一旦出现就保持 false）
                            merged[nearest] = alreadyDiagonal && isDiagonal;
                            mergeEvents++;
                        }
                        else
                        {
                            merged[nearest] = isDiagonal;
                        }
                    }
                }

                var keys = new List<int>(merged.Keys);
                keys.Sort();
                var list = new Neighbor[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    list[i] = new Neighbor(keys[i], merged[keys[i]]);
                }

                adj[cellId] = list;
                degree[cellId] = list.Length;
            }

            return new BoardTopology(directions, adj, degree, mergeEvents);
        }

        private static int FindNearest(Vec3 direction, Vec3[] candidates)
        {
            int best = -1;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < candidates.Length; i++)
            {
                float d = Vec3.Dot(direction, candidates[i]);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }
            return best;
        }

        public bool AreAdjacent(int a, int b, out bool isDiagonal)
        {
            var list = _adj[a];
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i].CellId == b)
                {
                    isDiagonal = list[i].IsDiagonal;
                    return true;
                }
            }
            isDiagonal = false;
            return false;
        }

        /// <summary>返回所有不满足约定的地方；空列表表示拓扑正确。</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            // 1. 54 个方向两两不同
            for (int i = 0; i < _directions.Length; i++)
            {
                for (int j = i + 1; j < _directions.Length; j++)
                {
                    if (Vec3.Dot(_directions[i], _directions[j]) > 0.9999f)
                    {
                        errors.Add($"方向重合: cell {i} 与 cell {j}");
                    }
                }
            }

            // 2. 无自环 + 对称 + 度数
            int degree7 = 0;
            int degree8 = 0;
            for (int a = 0; a < Board.CellCount; a++)
            {
                if (_degree[a] == 7) degree7++;
                else if (_degree[a] == 8) degree8++;
                else errors.Add($"cell {a} 的邻居数异常: {_degree[a]}");

                foreach (var n in _adj[a])
                {
                    if (n.CellId == a) errors.Add($"cell {a} 自环");
                    if (!AreAdjacent(n.CellId, a, out _)) errors.Add($"不对称: {a} -> {n.CellId}");
                }
            }
            if (degree7 != 24) errors.Add($"7 邻居的格应为 24，实际 {degree7}");
            if (degree8 != 30) errors.Add($"8 邻居的格应为 30，实际 {degree8}");

            // 3. 合并次数：每个角格一次，共 24 次（12 对）
            if (MergeEvents != 24) errors.Add($"合并次数应为 24，实际 {MergeEvents}");

            // 4. 每个面内：12 组正交邻对、8 组对角邻对（无向）
            for (int face = 0; face < Board.FaceCount; face++)
            {
                int orth = 0;
                int diag = 0;
                for (int r = 0; r < Board.CellsPerRow; r++)
                {
                    for (int c = 0; c < Board.CellsPerRow; c++)
                    {
                        int a = Board.CellId((FaceId)face, r, c);
                        foreach (var n in _adj[a])
                        {
                            if (Board.FaceOf(n.CellId) != (FaceId)face) continue;
                            if (n.CellId < a) continue; // 只数一次
                            if (n.IsDiagonal) diag++;
                            else orth++;
                        }
                    }
                }
                if (orth != 12) errors.Add($"面 {(FaceId)face} 面内正交邻对应为 12，实际 {orth}");
                if (diag != 8) errors.Add($"面 {(FaceId)face} 面内对角邻对应为 8，实际 {diag}");
            }

            // 5. 跨面样例（写死几对，防止"看起来对"）
            CheckOrth(errors, FaceId.Front, 2, 0, FaceId.Bottom, 0, 0);
            CheckOrth(errors, FaceId.Front, 2, 1, FaceId.Bottom, 0, 1);
            CheckOrth(errors, FaceId.Front, 2, 2, FaceId.Bottom, 0, 2);
            CheckOrth(errors, FaceId.Front, 0, 0, FaceId.Top, 2, 0);
            CheckOrth(errors, FaceId.Front, 0, 1, FaceId.Top, 2, 1);
            CheckOrth(errors, FaceId.Front, 0, 2, FaceId.Top, 2, 2);
            CheckOrth(errors, FaceId.Front, 0, 0, FaceId.Left, 0, 2);
            CheckOrth(errors, FaceId.Front, 1, 0, FaceId.Left, 1, 2);
            CheckOrth(errors, FaceId.Front, 2, 0, FaceId.Left, 2, 2);
            CheckDiag(errors, FaceId.Front, 0, 0, FaceId.Left, 1, 2);
            CheckDiag(errors, FaceId.Front, 0, 0, FaceId.Top, 2, 1);

            return errors;
        }

        private void CheckOrth(List<string> errors, FaceId fa, int ra, int ca, FaceId fb, int rb, int cb)
        {
            CheckRelation(errors, fa, ra, ca, fb, rb, cb, false);
        }

        private void CheckDiag(List<string> errors, FaceId fa, int ra, int ca, FaceId fb, int rb, int cb)
        {
            CheckRelation(errors, fa, ra, ca, fb, rb, cb, true);
        }

        private void CheckRelation(List<string> errors, FaceId fa, int ra, int ca,
                                   FaceId fb, int rb, int cb, bool expectDiagonal)
        {
            int a = Board.CellId(fa, ra, ca);
            int b = Board.CellId(fb, rb, cb);
            if (!AreAdjacent(a, b, out bool isDiagonal))
            {
                errors.Add($"应相邻但不相邻: {fa}({ra},{ca}) ~ {fb}({rb},{cb})");
                return;
            }
            if (isDiagonal != expectDiagonal)
            {
                errors.Add($"关系类型错误: {fa}({ra},{ca}) ~ {fb}({rb},{cb}) 期望 {(expectDiagonal ? "对角" : "正交")}");
            }
        }
    }
}
