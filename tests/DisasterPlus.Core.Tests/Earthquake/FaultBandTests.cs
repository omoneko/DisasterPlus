using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// Fault band geometry. **The full review (C1) found it was 33% too wide, so it was rebuilt.**
    ///
    /// What was wrong: the reach was taken to be <c>destructionRadiusMax = 2w</c>. The actual
    /// call is <c>preRadius: w</c> (see the call list in §A-3), and
    /// <c>DisasterHelpers.DestroyBuildings</c> puts <c>if (dist &gt;= preRadius) continue;</c>
    /// **before the seed generation and before the ramp is computed** (the <c>bge.un</c> at
    /// IL_0130). The 2w only takes effect in the numerator of fD, and for <c>dist &lt; w</c>
    /// we have fD &gt; 1 and probability = 1, so the comparison is unconditionally true ——
    /// which is to say 2w appears nowhere.
    ///
    /// The tests in this file **write out the derivation** (rather than pinning a single raw
    /// number). The previous version pinned the number 200 itself, which left the wrong model
    /// in the state of being "protected by tests".
    /// </summary>
    public class FaultBandTests
    {
        private const float L = 1000f;
        private const float W = 100f;

        // At angle 0, dir = (-sin 0, cos 0) = (0, 1), i.e. the fault runs along the Z axis.
        private static FaultBand Sample()
        {
            return new FaultBand(new Vec2(0f, 0f), 0f, length: L, width: W);
        }

        [Fact]
        public void DirectionMatchesTheVanillaFormula()
        {
            var band = Sample();
            Assert.Equal(0f, band.Direction.X, 4);
            Assert.Equal(1f, band.Direction.Z, 4);

            var rotated = new FaultBand(new Vec2(0f, 0f), (float)(Math.PI / 2), L, W);
            Assert.Equal(-1f, rotated.Direction.X, 3);
            Assert.Equal(0f, rotated.Direction.Z, 3);
        }

        [Fact]
        public void PatchRadiusIsTheTaperedWidth()
        {
            var band = Sample();
            // IL_01D6–01EA: w = W * (1 - 4t²). At |t| = 0.5 it is exactly 0.
            Assert.Equal(W, band.PatchRadiusAt(0f), 3);
            Assert.Equal(W * (1f - 4f * 0.4f * 0.4f), band.PatchRadiusAt(0.4f), 3);
            Assert.Equal(0f, band.PatchRadiusAt(0.5f), 3);
        }

        [Fact]
        public void HalfWidthIsTheReachPlusTheMeander()
        {
            var band = Sample();

            // The envelope across the fault = the disc's reach w (= preRadius)
            //                                + the width the meander can shift the centre by,
            //                                  0.5w (s * w * 0.5 for s in [-1,1])
            //                                = 1.5w
            foreach (float t in new[] { 0f, 0.2f, 0.4f })
            {
                float w = band.PatchRadiusAt(t);
                Assert.Equal(w + 0.5f * w, band.HalfWidthAt(t), 3);
            }

            Assert.Equal(0f, band.HalfWidthAt(0.5f), 3);
            Assert.True(band.HalfWidthAt(0.4f) < band.HalfWidthAt(0f));
        }

        [Fact]
        public void HalfWidthIsNotTheOldTwoWModel()
        {
            // Regression test. It used to return 2w * taper, which was 200 at the epicentre.
            var band = Sample();
            Assert.Equal(1.5f * W, band.HalfWidthAt(0f), 3);
            Assert.NotEqual(2f * W, band.HalfWidthAt(0f), 3);
        }

        [Fact]
        public void EpicentreIsInside()
        {
            Assert.True(Sample().Contains(new Vec2(0f, 0f)));
        }

        [Fact]
        public void AcrossTheFaultIsBoundedByOnePointFiveW()
        {
            var band = Sample();
            float limit = band.HalfWidthAt(0f);   // = 1.5 * W = 150

            Assert.True(band.Contains(new Vec2(limit - 5f, 0f)));
            Assert.False(band.Contains(new Vec2(limit + 5f, 0f)));

            // The edge of the old model (2w = 200) is now outside the band.
            Assert.False(band.Contains(new Vec2(190f, 0f)));
        }

        [Fact]
        public void AlongTheFaultReachesPastTheLastPatchCentreByItsRadius()
        {
            var band = Sample();

            // Disc centres only go at t in [-0.4, 0.4], i.e. no further than |z| <= 0.4L = 400.
            // But that disc destroys out to its own radius w(0.4) beyond. The previous Contains
            // treated |t| > 0.4 as unconditionally outside, so it missed exactly w at each end.
            float endW = band.PatchRadiusAt(FaultBand.MaxOffset);   // = 36
            float lastCentre = FaultBand.MaxOffset * L;             // = 400

            Assert.True(band.Contains(new Vec2(0f, lastCentre + endW * 0.5f)));
            Assert.True(band.Contains(new Vec2(0f, -(lastCentre + endW * 0.5f))));
            Assert.False(band.Contains(new Vec2(0f, lastCentre + endW * 2f)));
        }

        [Fact]
        public void AFatterDiscFurtherAlongTheFaultStillCounts()
        {
            // **Regression test for a flaw found by the independent review.**
            // Contains at first checked the circle intersection using only the single t
            // closest to the point. w thins out with t, so the t that minimises the
            // along-strike residual is not necessarily the t that maximises the reach ——
            // a fatter disc nearer the centre can still reach.
            //
            //   t = 0.30 … w = 64.00, residual 0, across-strike slack 99 - 32.00 = 67.00 → out
            //   t = 0.28 … w = 68.64, residual 20, across-strike slack 99 - 34.32 = 64.68
            //              → **in**, since 20² + 64.68² = 4583 < 68.64² = 4712
            //
            // The direction of the error was the worst possible: it called a building inside
            // the band an outside one, and would then claim "will not collapse" from the
            // overall disc for that building.
            var band = Sample();
            Assert.True(band.Contains(new Vec2(99f, 300f)));

            // Confirm independently that the disc at t = 0.28 does reach.
            float w = band.PatchRadiusAt(0.28f);
            double da = 300.0 - 0.28 * L;
            double dacross = 99.0 - 0.5 * w;
            Assert.True(da * da + dacross * dacross <= (double)w * w);
        }

        [Fact]
        public void ContainsAgreesWithABruteForceSweepOverAllPatchPositions()
        {
            // The sweep inside Contains (96 divisions + 24 refinements) must agree with a
            // naive exhaustive search. Right next to the true boundary (+/-1 m) the verdict
            // can wobble, so those points are excluded from the comparison.
            var band = Sample();

            for (float along = -520f; along <= 520f; along += 13f)
            {
                for (float across = 0f; across <= 170f; across += 3f)
                {
                    double best = BruteForceGap(along, across);
                    if (System.Math.Abs(best) < 200.0) continue;   // skip near the boundary

                    Assert.Equal(best <= 0.0, band.Contains(new Vec2(across, along)));
                }
            }
        }

        /// <summary>
        /// Naive exhaustive search. Sweeps the along-strike position of the disc centre in
        /// steps of 1/20000 and returns the smallest overshoot (it reaches if this is <= 0).
        /// </summary>
        private static double BruteForceGap(double along, double across)
        {
            const int Steps = 20000;
            double half = FaultBand.MaxOffset * L;
            double best = double.MaxValue;

            for (int i = 0; i <= Steps; i++)
            {
                double u = -half + 2.0 * half * i / Steps;
                double ratio = u / L;
                double w = W * (1.0 - 4.0 * ratio * ratio);
                if (w <= 0.0) continue;

                double da = along - u;
                double dacross = across - 0.5 * w;
                if (dacross < 0.0) dacross = 0.0;

                double gap = da * da + dacross * dacross - w * w;
                if (gap < best) best = gap;
            }

            return best;
        }

        [Fact]
        public void PastTheEndTheCrossSectionShrinks()
        {
            var band = Sample();
            float endW = band.PatchRadiusAt(FaultBand.MaxOffset);
            float lastCentre = FaultBand.MaxOffset * L;

            // Right at the rim of the end disc, almost no slack is left across the fault.
            // (It is a circle intersection, so using it all up along strike drives the
            // across-strike allowance towards 0.)
            Assert.False(band.Contains(
                new Vec2(band.HalfWidthAt(FaultBand.MaxOffset), lastCentre + endW * 0.95f)));
        }

        [Fact]
        public void UnknownGeometryNeverClaimsContainment()
        {
            // When the prefab could not be read (length = width = 0), do not declare the point
            // either "inside" or "outside". Contains is always false, and Known is false too.
            var unknown = new FaultBand(new Vec2(0f, 0f), 0f, 0f, 0f);
            Assert.False(unknown.Known);
            Assert.False(unknown.Contains(new Vec2(0f, 0f)));
            Assert.Equal(0f, unknown.HalfWidthAt(0f), 4);
            Assert.Equal(0f, unknown.PatchRadiusAt(0f), 4);
        }
    }
}
