using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <c>SplashWater</c> は<b>足し算</b>である（IL_099A–09C3、IMPACT 波）。
    /// **前線を遅くした瞬間に水位が跳ね上がる**という罠をここで塞ぐ。
    /// </summary>
    public class TsunamiSplashTests
    {
        [Fact]
        public void TheTotalDepthAddedToOneCellDoesNotDependOnHowOftenWeFire()
        {
            // ★★ これが要である。1 セルが浴びる発の数は
            //    (直径 / 1 発あたりの前進) で決まる。深さがその逆数で減れば、
            //    積み上がる総量は前線の速さによらない。
            const float rise = 20f;
            float diameter = TsunamiSplash.RadiusMetres * 2f;

            float slow = TsunamiSplash.DepthFor(rise, 40f) * (diameter / 40f);
            float fast = TsunamiSplash.DepthFor(rise, 200f) * (diameter / 200f);

            Assert.Equal(slow, fast, 3);
            Assert.Equal(rise * TsunamiSplash.DepthFraction, slow, 3);
        }

        [Fact]
        public void AFrontThatOutrunsItsOwnSplashIsNotScaledDown()
        {
            // 1 発で直径ぶん以上進むなら重なりが無いので、割ってはいけない。
            const float rise = 20f;
            float full = rise * TsunamiSplash.DepthFraction;

            Assert.Equal(full, TsunamiSplash.DepthFor(rise, TsunamiSplash.RadiusMetres * 2f), 3);
            Assert.Equal(full, TsunamiSplash.DepthFor(rise, 99999f), 3);
        }

        [Fact]
        public void TheDepthGrowsWithTheWave()
        {
            Assert.True(TsunamiSplash.DepthFor(20f, 50f) > TsunamiSplash.DepthFor(6f, 50f));
        }

        [Fact]
        public void NothingIsSplashedForBrokenInput()
        {
            Assert.Equal(0f, TsunamiSplash.DepthFor(float.NaN, 50f), 4);
            Assert.Equal(0f, TsunamiSplash.DepthFor(20f, float.NaN), 4);
            Assert.Equal(0f, TsunamiSplash.DepthFor(0f, 50f), 4);
            Assert.Equal(0f, TsunamiSplash.DepthFor(20f, 0f), 4);
            Assert.Equal(0f, TsunamiSplash.DepthFor(20f, -50f), 4);
        }

        [Fact]
        public void TheRingIsContinuousUntilTheCapBites()
        {
            // 隣どうしが重なる数を出していること（切れ目のない輪）。
            for (float front = 900f; front <= 6000f; front += 300f)
            {
                int count = TsunamiSplash.CountFor(front);
                if (count >= TsunamiSplash.MaxPerPulse) continue;

                float chord = 6.2831853f * front / count;
                Assert.True(chord < TsunamiSplash.RadiusMetres * 2f,
                            "the splashes are " + chord + " m apart at " + front
                            + " m but only " + (TsunamiSplash.RadiusMetres * 2f)
                            + " m wide — the ring would be a dotted line");
            }
        }

        [Fact]
        public void TheCountIsBounded()
        {
            Assert.Equal(TsunamiSplash.MinPerPulse, TsunamiSplash.CountFor(1f));
            Assert.Equal(TsunamiSplash.MinPerPulse, TsunamiSplash.CountFor(float.NaN));
            Assert.Equal(TsunamiSplash.MaxPerPulse, TsunamiSplash.CountFor(99999f));
            Assert.True(TsunamiSplash.MaxPerPulse > TsunamiSplash.MinPerPulse);
        }

        [Fact]
        public void TheCapStillCoversTheFarthestRing()
        {
            // ★ 届く限界（8,200 m）で上限に当たっても、輪が点線にならないこと。
            float front = TsunamiWaveTrain.ReachEdgeMetres;
            int count = TsunamiSplash.CountFor(front);
            float chord = 6.2831853f * front / count;

            Assert.True(chord <= TsunamiSplash.RadiusMetres * 2f,
                        "at the far edge the splashes are " + chord
                        + " m apart, wider than the " + (TsunamiSplash.RadiusMetres * 2f)
                        + " m each one covers");
        }
    }
}
