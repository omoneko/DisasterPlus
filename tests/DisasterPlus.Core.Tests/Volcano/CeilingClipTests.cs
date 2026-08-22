using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// ゲームの高さの天井（1023.98 m）に当たったことを名乗れるか。
    /// **黙って平らな山頂を出さない**のがここの目的である
    /// （所有者の質問 2026-08-22「CS の土地の高さ制限を拡張することは可能なのか」）。
    /// </summary>
    public class CeilingClipTests
    {
        [Fact]
        public void TheCeilingIsTheUshortLimitDividedByTheRawScale()
        {
            // 65535 / 64。TerrainManager.TERRAIN_HEIGHT = 1024 のすぐ下である。
            Assert.Equal(1023.98f, UpliftSchedule.CeilingMetres, 2);
        }

        [Fact]
        public void GroundLevelPlusASmallHillDoesNotHitTheCeiling()
        {
            // 標高 60 m（TERRAIN_LEVEL）に 300 m の山。
            ushort baseRaw = (ushort)(60f * UpliftSchedule.RawUnitsPerMetre);
            Assert.False(UpliftSchedule.CeilingClipped(baseRaw, 300f, 1f));
        }

        [Fact]
        public void HighGroundPlusATallVolcanoDoesHitTheCeiling()
        {
            // 標高 700 m の尾根に 400 m の山を足すと 1100 m で天井を越える。
            ushort baseRaw = (ushort)(700f * UpliftSchedule.RawUnitsPerMetre);
            Assert.True(UpliftSchedule.CeilingClipped(baseRaw, 400f, 1f));

            // 削られた結果は必ず天井そのものになる（巻き戻らない）。
            ushort target = UpliftSchedule.RawTargetAt(baseRaw, 400f, 1f);
            Assert.Equal(UpliftSchedule.MaxRaw, target);
        }

        [Fact]
        public void ThePredicateAgreesWithWhatRawTargetAtActuallyDid()
        {
            // 「削られた」と名乗るのは、実際に削られたときだけ（数を偽らない）。
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
            // 火口は負のプロファイルで彫る。下へ向かう書き込みは天井とは無関係。
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
