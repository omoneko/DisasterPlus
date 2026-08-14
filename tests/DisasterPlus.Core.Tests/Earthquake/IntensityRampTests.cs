using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 地図に描くランプが、**カーソル行が出している s と同じ量・同じ量子化**であること。
    ///
    /// この機能でいちばん出してはいけない壊れ方は「示していると称する量とは
    /// 違う減衰を描く」ことなので、ここのテストは見た目ではなく
    /// <c>SeismicIntensity</c> との一致を固定する。
    /// </summary>
    public class IntensityRampTests
    {
        private const byte Intensity = 55;

        [Fact]
        public void StepsAreTheSameQuantisationAsTheCursorBar()
        {
            // 段数が食い違うと、同じ s に対して 2 つの違う「段」が
            // 画面に同時に出ることになる（バーは 10 段、地図は別の段数）。
            Assert.Equal(SeismicScale.Steps, IntensityRamp.Steps);
        }

        [Fact]
        public void EachDiscEdgeLandsExactlyOnItsStepBoundary()
        {
            float r = SeismicIntensity.RadiusOf(Intensity);

            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float radius = IntensityRamp.RadiusOf(k, r);

                // 円盤 k の縁で s は k/Steps ちょうど。つまりこの円盤は
                // 「s > k/Steps の範囲」を覆っている。
                Assert.Equal((float)k / IntensityRamp.Steps,
                             SeismicIntensity.At(radius, Intensity), 4);
            }
        }

        [Fact]
        public void TheOutermostDiscIsTheWholeDestructionRadius()
        {
            float r = SeismicIntensity.RadiusOf(Intensity);
            Assert.Equal(r, IntensityRamp.RadiusOf(0, r), 3);
            Assert.True(IntensityRamp.RadiusOf(IntensityRamp.Steps - 1, r) < r);
        }

        [Fact]
        public void RadiiShrinkStrictlyInward()
        {
            float r = SeismicIntensity.RadiusOf(Intensity);
            for (int k = 1; k < IntensityRamp.Steps; k++)
            {
                Assert.True(IntensityRamp.RadiusOf(k, r) < IntensityRamp.RadiusOf(k - 1, r));
            }
        }

        /// <summary>
        /// **このファイルの中心。** 大きい順にアルファ合成した結果の濃さが、
        /// その輪帯の s に**比例**すること。
        ///
        /// 同じアルファを重ねると <c>1-(1-a)^(k+1)</c> の飽和曲線になり、
        /// 弱い側を強く・強い側を弱く見せる。それは減衰の描き違いである。
        /// </summary>
        [Fact]
        public void AccumulatedOpacityIsProportionalToS()
        {
            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float s = IntensityRamp.RepresentativeS(k);
                Assert.Equal(IntensityRamp.MaxOpacity * s,
                             IntensityRamp.AccumulatedOpacity(k), 4);
            }
        }

        [Fact]
        public void AccumulatedOpacityReachesTheCapAtTheEpicentre()
        {
            float innermost = IntensityRamp.AccumulatedOpacity(IntensityRamp.Steps - 1);

            // 震央側の代表 s は (Steps-0.5)/Steps なので、上限そのものには届かない。
            // ただし上限の 9 割は超えていて、地形を完全には隠さない。
            Assert.True(innermost > IntensityRamp.MaxOpacity * 0.9f);
            Assert.True(innermost < IntensityRamp.MaxOpacity);
            Assert.True(IntensityRamp.MaxOpacity < 1f);
        }

        /// <summary>
        /// 単体アルファが 0..1 に収まり、**内側ほど大きい**こと。
        ///
        /// 単調性はこの機能の保険でもある。仮に <c>alphaBlend: true</c> が
        /// アルファ合成ではなく上書きだったとしても（シェーダのブレンド式だけは
        /// DLL から読めない）、単調である限り**濃さが震央へ向かって増える向きは
        /// 反転しない** —— 曲線が凸に歪むだけで済む。
        /// </summary>
        [Fact]
        public void DrawAlphaIsMonotoneInwardAndWithinRange()
        {
            float previous = -1f;
            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float a = IntensityRamp.DrawAlpha(k);
                Assert.True(a > 0f && a < 1f);
                Assert.True(a > previous);
                previous = a;
            }
        }

        [Fact]
        public void OutOfRangeIndicesDrawNothing()
        {
            Assert.Equal(0f, IntensityRamp.RadiusOf(-1, 3100f), 4);
            Assert.Equal(0f, IntensityRamp.RadiusOf(IntensityRamp.Steps, 3100f), 4);
            Assert.Equal(0f, IntensityRamp.DrawAlpha(-1), 4);
            Assert.Equal(0f, IntensityRamp.DrawAlpha(IntensityRamp.Steps), 4);
        }

        [Fact]
        public void ANonPositiveRadiusDrawsNothing()
        {
            Assert.Equal(0f, IntensityRamp.RadiusOf(0, 0f), 4);
            Assert.Equal(0f, IntensityRamp.RadiusOf(0, float.NaN), 4);
        }
    }
}
