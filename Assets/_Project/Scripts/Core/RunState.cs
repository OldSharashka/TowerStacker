using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>一局的可变状态。</summary>
    public sealed class RunState
    {
        public readonly FaceState[] Faces = new FaceState[Board.FaceCount];

        public ulong Seed;
        public int TurnIndex;          // 0..17
        public int RoundIndex;         // 0..2
        public int TotalScore;
        public int TargetScore;

        public readonly List<TurnRecord> History = new List<TurnRecord>();

        /// <summary>每次三选一的记录（供复现与导出）。</summary>
        public readonly List<CardChoiceRecord> CardHistory = new List<CardChoiceRecord>();

        public RunState()
        {
            for (int i = 0; i < Faces.Length; i++)
            {
                Faces[i] = new FaceState();
            }
        }

        public int ValueAt(int cellId)
        {
            return Faces[(int)Board.FaceOf(cellId)][Board.IndexInFace(cellId)];
        }

        public bool IsEmpty(int cellId) => ValueAt(cellId) == FaceState.Empty;

        public void Place(int cellId, int value)
        {
            Faces[(int)Board.FaceOf(cellId)].Place(Board.IndexInFace(cellId), value);
        }

        public int PlacedCount
        {
            get
            {
                int placed = 0;
                for (int i = 0; i < Faces.Length; i++)
                {
                    placed += FaceState.CellCount - Faces[i].EmptyCount;
                }
                return placed;
            }
        }

        public bool IsBoardFull => PlacedCount == Board.CellCount;
    }

    /// <summary>一个回合的存档记录，用于结算与调优导出。</summary>
    public sealed class TurnRecord
    {
        public int TurnIndex;
        public FaceId Face;
        public int[] Dice = new int[3];
        public readonly List<int> PlacedCells = new List<int>(3);
        public readonly List<int> PlacedValues = new List<int>(3);
        public int Score;
        public int Cumulative;

        /// <summary>本回合 R1（边同数）命中的邻居次数，CSV 明细用。</summary>
        public int EdgeSameHits;

        /// <summary>本回合 R2（角异数）命中的邻居次数，CSV 明细用。</summary>
        public int DiagDiffHits;
    }
}
