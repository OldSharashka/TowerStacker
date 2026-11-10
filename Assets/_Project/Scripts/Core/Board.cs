namespace Tower.Core
{
    /// <summary>
    /// 立方体的六个面。枚举值同时也是面索引，顺序不要改，会影响 cellId 与存档兼容。
    /// </summary>
    public enum FaceId
    {
        Front = 0,   // +Z
        Back = 1,    // -Z
        Right = 2,   // +X
        Left = 3,    // -X
        Top = 4,     // +Y
        Bottom = 5,  // -Y
    }

    /// <summary>
    /// 棋盘的静态结构。54 = 6 面 × 3×3。
    /// cellId = faceIndex * 9 + row * 3 + col，取值 0..53。
    /// </summary>
    public static class Board
    {
        public const int FaceCount = 6;
        public const int CellsPerRow = 3;
        public const int CellsPerFace = CellsPerRow * CellsPerRow;
        public const int CellCount = FaceCount * CellsPerFace;

        public static int CellId(FaceId face, int row, int col)
        {
            return (int)face * CellsPerFace + row * CellsPerRow + col;
        }

        public static FaceId FaceOf(int cellId) => (FaceId)(cellId / CellsPerFace);

        public static int RowOf(int cellId) => (cellId % CellsPerFace) / CellsPerRow;

        public static int ColOf(int cellId) => (cellId % CellsPerFace) % CellsPerRow;

        /// <summary>该格在所属面内的序号 0..8。</summary>
        public static int IndexInFace(int cellId) => cellId % CellsPerFace;
    }
}
