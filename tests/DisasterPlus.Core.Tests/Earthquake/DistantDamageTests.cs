using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <b>Distant damage from a trench-type earthquake.</b> (2026-09-02, from the owner:
    /// "make fires and collapses happen with a certain probability even far from the
    /// epicentre, scaled to the size of the quake")
    ///
    /// ★ What is pinned here are the <b>two phrases of the request</b> ——
    ///   "with a certain probability even far away" (= a floor remains) and
    ///   "scaled to the size" (= it varies greatly with intensity).
    /// </summary>
    public class DistantDamageTests
    {
        private const float Full = DistantDamage.MaxStrength;

        [Fact]
        public void Far_from_the_epicentre_it_still_happens()
        {
            // ★★ **This is the substance of the request.** The vanilla disc (1 - d/R)
            //    drops to nearly 0 far away. Ours keeps a floor.
            float reach = DistantDamage.ReachMetres(200);

            float near = DistantDamage.Falloff(0f, 200);
            float half = DistantDamage.Falloff(reach * 0.5f, 200);

            Assert.Equal(1f, near, 3);

            // At half the distance, more than 60% of the near value must remain
            // (without the floor it would be exactly 0.5).
            Assert.True(half > near * 0.6f, "falloff at half reach was " + half);
        }

        [Fact]
        public void The_reach_covers_the_whole_map_for_a_big_quake()
        {
            // Even across all 25 tiles, half the diagonal is about 12,200 m. The epicentre
            // is offshore, so if the reach does not get here
            // **the city takes no damage at all**.
            Assert.True(DistantDamage.ReachMetres(255) > 12200f,
                "reach at full intensity was " + DistantDamage.ReachMetres(255));

            // Even at the default slider (55) it covers 9 tiles (half-diagonal about 7,330 m).
            Assert.True(DistantDamage.ReachMetres(55) > 7330f,
                "reach at intensity 55 was " + DistantDamage.ReachMetres(55));
        }

        [Fact]
        public void It_stops_at_the_edge_without_a_step()
        {
            // ★ If there is a step at the edge, you can see the damage cut off abruptly
            //   on the map.
            float reach = DistantDamage.ReachMetres(150);

            Assert.Equal(0f, DistantDamage.Falloff(reach, 150));
            Assert.Equal(0f, DistantDamage.Falloff(reach * 1.5f, 150));

            // Just inside the edge it is already small (the taper is working).
            float justInside = DistantDamage.Falloff(reach * 0.98f, 150);
            Assert.True(justInside < 0.1f, "just inside the edge was " + justInside);
        }

        [Fact]
        public void It_never_goes_up_with_distance()
        {
            float reach = DistantDamage.ReachMetres(120);
            float previous = float.MaxValue;

            for (float d = 0f; d <= reach * 1.1f; d += reach / 200f)
            {
                float f = DistantDamage.Falloff(d, 120);
                Assert.True(f <= previous + 1e-5f,
                    "falloff rose at " + d + ": " + previous + " -> " + f);
                previous = f;
            }
        }

        [Fact]
        public void A_big_quake_is_far_worse_than_a_moderate_one()
        {
            // ★★ "Scaled to the size of the quake". It is squared, so 5x the intensity
            //    gives close to 25x the damage.
            float small = DistantDamage.CollapseChance(0f, 51, Full);
            float big = DistantDamage.CollapseChance(0f, 255, Full);

            Assert.True(big > small * 20f,
                "255 gave " + big + " but 51 gave " + small);
        }

        [Fact]
        public void Fire_is_more_likely_than_collapse()
        {
            // For a trench-type quake it is mainly fire that burns the city (class doc).
            Assert.True(DistantDamage.FireChance(1000f, 200, Full)
                        > DistantDamage.CollapseChance(1000f, 200, Full));
        }

        [Fact]
        public void The_slider_scales_it_and_zero_turns_it_off()
        {
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, 0f));
            Assert.Equal(0f, DistantDamage.FireChance(0f, 255, 0f));

            float half = DistantDamage.CollapseChance(0f, 255, Full / 2f);
            float full = DistantDamage.CollapseChance(0f, 255, Full);
            Assert.Equal(full / 2f, half, 4);

            // Beyond the top of the scale the multiplier caps at 1 (and negatives stop at 0).
            Assert.Equal(full, DistantDamage.CollapseChance(0f, 255, Full * 3f), 4);
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, -5f));
        }

        [Fact]
        public void It_never_exceeds_its_own_ceiling()
        {
            for (int i = 0; i <= 255; i++)
            {
                float c = DistantDamage.CollapseChance(0f, (byte)i, Full);
                float f = DistantDamage.FireChance(0f, (byte)i, Full);

                Assert.InRange(c, 0f, DistantDamage.MaxCollapseChance);
                Assert.InRange(f, 0f, DistantDamage.MaxFireChance);
            }
        }

        [Fact]
        public void Broken_input_damages_nothing()
        {
            // ★ Every condition that returns 0 must fall on the "do not damage" side.
            Assert.Equal(0f, DistantDamage.CollapseChance(float.NaN, 255, Full));
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, float.NaN));
            Assert.Equal(0f, DistantDamage.CollapseChance(-100f, 255, Full));
            Assert.Equal(0f, DistantDamage.FireChance(float.NaN, 255, Full));

            // Intensity 0 means "the disaster could not be read". Do nothing.
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 0, Full));
        }

        [Fact]
        public void It_beats_the_vanilla_disc_where_vanilla_gives_up()
        {
            // ★★ This is the quantitative substance of the request. Intensity 100, a city
            //    3 km offshore: vanilla only hands out 0.02 x (1 - 3000/4000) = 0.5%.
            const byte intensity = 100;
            const float distance = 3000f;

            float vanilla = 0.02f * SeismicIntensity.At(distance, intensity);

            // At the default strength (6), collapse and fire together must beat vanilla.
            float ours = DistantDamage.CollapseChance(distance, intensity, 6f)
                         + DistantDamage.FireChance(distance, intensity, 6f);

            Assert.True(ours > vanilla,
                "ours " + ours.ToString("F5") + " vs vanilla " + vanilla.ToString("F5"));
        }
    }
}
