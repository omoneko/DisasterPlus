using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class VortexCloudProfileTests
    {
        [Fact]
        public void TheProfileAndTheLayoutAgreeOnParticleSize()
        {
            // ★★ ここがずれると**眼が埋まる。** VortexPuffLayout は
            //   SizeFractionOf で眼の余白を計算し、Game 側は VortexCloudProfile の
            //   SizeFraction で startSize を決める。**同じ数でなければならない。**
            for (int i = 0; i < 3; i++)
            {
                var layer = (VortexCloudLayer)i;
                Assert.Equal(VortexPuffLayout.SizeFractionOf(layer),
                             VortexCloudProfile.Of(layer).SizeFraction, 6);
            }
        }

        [Fact]
        public void TheCloudIsBrightOnTopAndDarkUnderneath()
        {
            // 入道雲そのもの。ここが逆だと、どんな形に置いても煙の塊に見える
            // （旧実装は 1 つの複製に均一な灰色を入れていた）。
            float deck = Luma(VortexCloudProfile.Deck);
            float tower = Luma(VortexCloudProfile.Tower);
            float canopy = Luma(VortexCloudProfile.Canopy);

            Assert.True(deck < tower, "the cloud base must be darker than the towers");
            Assert.True(tower < canopy, "the canopy must be the brightest layer");
            // 差が小さいと「明るさの違う灰色」にしかならない。
            Assert.True(canopy - deck > 0.35f);
        }

        [Fact]
        public void NoLayerEmitsZeroParticles()
        {
            // ★★ rateOverTime を 0 にすると 1 粒も出ない（emission.enabled が false でも
            //   EmitParticles は乗数として読み続ける）。いちばん踏みやすい罠である。
            for (int i = 0; i < 3; i++)
            {
                VortexCloudProfile p = VortexCloudProfile.Of((VortexCloudLayer)i);
                Assert.True(p.RateOverTime > 0f);
                Assert.True(p.MaxParticles > 0);
                Assert.True(p.LifeMinSeconds > 0f);
                Assert.True(p.LifeMaxSeconds >= p.LifeMinSeconds);
                Assert.InRange(p.Alpha, 0.05f, 1f);
            }
        }

        [Fact]
        public void TheCanopySpreadsSidewaysAndTheTowersRise()
        {
            // かなとこは中立浮力高度で横へ広がる（放出角がほぼ水平）。
            Assert.True(VortexCloudProfile.Canopy.SpawnAngleMinDegrees >= 60f);
            // 塔は上へ立ち上がる（放出角が狭い）。
            Assert.True(VortexCloudProfile.Tower.SpawnAngleMaxDegrees <= 45f);
            // 塔だけがわずかに浮く（負の重力）。雲底とかなとこは滞留する。
            Assert.True(VortexCloudProfile.Tower.GravityModifier < 0f);
            // かなとこはいちばん長生き（滞留して平たい天蓋になる）。
            Assert.True(VortexCloudProfile.Canopy.LifeMaxSeconds
                        > VortexCloudProfile.Tower.LifeMaxSeconds);
        }

        private static float Luma(VortexCloudProfile p)
        {
            float bright = 0.299f * p.BrightRed + 0.587f * p.BrightGreen + 0.114f * p.BrightBlue;
            float dark = 0.299f * p.DarkRed + 0.587f * p.DarkGreen + 0.114f * p.DarkBlue;
            return (bright + dark) * 0.5f;
        }
    }
}
