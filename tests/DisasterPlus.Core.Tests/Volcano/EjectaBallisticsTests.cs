using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The ballistics of volcanic blocks. **What is pinned down are the three properties
    /// "it comes down", "bigger goes further" and "it follows the size of the mountain"**,
    /// plus never letting a NaN escape on bad input. The look itself is drawn by
    /// tools/VolcanoPreview and checked by eye.
    /// </summary>
    public class EjectaBallisticsTests
    {
        private const uint Seed = 0x1A2B3C4Du;
        private const float R = 1200f;
        private const float H = 600f;
        private const float Vent = 540f;   // the crater floor (roughly this height for the
                                           // stratovolcano default)

        [Fact]
        public void EveryBlockLandsInFiniteTime()
        {
            // ★ The guarantee that the phase machine never stalls. If even one rock keeps
            //   flying, its slot stays occupied until the eruption ends.
            for (int blast = 0; blast < 24; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f,
                                                          VolcanoForm.Strato, R, H, Vent);
                    Assert.True(b.Valid, "block " + blast + "/" + i + " has no trajectory");
                    // ★ We also check that it has **not** hit the cap (MaxFlightSeconds).
                    //   If it had, that rock would simply vanish while frozen in mid-air.
                    Assert.True(b.FlightSeconds > 0f
                                && b.FlightSeconds < 40f,
                        "flight " + b.FlightSeconds + " s is not finite and sane");
                    Assert.False(float.IsNaN(b.RangeMetres));
                    Assert.True(b.RangeMetres > 0f);
                }
            }
        }

        [Fact]
        public void BiggerBlocksTravelFurther()
        {
            // The owner's request itself ("larger ones travelling further").
            // Within the same blast, compare the largest and the smallest blocks.
            float smallSum = 0f, bigSum = 0f;
            int smallCount = 0, bigCount = 0;

            for (int blast = 0; blast < 24; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f,
                                                          VolcanoForm.Strato, R, H, Vent);
                    if (!b.Valid) continue;

                    if (b.SizeUnit < 0.25f) { smallSum += b.RangeMetres; smallCount++; }
                    else if (b.SizeUnit > 0.75f) { bigSum += b.RangeMetres; bigCount++; }
                }
            }

            Assert.True(smallCount > 0 && bigCount > 0, "the sample has no small or big blocks");
            Assert.True(bigSum / bigCount > smallSum / smallCount * 1.5f,
                "big blocks do not travel further: " + (bigSum / bigCount)
                + " m vs " + (smallSum / smallCount) + " m");
        }

        [Fact]
        public void TheSpreadFollowsTheSizeOfTheMountain()
        {
            // ★ Feature no. 5 lets the player choose the size. If a rock flew 2 km from a
            //   lava dome with a radius of 350 m, that would look like a bug, not physics.
            float small = LongestRange(VolcanoForm.Dome, 350f, 300f, 270f);
            float large = LongestRange(VolcanoForm.Strato, R, H, Vent);

            Assert.True(large > small * 2f,
                "the spread does not scale with the cone: " + large + " vs " + small);
            // ★ The real range is longer than the range assuming it lands on flat ground ——
            //   because the vent sits on the crater floor (270 m here) and the block falls
            //   to the lower slopes below it. Even with that extra reach it never goes
            //   beyond two mountains' worth.
            Assert.True(small < 350f * 2.2f,
                "the small cone throws blocks far beyond its own footprint: " + small);
        }

        [Fact]
        public void TheBlockIsAboveTheGroundWhileItFliesAndAtTheGroundWhenItLands()
        {
            EjectaBlock b = EjectaBallistics.Plan(Seed, 3, 2, 1f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.True(b.Valid);

            // In flight it is always above the ground.
            for (int i = 1; i < 10; i++)
            {
                float t = b.FlightSeconds * i / 10f;
                float dx, dy, dz;
                EjectaBallistics.OffsetAt(b, t, out dx, out dy, out dz);

                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                float ground = EjectaBallistics.GroundAt(VolcanoForm.Strato, d, R, H);
                Assert.True(Vent + dy > ground - 1f,
                    "the block is underground at t=" + t + " (" + (Vent + dy) + " vs " + ground + ")");
            }

            // On impact it coincides with the ground (only the 0.15 ms step of the bisection
            // as error).
            float lx, ly, lz;
            EjectaBallistics.OffsetAt(b, b.FlightSeconds, out lx, out ly, out lz);
            float landDistance = (float)Math.Sqrt(lx * lx + lz * lz);
            float landGround = EjectaBallistics.GroundAt(VolcanoForm.Strato, landDistance, R, H);
            Assert.True(Math.Abs(Vent + ly - landGround) < 2f,
                "the impact is not on the ground: " + (Vent + ly) + " vs " + landGround);
        }

        [Fact]
        public void TheSameBlastAlwaysThrowsTheSameBlocks()
        {
            // The guarantee that the frame number is not mixed into the seed. Mix it in and
            // the destination changes mid-flight.
            EjectaBlock a = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            EjectaBlock b = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.Equal(a.DirX, b.DirX);
            Assert.Equal(a.DirZ, b.DirZ);
            Assert.Equal(a.FlightSeconds, b.FlightSeconds);
            Assert.Equal(a.RangeMetres, b.RangeMetres);
        }

        [Fact]
        public void DifferentBlastsThrowDifferentBlocks()
        {
            EjectaBlock a = EjectaBallistics.Plan(Seed, 5, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            EjectaBlock b = EjectaBallistics.Plan(Seed, 6, 1, 0.7f,
                                                  VolcanoForm.Strato, R, H, Vent);
            Assert.NotEqual(a.DirX, b.DirX);
        }

        [Fact]
        public void BadInputIsRefusedAndNeverNaN()
        {
            // Both the .cgs and the terrain can be edited by hand. **Do not substitute 0;
            // return "unusable".**
            foreach (float bad in new float[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.False(EjectaBallistics.Plan(Seed, 0, 0, 1f,
                             VolcanoForm.Strato, bad, H, Vent).Valid);
                Assert.False(EjectaBallistics.Plan(Seed, 0, 0, 1f,
                             VolcanoForm.Strato, R, bad, Vent).Valid);
            }

            Assert.False(EjectaBallistics.Plan(Seed, -1, 0, 1f,
                         VolcanoForm.Strato, R, H, Vent).Valid);

            // The trajectory still holds when the vent height is broken (it falls back to 0).
            EjectaBlock ok = EjectaBallistics.Plan(Seed, 0, 0, 1f,
                                                   VolcanoForm.Strato, R, H, float.NaN);
            Assert.True(ok.Valid);
            Assert.False(float.IsNaN(ok.FlightSeconds));

            // Asking an invalid slot for a position just returns 0 (no NaN escapes).
            float dx, dy, dz;
            EjectaBallistics.OffsetAt(default(EjectaBlock), 1f, out dx, out dy, out dz);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            Assert.Equal(0f, dz);

            EjectaBallistics.OffsetAt(ok, float.NaN, out dx, out dy, out dz);
            Assert.Equal(0f, dx);
            Assert.Equal(0f, dy);
            Assert.Equal(0f, dz);
        }

        [Fact]
        public void WeakEruptionsThrowFewerAndShorterBlocks()
        {
            Assert.True(EjectaBallistics.BlocksPerBlast(0f)
                        < EjectaBallistics.BlocksPerBlast(1f));
            Assert.Equal(EjectaBallistics.MinBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(0f));
            Assert.Equal(EjectaBallistics.MaxBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(1f));

            // NaN is treated the same as 0 (the quietest side).
            Assert.Equal(EjectaBallistics.MinBlocksPerBlast,
                         EjectaBallistics.BlocksPerBlast(float.NaN));

            float weak = LongestRangeAt(0.15f);
            float strong = LongestRangeAt(1f);
            Assert.True(weak < strong, "a weak eruption throws as far as a strong one");
        }

        private static float LongestRange(VolcanoForm form, float r, float h, float vent)
        {
            float longest = 0f;
            for (int blast = 0; blast < 16; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, 1f, form, r, h, vent);
                    if (b.Valid && b.RangeMetres > longest) longest = b.RangeMetres;
                }
            }
            return longest;
        }

        private static float LongestRangeAt(float unit)
        {
            float longest = 0f;
            for (int blast = 0; blast < 16; blast++)
            {
                for (int i = 0; i < EjectaBallistics.MaxBlocksPerBlast; i++)
                {
                    EjectaBlock b = EjectaBallistics.Plan(Seed, blast, i, unit,
                                                          VolcanoForm.Strato, R, H, Vent);
                    if (b.Valid && b.RangeMetres > longest) longest = b.RangeMetres;
                }
            }
            return longest;
        }
    }
}
