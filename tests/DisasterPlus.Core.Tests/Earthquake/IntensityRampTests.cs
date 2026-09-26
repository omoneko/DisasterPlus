using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// The ramp drawn on the map must be **the same quantity and the same quantisation as
    /// the s reported by the cursor row**.
    ///
    /// The one way this feature must never break is by "drawing an attenuation different
    /// from the quantity it claims to show", so these tests pin down agreement with
    /// <c>SeismicIntensity</c> rather than the look of it.
    /// </summary>
    public class IntensityRampTests
    {
        private const byte Intensity = 55;

        [Fact]
        public void StepsAreTheSameQuantisationAsTheCursorBar()
        {
            // If the step counts disagree, two different "steps" for the same s appear on
            // screen at once (the bar with 10 steps, the map with some other number).
            Assert.Equal(SeismicScale.Steps, IntensityRamp.Steps);
        }

        [Fact]
        public void EachDiscEdgeLandsExactlyOnItsStepBoundary()
        {
            float r = SeismicIntensity.RadiusOf(Intensity);

            for (int k = 0; k < IntensityRamp.Steps; k++)
            {
                float radius = IntensityRamp.RadiusOf(k, r);

                // At the edge of disc k, s is exactly k/Steps. In other words this disc
                // covers "the region where s > k/Steps".
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
        /// **The heart of this file.** The opacity resulting from alpha-compositing the
        /// discs from largest to smallest must be **proportional** to that annulus's s.
        ///
        /// Stacking the same alpha gives the saturating curve <c>1-(1-a)^(k+1)</c>, which
        /// makes the weak side look strong and the strong side look weak. That is drawing
        /// the attenuation wrongly.
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

            // The representative s on the epicentre side is (Steps-0.5)/Steps, so it never
            // reaches the cap itself. It does exceed 90 per cent of the cap, yet it never
            // hides the terrain completely.
            Assert.True(innermost > IntensityRamp.MaxOpacity * 0.9f);
            Assert.True(innermost < IntensityRamp.MaxOpacity);
            Assert.True(IntensityRamp.MaxOpacity < 1f);
        }

        /// <summary>
        /// The individual alpha must lie in 0..1 and **grow towards the inside**.
        ///
        /// Monotonicity is also this feature's insurance. Even if <c>alphaBlend: true</c>
        /// turned out to overwrite rather than alpha-composite (the shader's blend equation
        /// is the one thing we cannot read from the DLL), as long as it is monotone
        /// **the direction in which the opacity increases towards the epicentre does not
        /// reverse** —— the curve merely warps into a convex one.
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
