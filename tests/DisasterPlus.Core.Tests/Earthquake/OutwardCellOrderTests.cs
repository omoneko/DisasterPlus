using System.Collections.Generic;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Sweep order (second-layer review I1).
    ///
    /// Before the fix the implementation walked the rectangle in row-major order, so **the
    /// first cell it looked at was the corner furthest from the epicentre**, and the
    /// per-pass budget cut off "the buildings most likely to be destroyed". Only three
    /// things are pinned here:
    ///
    ///   1. Ordinal 0 is the epicentre itself (<see cref="StartsAtTheCentre"/>)
    ///   2. The ring (Chebyshev distance) is **non-decreasing** in the ordinal
    ///      (<see cref="RingsNeverGoBackInwards"/>) —— this is the substance of
    ///      "look at the nearer ones first"
    ///   3. Running through <see cref="OutwardCellOrder.OrdinalCount"/> ordinals passes
    ///      through every cell of the square of that radius **exactly once** (no
    ///      duplicates and nothing missed)
    /// </summary>
    public class OutwardCellOrderTests
    {
        [Fact]
        public void StartsAtTheCentre()
        {
            int dx, dz;
            Assert.True(OutwardCellOrder.Offset(0, out dx, out dz));
            Assert.Equal(0, dx);
            Assert.Equal(0, dz);
        }

        [Fact]
        public void NegativeOrdinalIsRefusedAndYieldsTheCentre()
        {
            int dx = 7, dz = 7;
            Assert.False(OutwardCellOrder.Offset(-1, out dx, out dz));
            Assert.Equal(0, dx);
            Assert.Equal(0, dz);
        }

        /// <summary>
        /// **The substance of this feature.** The ring must not go back inwards as the
        /// ordinal advances. A row-major implementation fails here (its first ordinal is
        /// on the largest ring).
        /// </summary>
        [Fact]
        public void RingsNeverGoBackInwards()
        {
            int count = OutwardCellOrder.OrdinalCount(12);
            int previous = -1;

            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));

                int ring = OutwardCellOrder.RingOf(dx, dz);
                Assert.True(ring >= previous, "ring went back inwards at ordinal " + n);
                previous = ring;
            }

            // The last ordinal is always on the outermost ring.
            Assert.Equal(12, previous);
        }

        [Fact]
        public void CoversEverySquareCellExactlyOnce()
        {
            const int radius = 9;
            int count = OutwardCellOrder.OrdinalCount(radius);
            var seen = new HashSet<int>();

            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));
                Assert.True(dx >= -radius && dx <= radius);
                Assert.True(dz >= -radius && dz <= radius);

                // Fold (dx, dz) into a single integer. The radius is 9, so it cannot collide.
                Assert.True(seen.Add((dx + radius) * 1000 + (dz + radius)),
                            "ordinal " + n + " repeated a cell");
            }

            Assert.Equal(count, seen.Count);
            Assert.Equal((2 * radius + 1) * (2 * radius + 1), count);
        }

        /// <summary>
        /// The ring is determined with an integer square root. The boundaries are
        /// **immediately before and after** <c>(2r+1)²</c>, so only those are pinned by
        /// name (a double Sqrt can be off by one there).
        /// </summary>
        [Fact]
        public void RingBoundariesAreExact()
        {
            for (int r = 1; r <= 60; r++)
            {
                int firstOfRing = (2 * r - 1) * (2 * r - 1);
                int lastOfRing = (2 * r + 1) * (2 * r + 1) - 1;

                int dx, dz;
                OutwardCellOrder.Offset(firstOfRing, out dx, out dz);
                Assert.Equal(r, OutwardCellOrder.RingOf(dx, dz));

                OutwardCellOrder.Offset(lastOfRing, out dx, out dz);
                Assert.Equal(r, OutwardCellOrder.RingOf(dx, dz));

                OutwardCellOrder.Offset(firstOfRing - 1, out dx, out dz);
                Assert.Equal(r - 1, OutwardCellOrder.RingOf(dx, dz));
            }
        }

        [Fact]
        public void OrdinalCountIsTheSquareOfTheOddSide()
        {
            Assert.Equal(1, OutwardCellOrder.OrdinalCount(0));
            Assert.Equal(1, OutwardCellOrder.OrdinalCount(-5));
            Assert.Equal(9, OutwardCellOrder.OrdinalCount(1));
            Assert.Equal(25, OutwardCellOrder.OrdinalCount(2));

            // Even the largest one actually used (a 270-cell building grid side) fits in an int.
            Assert.Equal(541 * 541, OutwardCellOrder.OrdinalCount(270));
        }

        /// <summary>
        /// Overflow must not turn into 0 or a negative. Running
        /// <c>while (n &lt; count)</c> with an overflowed value makes the sweep visit not
        /// a single cell, which is the hardest breakage of all to see.
        /// </summary>
        [Fact]
        public void OrdinalCountSaturatesInsteadOfOverflowing()
        {
            Assert.Equal(int.MaxValue, OutwardCellOrder.OrdinalCount(int.MaxValue));
            Assert.True(OutwardCellOrder.OrdinalCount(100000) > 0);
        }

        /// <summary>
        /// The ring radius covers every cell of the rectangle. It covers them even when the
        /// centre is **outside** the rectangle (the case where the epicentre was clamped
        /// because it lay off the map).
        /// </summary>
        [Fact]
        public void RingRadiusCoversTheWholeBox()
        {
            Assert.Equal(0, OutwardCellOrder.RingRadiusFor(5, 5, 5, 5, 5, 5));
            Assert.Equal(3, OutwardCellOrder.RingRadiusFor(5, 5, 2, 8, 4, 6));

            // The centre is outside the rectangle (below and to the left). It becomes the
            // distance to the furthest corner.
            Assert.Equal(10, OutwardCellOrder.RingRadiusFor(0, 0, 3, 10, 1, 4));
        }

        /// <summary>
        /// An order built from a rectangle and its centre must pass through every cell of
        /// the rectangle once. This is a direct transcription of the "skip what falls
        /// outside" that <c>LongPeriodDamage.Sweep</c> actually does.
        /// </summary>
        [Fact]
        public void SweepingAClippedBoxVisitsEveryCellOnce()
        {
            const int minX = 3, maxX = 11, minZ = 0, maxZ = 5;
            const int centreX = 4, centreZ = 1;

            int count = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var seen = new HashSet<int>();
            for (int n = 0; n < count; n++)
            {
                int dx, dz;
                Assert.True(OutwardCellOrder.Offset(n, out dx, out dz));

                int x = centreX + dx;
                int z = centreZ + dz;
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                Assert.True(seen.Add(x * 1000 + z), "cell visited twice at ordinal " + n);
            }

            Assert.Equal((maxX - minX + 1) * (maxZ - minZ + 1), seen.Count);
        }

        /// <summary>
        /// Truncation drops cells **from the outside in**. When the per-pass budget cuts
        /// the sweep short, every cell already visited is nearer the epicentre than, or on
        /// the same ring as, every cell not yet visited.
        /// </summary>
        [Fact]
        public void TruncatingDropsTheOutermostCellsFirst()
        {
            const int budget = 30;
            int count = OutwardCellOrder.OrdinalCount(6);

            int lastVisitedRing = 0;
            for (int n = 0; n < budget; n++)
            {
                int dx, dz;
                OutwardCellOrder.Offset(n, out dx, out dz);
                lastVisitedRing = OutwardCellOrder.RingOf(dx, dz);
            }

            for (int n = budget; n < count; n++)
            {
                int dx, dz;
                OutwardCellOrder.Offset(n, out dx, out dz);
                Assert.True(OutwardCellOrder.RingOf(dx, dz) >= lastVisitedRing);
            }
        }
    }
}
