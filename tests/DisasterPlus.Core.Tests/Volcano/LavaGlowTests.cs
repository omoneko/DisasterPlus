using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// The glow of the lava (in-game observation ④ "it is flickering", "it keeps
    /// glowing even after the eruption has finished").
    ///
    /// Four things are pinned down here:
    ///   1. **There is no time argument anywhere** (so it cannot possibly flicker)
    ///   2. Brightest is the <b>advancing front</b>, then the <b>vent</b>, crust in between
    ///   3. The crust has cracks too, and the brightness varies <b>by place</b>
    ///   4. Once cooled right down it becomes **exactly 0** and the renderer folds it away
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
            // As the lava advances, the v of a place already laid down shifts lower.
            // ★ This is the implementation of "cools with age" itself.
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
            // Crossing the crust band (v = 0.4 to 0.6), bright places and dark places
            // must **both be present**. If it is all the same brightness, it is "paint".
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
            // ★★ It used to be k = 0.15 + 0.85 x cool, leaving 0.15 even once fully cooled.
            Assert.Equal(0f, LavaGlow.CoolFade(0f), 4);
            Assert.Equal(1f, LavaGlow.CoolFade(1f), 4);
            // On-screen brightness is CoolFade squared (it multiplies both the colour and
            // the opacity), so an exponent below 1 here means "stays red for a while,
            // then darkens all at once at the end".
            Assert.True(LavaGlow.CoolFade(0.5f) > 0.5f);
            Assert.True(LavaGlow.CoolFade(0.5f) * LavaGlow.CoolFade(0.5f) < 0.5f);

            Assert.False(LavaGlow.Visible(0f));
            Assert.False(LavaGlow.Visible(LavaGlow.InvisibleBelow));
            Assert.True(LavaGlow.Visible(LavaGlow.InvisibleBelow + 0.01f));
            Assert.True(LavaGlow.Visible(1f));

            // It darkens monotonically (it never brightens again part-way through).
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

            // Out-of-range u / v also stay within [0,1] (no colour blow-out at the
            // edge of the texture).
            Assert.InRange(LavaGlow.GlowUnit(-1f, -1f), 0f, 1f);
            Assert.InRange(LavaGlow.GlowUnit(2f, 2f), 0f, 1f);
            Assert.InRange(LavaGlow.CoolFade(5f), 0f, 1f);
        }
    }
}
