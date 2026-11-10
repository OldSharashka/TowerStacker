namespace Tower.Core
{
    /// <summary>
    /// 每个面的局部坐标系。约定：R × U = N（右手系），
    /// r=0 在面内最上、c=0 在面内最左（都从"从外面看该面"的视角）。
    /// </summary>
    public readonly struct FaceFrame
    {
        public readonly Vec3 Right;
        public readonly Vec3 Up;
        public readonly Vec3 Normal;

        public FaceFrame(Vec3 right, Vec3 up, Vec3 normal)
        {
            Right = right;
            Up = up;
            Normal = normal;
        }
    }

    public static class FaceFrames
    {
        private static readonly FaceFrame[] Frames =
        {
            // Front (+Z)
            new FaceFrame(new Vec3(1f, 0f, 0f), new Vec3(0f, 1f, 0f), new Vec3(0f, 0f, 1f)),
            // Back (-Z)
            new FaceFrame(new Vec3(-1f, 0f, 0f), new Vec3(0f, 1f, 0f), new Vec3(0f, 0f, -1f)),
            // Right (+X)
            new FaceFrame(new Vec3(0f, 0f, -1f), new Vec3(0f, 1f, 0f), new Vec3(1f, 0f, 0f)),
            // Left (-X)
            new FaceFrame(new Vec3(0f, 0f, 1f), new Vec3(0f, 1f, 0f), new Vec3(-1f, 0f, 0f)),
            // Top (+Y)
            new FaceFrame(new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, -1f), new Vec3(0f, 1f, 0f)),
            // Bottom (-Y)
            new FaceFrame(new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 1f), new Vec3(0f, -1f, 0f)),
        };

        public static FaceFrame Get(FaceId face) => Frames[(int)face];

        /// <summary>格心在立方体表面的坐标（未归一化）。</summary>
        public static Vec3 CellCenter(FaceId face, int row, int col, float halfSize, float cellSize)
        {
            FaceFrame f = Get(face);
            Vec3 center = f.Normal * halfSize
                          + f.Right * ((col - 1) * cellSize)
                          + f.Up * ((1 - row) * cellSize);
            return center;
        }
    }
}
