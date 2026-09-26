using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// Can it report that it hit the game's height ceiling (1023.98 m)?
    /// The purpose here is to **never silently produce a flat summit**
    /// (owner's question, 2026-08-22: "is it possible to extend the CS terrain
    /// height limit?").
    /// </summary>
    public class CeilingClipTests
    {
        [Fact]
        public void TheCeilingIsTheUshortLimitDividedByTheRawScale()
        {
            // 65535 / 64. Just below TerrainManager.TERRAIN_HEIGHT = 1024.
            Assert.Equal(1023.98f, UpliftSchedule.CeilingMetres, 2);
        }

        [Fact]
        public void GroundLevelPlusASmallHillDoesNotHitTheCeiling()
        {
            // A 300 m hill on ground at an elevation of 60 m (TERRAIN_LEVEL).
            ushort baseRaw = (ushort)(60f * UpliftSchedule.RawUnitsPerMetre);
            Assert.False(UpliftSchedule.CeilingClipped(baseRaw, 300f, 1f));
        }

        [Fact]
        public void HighGroundPlusATallVolcanoDoesHitTheCeiling()
        {
            // Adding a 400 m cone to a ridge at 700 m gives 1100 m, which is over the
            // ceiling.
            ushort baseRaw = (ushort)(700f * UpliftSchedule.RawUnitsPerMetre);
            Assert.True(UpliftSchedule.CeilingClipped(baseRaw, 400f, 1f));

            // The clipped result is always the ceiling itself (it never wraps around).
            ushort target = UpliftSchedule.RawTargetAt(baseRaw, 400f, 1f);
            Assert.Equal(UpliftSchedule.MaxRaw, target);
        }

        [Fact]
        public void ThePredicateAgreesWithWhatRawTargetAtActuallyDid()
        {
            // It reports "clipped" only when it actually was clipped
            // (it does not lie about the numbers).
            for (int metres = 0; metres <= 1200; metres += 25)
            {
                for (int baseMetres = 0; baseMetres <= 1000; baseMetres += 100)
                {
                    ushort baseRaw = (ushort)System.Math.Min(
                        UpliftSchedule.MaxRaw,
                        (int)(baseMetres * UpliftSchedule.RawUnitsPerMetre));

                    bool says = UpliftSchedule.CeilingClipped(baseRaw, metres, 1f);
                    ushort got = UpliftSchedule.RawTargetAt(baseRaw, metres, 1f);
                    bool actually = got == UpliftSchedule.MaxRaw
                                    && baseMetres + metres > UpliftSchedule.CeilingMetres;

                    Assert.Equal(actually, says);
                }
            }
        }

        [Fact]
        public void DiggingTheCraterNeverCountsAsCeilingClipping()
        {
            // The crater is carved with a negative profile. A downward write has
            // nothing to do with the ceiling.
            ushort baseRaw = (ushort)(900f * UpliftSchedule.RawUnitsPerMetre);
            Assert.False(UpliftSchedule.CeilingClipped(baseRaw, -120f, 1f));
        }

        [Fact]
        public void BrokenInputIsNotReportedAsClipping()
        {
            Assert.False(UpliftSchedule.CeilingClipped(0, float.NaN, 1f));
            Assert.False(UpliftSchedule.CeilingClipped(0, 100f, float.NaN));
        }
    }
}
