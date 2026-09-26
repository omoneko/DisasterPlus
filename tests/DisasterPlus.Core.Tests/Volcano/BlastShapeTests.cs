using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Live report (2026-08-22): "About the volcanic explosion, the effect looks flat
    /// and the location seems slightly off."
    ///
    /// ★★ Both causes have been confirmed in IL:
    ///
    ///   1. <b>Flat</b> —— the 3-argument <c>EffectInfo.SpawnArea(pos, dir, radius)</c>
    ///      writes <c>m_halfHeight = 0</c> (IL_006F-0075). Particles spawn in
    ///      "disc + up×[0, halfHeight)", so it becomes a **disc of zero thickness**.
    ///   2. <b>Off</b> —— the scatter of the bursts was wider than the crater
    ///      (1.15x horizontally, 1.9x vertically).
    /// </summary>
    public class BlastShapeTests
    {
        private const float Crater = 351f;
        private const uint Seed = 991u;

        [Fact]
        public void EveryBurstHasRealVerticalExtent()
        {
            // ★★ **Pass 0 and it goes back to being flat.** If 0 is allowed here, we get
            //    the same picture as the 3-argument SpawnArea all over again.
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);
                Assert.True(b.HalfHeightMetres > 0f, "burst " + i + " is a flat disc");
                Assert.True(b.HalfHeightMetres >= b.RadiusMetres,
                            "burst " + i + " is flatter than it is wide ("
                            + b.HalfHeightMetres + " vs " + b.RadiusMetres + ")");
            }
        }

        [Fact]
        public void TheBurstsStayInsideTheCrater()
        {
            // If they scatter beyond the crater it looks like things "popping off
            // separately around the crater" rather than one big explosion.
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);

                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX
                                                  + b.OffsetZ * b.OffsetZ);
                Assert.True(d <= Crater,
                            "burst " + i + " landed " + d + " m out, past the crater rim at "
                            + Crater);
            }
        }

        [Fact]
        public void TheBurstsStayNearTheVentVertically()
        {
            // The explosion happens at the crater. Do not float it up to the height
            // of the plume.
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);
                Assert.True(b.OffsetY <= Crater,
                            "burst " + i + " floated " + b.OffsetY + " m above the vent");
                Assert.True(b.OffsetY >= 0f);
            }
        }

        [Fact]
        public void TheRingFissureIsStillAllowedToReachOutFar()
        {
            // ★ Only the **central eruption** is kept inside the crater. The ring of
            //   fissures of a caldera-forming eruption has to reach the rim of the
            //   caldera (see the BlastCluster class doc).
            const float ring = 5563f;
            int count = BlastCluster.CountFor(1f, 1f, true);

            float furthest = 0f;
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, true, Crater, ring, Seed);
                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX
                                                  + b.OffsetZ * b.OffsetZ);
                if (d > furthest) furthest = d;
            }

            Assert.True(furthest > ring * 0.8f,
                        "the ring bursts only reached " + furthest + " m of " + ring);
        }
    }
}
