using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <see cref="SeaCell"/> —— "is that cell open sea?".
    ///
    /// ★★ **This formula was rewritten three times, and each time it mistook a different
    ///   landform for another.** Every single case listed here is a hole we actually fell
    ///   into (see the <see cref="SeaCell"/> class doc). Do not cut any of them.
    /// </summary>
    public class SeaCellTests
    {
        // Sea level 40 m = the vanilla default. Minimum depth 24 m.
        private const int Sea = 40 * 64;
        private const int MinDepth = 24 * 64;

        private static bool Is(float terrainMetres, float columnMetres)
        {
            return SeaCell.IsOpenSea((int)(terrainMetres * 64f), (int)(columnMetres * 64f),
                                     Sea, MinDepth);
        }

        [Fact]
        public void Real_open_sea_is_sea()
        {
            // Seabed at 5 m, water column 35 m.
            Assert.True(Is(5f, 35f));
        }

        [Fact]
        public void Water_too_shallow_is_not_sea()
        {
            // Seabed at 25 m, water column 15 m. Not deep enough.
            Assert.False(Is(25f, 15f));
        }

        [Fact]
        public void A_dry_basin_below_sea_level_is_not_sea()
        {
            // ★★ Reclaimed land ringed by dykes. **40 m below sea level, but dry.**
            //    The version that only looked at the seabed height answered "sea" here
            //    and tried to put a water source with a 3.8 km radius on dry land.
            Assert.False(Is(0f, 0f));

            // The same holds when a little rainwater has pooled at the bottom
            // (under 2 m does not count as "water is present").
            Assert.False(Is(0f, 1f));
            Assert.False(Is(0f, 1.9f));

            // ★ With 2 m pooled we do accept "water is present". **This is where the line
            //   is drawn.** Lower it and the rainwater in a hollow reads as sea; raise it
            //   and open sea reads as not-sea during the drawback (see the PresenceUnits doc).
            Assert.True(Is(0f, 2f));
        }

        [Fact]
        public void A_trough_does_not_turn_the_sea_into_land()
        {
            // ★★ **The mirror image of the storm surge.** (2026-08-31, 7th verification run)
            //    The version that demanded the "minimum depth" of the water column itself
            //    answered <b>not-sea for the whole open ocean</b> while the water level was
            //    drawn down, and told the player "no deep sea within 2,304 m" —— when it was
            //    plainly sea. Depth is decided by the seabed, not by the water column.
            Assert.True(Is(5f, 35f));   // normal (35 m deep)
            Assert.True(Is(5f, 20f));   // drawn down by 15 m
            Assert.True(Is(5f, 5f));    // drawn down by 30 m. Still sea
            Assert.True(Is(5f, 2f));    // drawn down by 33 m. Only just sea

            // ★ Once it has dried out completely it is, of course, no longer sea.
            Assert.False(Is(5f, 0f));
        }

        [Fact]
        public void A_high_river_is_not_sea()
        {
            // ★★ A 30 m deep river running through a valley at an altitude of 60 m.
            //    The version that only looked at the water column answered "sea" here.
            Assert.False(Is(60f, 30f));
        }

        [Fact]
        public void A_lake_above_sea_level_is_not_sea()
        {
            // Lake surface at 70 m, lake bed at 41 m (1 m above sea level).
            Assert.False(Is(41f, 29f));
        }

        [Fact]
        public void Flooded_land_is_not_sea()
        {
            // ★★ A town flooded by an earlier tsunami. The ground is at an altitude of 20 m
            //    (not above sea level... rather, 20 m against a sea level of 40 m, so it is
            //    below sea level). **This is the crux**: if the land altitude is higher than
            //    "sea level - minimum depth", it is not sea however much water covers it.
            Assert.False(Is(20f, 30f));   // 20 > 40 - 24 = 16, so it is land
            Assert.True(Is(16f, 30f));    // exactly on the boundary it is sea
        }

        [Fact]
        public void A_storm_surge_does_not_turn_the_sea_into_land()
        {
            // ★★ **This is the reason for the third rewrite.**
            //    The version that rejected rivers by the water surface height answered
            //    not-sea for the whole open ocean during a storm surge, and told the player
            //    "no deep sea within 2,304 m".
            //    The seabed does not move, so the verdict does not change when the water
            //    column grows.
            Assert.True(Is(5f, 35f));    // normal
            Assert.True(Is(5f, 55f));    // mid-surge, sea level up by 20 m
            Assert.True(Is(5f, 135f));   // the crest of the tsunami
        }

        [Fact]
        public void The_boundary_is_inclusive_on_the_seabed()
        {
            // If the seabed is exactly at "sea level - minimum depth", it is sea.
            Assert.True(Is(16f, 24f));

            // One unit above that is land. **This is the only place depth is decided.**
            Assert.False(SeaCell.IsOpenSea(16 * 64 + 1, 24 * 64, Sea, MinDepth));
        }

        [Fact]
        public void The_column_only_has_to_be_present()
        {
            // ★ We do not demand the minimum depth of the water column (see the
            //   PresenceUnits doc).
            Assert.True(SeaCell.IsOpenSea(5 * 64, SeaCell.PresenceUnits, Sea, MinDepth));
            Assert.False(SeaCell.IsOpenSea(5 * 64, SeaCell.PresenceUnits - 1, Sea, MinDepth));
        }

        [Fact]
        public void A_deep_map_works_the_same_way()
        {
            // A map with sea level at 207 m (the player raised the sea level).
            int sea = 207 * 64;
            int min = 24 * 64;

            Assert.True(SeaCell.IsOpenSea(30 * 64, 177 * 64, sea, min));   // 177 m deep
            Assert.True(SeaCell.IsOpenSea(30 * 64, 10 * 64, sea, min));    // mid-drawback
            Assert.False(SeaCell.IsOpenSea(30 * 64, 0, sea, min));         // a dry hollow
            Assert.False(SeaCell.IsOpenSea(220 * 64, 40 * 64, sea, min));  // a high lake
        }

        [Fact]
        public void Asking_for_no_depth_still_needs_water()
        {
            // ★ Even when passed a minimum depth of 0, **without water it is not sea**.
            //   Rejecting a dry hollow is the water column's job.
            Assert.False(SeaCell.IsOpenSea(0, 0, Sea, 0));
            Assert.True(SeaCell.IsOpenSea(0, SeaCell.PresenceUnits, Sea, 0));
        }
    }
}
