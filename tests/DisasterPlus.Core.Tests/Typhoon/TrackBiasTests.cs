using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TrackBiasTests
    {
        private const float HalfPi = 1.57079633f;
        private const float Pi = 3.14159265f;

        [Fact]
        public void FacingNorthTheRightSideIsEast()
        {
            // クラス doc の検算そのもの。ここを取り違えると偏りが左右反対になる。
            // 北へ進む = heading 90 度（(cos, sin) = (0, 1) = +Z）。
            Assert.Equal(1f, TrackBias.SideOf(HalfPi, 100f, 0f), 4);    // 東 = 真右
            Assert.Equal(-1f, TrackBias.SideOf(HalfPi, -100f, 0f), 4);  // 西 = 真左
            Assert.Equal(0f, TrackBias.SideOf(HalfPi, 0f, 100f), 4);    // 北 = 正面
            Assert.Equal(0f, TrackBias.SideOf(HalfPi, 0f, -100f), 4);   // 南 = 真後ろ
        }

        [Fact]
        public void FacingEastTheRightSideIsSouth()
        {
            // 東へ進む = heading 0 度（(1, 0) = +X）。右は南（-Z）。
            Assert.Equal(1f, TrackBias.SideOf(0f, 0f, -100f), 4);
            Assert.Equal(-1f, TrackBias.SideOf(0f, 0f, 100f), 4);
        }

        [Fact]
        public void TheBiasTurnsWithTheTrack()
        {
            // ★ これが「曲がる経路に付いてくる」ことの試験である。
            //   同じ点でも進行方位が回れば、危険半円のどちらに入るかが変わる。
            const float dx = 100f;
            const float dz = 0f;

            // 北向き: 東の点は真右 = 強化される。
            Assert.True(TrackBias.RadiusFactor(HalfPi, dx, dz, false) > 1f);

            // 半周して南向きになると、同じ点は真左 = 強化されない。
            Assert.Equal(1f, TrackBias.RadiusFactor(-HalfPi, dx, dz, false), 5);
            Assert.Equal(1f, TrackBias.RadiusFactor(HalfPi + Pi, dx, dz, false), 5);

            // 少しずつ回すと連続に落ちていく（段差が無い）。
            float previous = TrackBias.RadiusFactor(HalfPi, dx, dz, false);
            for (int i = 1; i <= 18; i++)
            {
                float heading = HalfPi + Pi * i / 18f;
                float now = TrackBias.RadiusFactor(heading, dx, dz, false);
                Assert.True(now <= previous + 1e-5f);
                previous = now;
            }
        }

        [Fact]
        public void TheLeftSideIsNeverWeakenedAndTheRightIsNeverExtreme()
        {
            // 指示は「右側を若干強化」であって「左側を弱める」ではない。
            for (int i = 0; i < 72; i++)
            {
                float angle = 6.2831853f * i / 72f;
                float dx = (float)System.Math.Cos(angle) * 500f;
                float dz = (float)System.Math.Sin(angle) * 500f;

                float radius = TrackBias.RadiusFactor(0.7f, dx, dz, false);
                float chance = TrackBias.ChanceFactor(0.7f, dx, dz, false);

                Assert.InRange(radius, 1f, 1f + TrackBias.MaxRadiusBoost);
                Assert.InRange(chance, 1f, 1f + TrackBias.MaxChanceBoost);
            }
        }

        [Fact]
        public void TheSouthernHemisphereMirrorsTheDangerousSemicircle()
        {
            const float heading = HalfPi;   // 北向き

            // 北半球は右（東）。
            Assert.True(TrackBias.DangerousSideOf(heading, 100f, 0f, false) > 0.99f);
            Assert.Equal(0f, TrackBias.DangerousSideOf(heading, -100f, 0f, false), 5);

            // 南半球は左（西）。
            Assert.Equal(0f, TrackBias.DangerousSideOf(heading, 100f, 0f, true), 5);
            Assert.True(TrackBias.DangerousSideOf(heading, -100f, 0f, true) > 0.99f);
        }

        [Fact]
        public void TheEyeItselfAndBrokenInputsGetNoBias()
        {
            // 中心そのもの（長さ 0）で 0 除算しない。
            Assert.Equal(0f, TrackBias.SideOf(1f, 0f, 0f), 6);
            Assert.Equal(1f, TrackBias.RadiusFactor(1f, 0f, 0f, false), 6);

            Assert.Equal(0f, TrackBias.SideOf(float.NaN, 100f, 0f), 6);
            Assert.Equal(0f, TrackBias.SideOf(1f, float.NaN, 0f), 6);
            Assert.Equal(0f, TrackBias.SideOf(1f, 0f, float.NaN), 6);
            Assert.Equal(1f, TrackBias.ChanceFactor(float.NaN, 100f, 100f, false), 6);
        }

        [Fact]
        public void TheBoostIsModest()
        {
            // 「気付く程度であって別の台風ではない」ことを数字で固定する。
            Assert.True(TrackBias.MaxRadiusBoost > 0.05f);
            Assert.True(TrackBias.MaxRadiusBoost <= 0.25f);
            Assert.True(TrackBias.MaxChanceBoost > 0.10f);
            Assert.True(TrackBias.MaxChanceBoost <= 0.50f);
        }
    }
}
