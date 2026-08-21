using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 溶岩の発光（実機の指摘④「点滅している」「噴火が終わっても光り続けている」）。
    ///
    /// ここで固定するのは 4 つ:
    ///   1. **時間の引数がどこにも無い**（点滅しようがない）
    ///   2. いちばん明るいのは<b>前進端</b>、次が<b>火口</b>、あいだは地殻
    ///   3. 地殻にも割れ目があり、明るさは<b>場所で</b>変わる
    ///   4. 冷え切ったら**ちょうど 0**になり、描画側は畳む
    /// </summary>
    public class LavaGlowTests
    {
        [Fact]
        public void TheFrontIsBrightestAndTheVentIsNext()
        {
            float front = LavaGlow.AlongFlowUnit(1f);
            float vent = LavaGlow.AlongFlowUnit(0f);
            float middle = LavaGlow.AlongFlowUnit(0.5f);

            Assert.Equal(1f, front, 3);
            Assert.True(vent > 0.5f, "the vent should stay hot: it is fed continuously");
            Assert.True(vent < front, "the front should be the brightest");
            Assert.Equal(0f, middle, 3);
        }

        [Fact]
        public void ThePlaceCoolsAsTheFlowRunsPastIt()
        {
            // 溶岩が前へ進むと、既に置かれた場所の v は小さいほうへずれる。
            // ★ これが「年齢とともに冷える」の実装そのものである。
            float atFront = LavaGlow.AlongFlowUnit(1.00f);
            float justBehind = LavaGlow.AlongFlowUnit(0.95f);
            float wellBehind = LavaGlow.AlongFlowUnit(0.85f);
            float crust = LavaGlow.AlongFlowUnit(0.60f);

            Assert.True(atFront > justBehind);
            Assert.True(justBehind > wellBehind);
            Assert.True(wellBehind >= crust);
            Assert.Equal(0f, crust, 3);
        }

        [Fact]
        public void TheCrustGlowsOnlyInItsCracks()
        {
            // 地殻の帯（v = 0.4〜0.6）を横切って、明るいところと暗いところが
            // **どちらも在る**こと。全部同じ明るさなら「塗り」である。
            float min = 1f, max = 0f;
            for (float v = 0.35f; v <= 0.65f; v += 0.01f)
            {
                for (float u = 0.05f; u <= 0.95f; u += 0.01f)
                {
                    float g = LavaGlow.GlowUnit(u, v);
                    Assert.InRange(g, 0f, 1f);
                    if (g < min) min = g;
                    if (g > max) max = g;
                }
            }

            Assert.Equal(LavaGlow.CrustFloor, min, 3);
            Assert.True(max > LavaGlow.CrustFloor + 0.2f,
                "the crust has no glowing cracks at all");
        }

        [Fact]
        public void TheEdgesOfTheBandFadeOut()
        {
            Assert.Equal(0f, LavaGlow.AcrossFalloff(0f), 4);
            Assert.Equal(0f, LavaGlow.AcrossFalloff(1f), 4);
            Assert.Equal(1f, LavaGlow.AcrossFalloff(0.5f), 4);
            Assert.True(LavaGlow.AcrossFalloff(0.25f) > 0f);
            Assert.True(LavaGlow.AcrossFalloff(0.25f) < 1f);
        }

        [Fact]
        public void ItGoesOutCompletelyWhenTheLavaHasCooled()
        {
            // ★★ 以前は k = 0.15 + 0.85 x cool で、冷え切っても 0.15 残っていた。
            Assert.Equal(0f, LavaGlow.CoolFade(0f), 4);
            Assert.Equal(1f, LavaGlow.CoolFade(1f), 4);
            // 画面の明るさは CoolFade の 2 乗（色と不透明度の両方に掛かる）なので、
            // ここは 1 未満の指数＝「しばらく赤いまま、終わりで一気に暗くなる」である。
            Assert.True(LavaGlow.CoolFade(0.5f) > 0.5f);
            Assert.True(LavaGlow.CoolFade(0.5f) * LavaGlow.CoolFade(0.5f) < 0.5f);

            Assert.False(LavaGlow.Visible(0f));
            Assert.False(LavaGlow.Visible(LavaGlow.InvisibleBelow));
            Assert.True(LavaGlow.Visible(LavaGlow.InvisibleBelow + 0.01f));
            Assert.True(LavaGlow.Visible(1f));

            // 単調に暗くなる（途中で明るくなり返さない）。
            float previous = 0f;
            for (float c = 0f; c <= 1.0001f; c += 0.02f)
            {
                float k = LavaGlow.CoolFade(c);
                Assert.True(k >= previous - 0.0001f);
                previous = k;
            }
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            Assert.Equal(0f, LavaGlow.AlongFlowUnit(float.NaN), 4);
            Assert.Equal(0f, LavaGlow.CrackUnit(float.NaN, 0.5f), 4);
            Assert.Equal(0f, LavaGlow.CrackUnit(0.5f, float.NaN), 4);
            Assert.Equal(0f, LavaGlow.AcrossFalloff(float.NaN), 4);
            Assert.Equal(0f, LavaGlow.CoolFade(float.NaN), 4);
            Assert.False(LavaGlow.Visible(float.NaN));

            // 範囲外の u / v も [0,1] に収まる（テクスチャの端で色が飛ばない）。
            Assert.InRange(LavaGlow.GlowUnit(-1f, -1f), 0f, 1f);
            Assert.InRange(LavaGlow.GlowUnit(2f, 2f), 0f, 1f);
            Assert.InRange(LavaGlow.CoolFade(5f), 0f, 1f);
        }
    }
}
