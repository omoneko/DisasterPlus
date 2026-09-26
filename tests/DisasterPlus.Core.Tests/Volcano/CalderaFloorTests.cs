using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// In-game report (2026-08-22): "it is odd that the inside of the caldera becomes flat
    /// ground (please make it more realistic by allowing for the original terrain and the
    /// remains of the volcanic edifice)".
    ///
    /// ★★ <b>"Not being perfectly flat" can be tested.</b>
    ///    A roof that falls in does not land as a single slab; it breaks into a heap of blocks.
    /// </summary>
    public class CalderaFloorTests
    {
        private const float ConeRadius = 2928f;
        private const float ConeHeight = 1000f;
        private const uint Seed = 20260822u;

        private static float R { get { return SuperEruption.CalderaRadiusMetres(ConeRadius); } }
        private static float D { get { return SuperEruption.CalderaDepthMetres(ConeHeight); } }

        private static float F(float dx, float dz)
        {
            return SuperEruption.CalderaFloorOffsetAt(dx, dz, R, D, Seed);
        }

        [Fact]
        public void TheFloorIsNotFlat()
        {
            // ★★ This is the owner's report itself. The floor height must not collapse to
            //    a single value.
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 60; i++)
            {
                float d = R * SuperEruption.FloorFraction * i / 60f;
                seen.Add((int)(F(d, 0f) / 5f));   // in 5 m steps
            }

            Assert.True(seen.Count > 12,
                        "the caldera floor only has " + seen.Count + " distinct heights");
        }

        [Fact]
        public void TheFloorHasARaisedCentre()
        {
            // Real large calderas have a central cone. Without one it looks like "a plain bowl".
            float centre = F(0f, 0f);
            float outer = F(R * 0.6f, 0f);

            Assert.True(centre > outer,
                        "the centre (" + centre + ") is not raised over the floor ("
                        + outer + ")");
        }

        [Fact]
        public void NothingInTheCalderaEverRisesAboveTheOriginalGround()
        {
            // Neither the roughness nor the central cone ever rises above the original ground
            // —— if it did, an island at the original height would be left inside a caldera
            // that is supposed to have collapsed.
            for (int i = 0; i <= 240; i++)
            {
                float d = R * 1.3f * i / 240f;
                Assert.True(F(d, 0f) <= 0f, "the caldera floor rose above ground at " + d);
                Assert.True(F(0f, d) <= 0f, "the caldera floor rose above ground at z=" + d);
            }
        }

        [Fact]
        public void TheBumpinessDiesOutAtTheRim()
        {
            // No stray blocks may be left dotted about outside the rim.
            Assert.Equal(0f, F(R, 0f), 3);
            Assert.Equal(0f, F(R * 1.5f, 0f), 3);
            Assert.Equal(0f, F(R * 4f, 0f), 3);
        }

        [Fact]
        public void TheFloorNeverGoesUnreasonablyDeeperThanTheCaldera()
        {
            float worst = 0f;
            for (int i = 0; i <= 300; i++)
            {
                float d = R * i / 300f;
                float v = F(d, 0f);
                if (v < worst) worst = v;
            }

            // It may go past by the amplitude of the roughness, but no further.
            Assert.True(worst >= -D * (1f + SuperEruption.FloorRoughFraction) - 1f,
                        "the floor reached " + worst + " for a " + D + " m caldera");
        }

        [Fact]
        public void TheSameVolcanoAlwaysGetsTheSameFloor()
        {
            for (int i = 0; i < 40; i++)
            {
                float d = R * i / 40f;
                Assert.Equal(F(d, 0f), F(d, 0f), 5);
            }
        }

        [Fact]
        public void ADifferentVolcanoGetsADifferentFloor()
        {
            bool different = false;
            for (int i = 0; i < 60; i++)
            {
                float d = R * i / 60f;
                if (SuperEruption.CalderaFloorOffsetAt(d, 0f, R, D, Seed)
                    != SuperEruption.CalderaFloorOffsetAt(d, 0f, R, D, Seed + 1u))
                {
                    different = true;
                    break;
                }
            }

            Assert.True(different, "every volcano gets the same caldera floor");
        }

        [Fact]
        public void BrokenInputMovesNoGround()
        {
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(float.NaN, 0f, R, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, float.NaN, R, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, 0f, 0f, D, Seed), 4);
            Assert.Equal(0f, SuperEruption.CalderaFloorOffsetAt(0f, 0f, R, 0f, Seed), 4);
        }

        [Fact]
        public void ACalderaOnHighGroundStaysAboveSeaLevel()
        {
            // ★★ The check behind the answer to the owner's question, "why is the elevation
            //    inside the caldera always below sea level?". **It is no longer "always".**
            //    The game's sea level is 40 m (WaterSimulation.DEFAULT_SEA_LEVEL, measured
            //    from the IL).
            const float seaLevel = 40f;

            float highGround = 600f;
            Assert.True(highGround - D > seaLevel,
                        "even on 600 m ground the caldera floor (" + (highGround - D)
                        + " m) is below sea level");

            // On ground close to the sea it may flood (Santorini, Krakatoa).
            float lowGround = 90f;
            Assert.True(lowGround - D < seaLevel);
        }
    }
}
