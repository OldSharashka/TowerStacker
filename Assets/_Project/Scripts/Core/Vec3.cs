using System;

namespace Tower.Core
{
    /// <summary>纯 C# 的三维向量，避免 Core 层依赖 UnityEngine。</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float k) => new Vec3(a.X * k, a.Y * k, a.Z * k);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public float SqrLength => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(SqrLength);

        public Vec3 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-9f ? new Vec3(X / len, Y / len, Z / len) : Zero;
            }
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (X.GetHashCode() * 397) ^ (Y.GetHashCode() * 31) ^ Z.GetHashCode(); }
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }
}
