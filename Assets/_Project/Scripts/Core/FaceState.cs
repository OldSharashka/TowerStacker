using System;

namespace Tower.Core
{
    /// <summary>一个面的 3×3 格状态。0 表示空，1..3 表示数字。</summary>
    public sealed class FaceState
    {
        public const int CellCount = Board.CellsPerFace;
        public const int Empty = 0;

        private readonly int[] _values = new int[CellCount];

        public int EmptyCount { get; private set; }

        public bool IsFull => EmptyCount == 0;

        public FaceState()
        {
            EmptyCount = CellCount;
        }

        public int this[int index] => _values[index];

        public bool IsEmpty(int index) => _values[index] == Empty;

        public void Place(int index, int value)
        {
            if (index < 0 || index >= CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "面内索引必须是 0..8");
            }
            if (value <= Empty)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "数字必须 >= 1");
            }
            if (_values[index] != Empty)
            {
                throw new InvalidOperationException($"格 {index} 已有数字 {_values[index]}，不能重复放置");
            }

            _values[index] = value;
            EmptyCount--;
        }

        public void Clear()
        {
            Array.Clear(_values, 0, CellCount);
            EmptyCount = CellCount;
        }

        public int[] Snapshot() => (int[])_values.Clone();

        public bool IsRingCell(int index) => index != 4; // 中心格是 4

        public override string ToString()
        {
            return $"[{_values[0]}{_values[1]}{_values[2]}/" +
                   $"{_values[3]}{_values[4]}{_values[5]}/" +
                   $"{_values[6]}{_values[7]}{_values[8]}]";
        }
    }
}
