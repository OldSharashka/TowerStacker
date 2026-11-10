using System.Collections.Generic;

namespace Tower.Core
{
    /// <summary>
    /// 确定性随机（PCG32）。禁止使用 UnityEngine.Random：
    /// 它的全局状态无法复现，会让「同种子同一局」失效。
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;
        private readonly ulong _inc;

        public DeterministicRandom(ulong seed)
        {
            _inc = (seed << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * 6364136223846793005UL + _inc;

            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>[0, maxExclusive) 的均匀整数，用拒绝采样去掉取模偏差。</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;

            uint bound = (uint)maxExclusive;
            uint threshold = (uint)(-(int)bound) % bound;

            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>[minInclusive, maxInclusive] 的均匀整数。</summary>
        public int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) return minInclusive;
            return minInclusive + NextInt(maxInclusive - minInclusive + 1);
        }

        /// <summary>Fisher–Yates 洗牌。</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
