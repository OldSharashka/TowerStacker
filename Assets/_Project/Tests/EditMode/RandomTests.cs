using System.Collections.Generic;
using NUnit.Framework;
using Tower.Core;

namespace Tower.Tests
{
    public class RandomTests
    {
        [Test]
        public void SameSeed_SameSequence()
        {
            var a = new DeterministicRandom(2024UL);
            var b = new DeterministicRandom(2024UL);

            for (int i = 0; i < 32; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt(), $"第 {i} 个数不同");
            }
        }

        [Test]
        public void DifferentSeed_DifferentSequence()
        {
            var a = new DeterministicRandom(1UL);
            var b = new DeterministicRandom(2UL);

            bool differs = false;
            for (int i = 0; i < 32 && !differs; i++)
            {
                if (a.NextUInt() != b.NextUInt()) differs = true;
            }

            Assert.IsTrue(differs);
        }

        [Test]
        public void NextInt_StaysInRange_AndCoversAllValues()
        {
            var rng = new DeterministicRandom(7UL);
            var seen = new HashSet<int>();

            for (int i = 0; i < 3000; i++)
            {
                int v = rng.NextInt(1, 3);
                Assert.GreaterOrEqual(v, 1);
                Assert.LessOrEqual(v, 3);
                seen.Add(v);
            }

            Assert.AreEqual(3, seen.Count, "1/2/3 都应该出现");
        }

        [Test]
        public void NextInt_ZeroOrNegative_ReturnsZero()
        {
            var rng = new DeterministicRandom(3UL);
            Assert.AreEqual(0, rng.NextInt(0));
            Assert.AreEqual(0, rng.NextInt(-5));
        }

        [Test]
        public void Shuffle_KeepsAllElements()
        {
            var list = new List<int> { 0, 1, 2, 3, 4, 5 };
            var rng = new DeterministicRandom(99UL);

            rng.Shuffle(list);

            list.Sort();
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, list);
        }

        [Test]
        public void Shuffle_IsDeterministic()
        {
            var a = new List<int> { 0, 1, 2, 3, 4, 5 };
            var b = new List<int> { 0, 1, 2, 3, 4, 5 };

            new DeterministicRandom(5UL).Shuffle(a);
            new DeterministicRandom(5UL).Shuffle(b);

            CollectionAssert.AreEqual(a, b);
        }
    }
}
