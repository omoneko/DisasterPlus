using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class IgnitionSpreadTests
    {
        private static List<IgnitionCandidate> Ring(int n, float distance, bool burning = false)
        {
            var list = new List<IgnitionCandidate>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * System.Math.PI * i / n;
                var p = new Vec2((float)System.Math.Cos(a) * distance, (float)System.Math.Sin(a) * distance);
                list.Add(new IgnitionCandidate((ushort)(i + 1), p, burning));
            }
            return list;
        }

        private static int CountOverTicks(List<IgnitionCandidate> near, int strength, int ticks, float radius = 100f)
        {
            var into = new List<ushort>();
            int total = 0;
            for (uint t = 0; t < ticks; t++)
            {
                IgnitionSpread.Select(new Vec2(0f, 0f), radius, strength, near, t, into);
                total += into.Count;
            }
            return total;
        }

        [Fact]
        public void Strength0_IgnitesNothing()
        {
            Assert.Equal(0, CountOverTicks(Ring(50, 60f), 0, 200));
        }

        [Fact]
        public void Strength10_IgnitesSomething()
        {
            Assert.True(CountOverTicks(Ring(50, 60f), 10, 200) > 0);
        }

        [Fact]
        public void HigherStrength_IgnitesMore()
        {
            var near = Ring(50, 60f);
            int low = CountOverTicks(near, 2, 400);
            int high = CountOverTicks(near, 9, 400);
            Assert.True(high > low, "high=" + high + " low=" + low);
        }

        [Fact]
        public void AlreadyBurning_IsNeverSelected()
        {
            var near = Ring(50, 60f, burning: true);
            Assert.Equal(0, CountOverTicks(near, 10, 200));
        }

        [Fact]
        public void OutsideRadius_IsNeverSelected()
        {
            // With a radius of 100 m, a building 400 m away is out of scope
            Assert.Equal(0, CountOverTicks(Ring(50, 400f), 10, 200, radius: 100f));
        }

        [Fact]
        public void CloserBuildings_IgniteMoreOften()
        {
            // There must be a falloff with distance. Right beside the whirl catches fire
            // more readily.
            int close = CountOverTicks(Ring(40, 20f), 6, 400, radius: 200f);
            int far = CountOverTicks(Ring(40, 190f), 6, 400, radius: 200f);
            Assert.True(close > far, "close=" + close + " far=" + far);
        }

        [Fact]
        public void SameTickAndId_ProducesSameResult()
        {
            var near = Ring(40, 50f);
            var a = new List<ushort>();
            var b = new List<ushort>();
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 5, near, 77u, a);
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 5, near, 77u, b);
            Assert.Equal(a, b);
        }

        [Fact]
        public void DifferentTicks_ProduceDifferentResults()
        {
            // If only the same buildings are picked every tick, the fire never spreads.
            var near = Ring(60, 50f);
            var seen = new HashSet<ushort>();
            var into = new List<ushort>();
            for (uint t = 0; t < 100; t++)
            {
                IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 8, near, t, into);
                foreach (var id in into) seen.Add(id);
            }
            Assert.True(seen.Count > 5, "only " + seen.Count + " distinct buildings ignited");
        }

        [Fact]
        public void Select_ClearsOutputList()
        {
            var into = new List<ushort> { 999 };
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 0, Ring(10, 50f), 1u, into);
            Assert.DoesNotContain((ushort)999, into);
        }

        [Fact]
        public void EmptyCandidates_IsSafe()
        {
            var into = new List<ushort>();
            IgnitionSpread.Select(new Vec2(0f, 0f), 100f, 10, new List<IgnitionCandidate>(), 1u, into);
            Assert.Empty(into);
        }
    }
}
