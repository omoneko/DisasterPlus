using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// 所有者の依頼（2026-08-22）「タブアイコンの火山と台風をイラストにしてほしいです」。
    ///
    /// ★★ 絵の良し悪しはテストで決められない（<c>tools/IconPreview</c> で目で見る）。
    ///    ここが固定するのは<b>小さくしても読める形の条件</b>である ——
    ///    どちらの絵も、実寸 70 px でこれらが崩れると意味が分からなくなる。
    /// </summary>
    public class DisasterIconArtTests
    {
        /// <summary>実寸に近い解像度で走査する（拡大した絵ではなく、実際に出る絵を見る）。</summary>
        private const int Steps = 70;

        [Fact]
        public void BothIconsActuallyDrawSomething()
        {
            Assert.True(Coverage(true) > 0.15f, "the volcano icon is nearly empty");
            Assert.True(Coverage(false) > 0.40f, "the typhoon icon is nearly empty");
        }

        [Fact]
        public void NeitherIconFillsTheWholeSquare()
        {
            // 端まで塗ると、タイルの背景から浮かずに「四角い板」に見える。
            Assert.True(Coverage(true) < 0.75f, "the volcano icon is a solid block");
            Assert.True(Coverage(false) < 0.85f, "the typhoon icon is a solid block");
        }

        [Fact]
        public void TheVolcanoHasGroundAtTheBottomAndSkyAtTheTopCorners()
        {
            // 山は下に、噴煙は真ん中の上。**四隅の上は空いている**こと。
            Assert.True(DisasterIconArt.Volcano(0.5f, 0.05f).A > 0, "no mountain at the bottom");
            Assert.Equal(0, DisasterIconArt.Volcano(0.04f, 0.96f).A);
            Assert.Equal(0, DisasterIconArt.Volcano(0.96f, 0.96f).A);
        }

        [Fact]
        public void TheVolcanoShowsLava()
        {
            // 溶岩が 1 画素も出ないなら、ただの山である。
            int hot = 0;
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    IconPixel p = DisasterIconArt.Volcano((x + 0.5f) / Steps, (y + 0.5f) / Steps);
                    if (p.A > 0 && p.R > 200 && p.G < 200 && p.B < 120) hot++;
                }
            }

            Assert.True(hot > Steps, hot + " lava pixels is not enough to read as a volcano");
        }

        [Fact]
        public void TheTyphoonIsRoundAndKeepsItsEyeOpen()
        {
            // 角は透明（丸い）。
            Assert.Equal(0, DisasterIconArt.Typhoon(0.02f, 0.02f).A);
            Assert.Equal(0, DisasterIconArt.Typhoon(0.98f, 0.98f).A);

            // 中心は雲ではなく海（眼）。**雲で埋まると台風に見えない。**
            IconPixel eye = DisasterIconArt.Typhoon(0.5f, 0.5f);
            Assert.True(eye.A > 0, "the eye should still be part of the disc");
            Assert.True(eye.B > eye.R, "the eye should be the dark sea, not white cloud");
        }

        [Fact]
        public void TheTyphoonActuallySpirals()
        {
            // 同じ半径を一周して、雲と海が**何度も入れ替わる**こと。
            // 入れ替わらなければ、渦ではなくただの輪である。
            int flips = 0;
            bool wasCloud = false;

            for (int i = 0; i <= 360; i++)
            {
                double a = i * System.Math.PI / 180.0;
                float u = 0.5f + 0.30f * (float)System.Math.Cos(a);
                float v = 0.5f + 0.30f * (float)System.Math.Sin(a);

                IconPixel p = DisasterIconArt.Typhoon(u, v);
                bool cloud = p.A > 0 && p.R > 150;

                if (i > 0 && cloud != wasCloud) flips++;
                wasCloud = cloud;
            }

            Assert.True(flips >= 4, "only " + flips + " cloud/sea transitions; that is not a spiral");
        }

        [Fact]
        public void EveryDrawnPixelIsFullyOpaque()
        {
            // 半透明の画素を作らない。タイルの背景は明るくも暗くもなりうるので、
            // 半透明だと**背景しだいで色が変わる**。
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    float u = (x + 0.5f) / Steps;
                    float v = (y + 0.5f) / Steps;

                    foreach (IconPixel p in new[]
                             {
                                 DisasterIconArt.Volcano(u, v),
                                 DisasterIconArt.Typhoon(u, v),
                             })
                    {
                        Assert.True(p.A == 0 || p.A == 255, "half-transparent pixel at " + u + "," + v);
                    }
                }
            }
        }

        [Fact]
        public void BrokenInputDrawsNothingInsteadOfThrowing()
        {
            Assert.Equal(0, DisasterIconArt.Volcano(float.NaN, 0.5f).A);
            Assert.Equal(0, DisasterIconArt.Typhoon(0.5f, float.PositiveInfinity).A);
        }

        private static float Coverage(bool volcano)
        {
            int drawn = 0;
            for (int y = 0; y < Steps; y++)
            {
                for (int x = 0; x < Steps; x++)
                {
                    float u = (x + 0.5f) / Steps;
                    float v = (y + 0.5f) / Steps;
                    IconPixel p = volcano
                        ? DisasterIconArt.Volcano(u, v)
                        : DisasterIconArt.Typhoon(u, v);
                    if (p.A > 0) drawn++;
                }
            }
            return drawn / (float)(Steps * Steps);
        }
    }
}
