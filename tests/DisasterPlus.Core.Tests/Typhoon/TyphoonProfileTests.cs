using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TyphoonProfileTests
    {
        // The prefab radius is unknown until it has been measured. The tests pass a
        // placeholder value and pin down only "it is proportional to this value".
        private const float Radius = 1000f;

        [Fact]
        public void StormRadiusUsesTheVanillaScatterFormula()
        {
            // §A-1 / §A-2: R = m_radius * (0.25 + intensity * 0.0075)
            // Matching feature no. 4's storm area to this formula makes the hazard disc
            // vanilla paints and the storm area feature no. 4 displays the same size.
            Assert.Equal(Radius * 0.25f, TyphoonProfile.StormRadiusOf(0, Radius), 3);
            Assert.Equal(Radius * 1.0f, TyphoonProfile.StormRadiusOf(100, Radius), 3);
            Assert.Equal(Radius * 2.1625f, TyphoonProfile.StormRadiusOf(255, Radius), 3);
        }

        [Fact]
        public void UnknownPrefabRadiusGivesZeroNotAGuess()
        {
            // ★ In an environment where the prefab value cannot be read, both the storm area
            //    and the gale area become 0 and the caller does nothing. Design doc §6.
            Assert.Equal(0f, TyphoonProfile.StormRadiusOf(255, 0f), 4);
            Assert.Equal(0f, TyphoonProfile.GaleRadiusOf(255, 0f), 4);
            Assert.Equal(0f, TyphoonProfile.WindAt(0f, 255, 0f), 4);
            Assert.Equal(0f, TyphoonProfile.StormRadiusOf(255, float.NaN), 4);
        }

        [Fact]
        public void TheGaleRadiusIsLargerThanTheStormRadius()
        {
            Assert.True(TyphoonProfile.GaleRadiusOf(100, Radius)
                        > TyphoonProfile.StormRadiusOf(100, Radius));
        }

        [Fact]
        public void TheEyeIsCalmAndTheEyewallIsTheStrongest()
        {
            float r = TyphoonProfile.StormRadiusOf(100, Radius);
            float eye = TyphoonProfile.WindAt(0f, 100, Radius);
            float wall = TyphoonProfile.WindAt(r * TyphoonProfile.WallFraction, 100, Radius);
            float outer = TyphoonProfile.WindAt(r * 0.9f, 100, Radius);

            Assert.True(eye < wall, "the eye must be calmer than the eyewall");
            Assert.True(outer < wall, "the eyewall must be the strongest ring");
            Assert.Equal(1f, wall, 3);
        }

        [Fact]
        public void WindIsAlwaysInsideZeroToOne()
        {
            for (int i = 0; i <= 255; i += 5)
            {
                for (float d = 0f; d < 12000f; d += 50f)
                {
                    Assert.InRange(TyphoonProfile.WindAt(d, (byte)i, Radius), 0f, 1f);
                }
            }
        }

        [Fact]
        public void BeyondTheGaleRadiusThereIsNoWind()
        {
            float gale = TyphoonProfile.GaleRadiusOf(100, Radius);
            Assert.Equal(0f, TyphoonProfile.WindAt(gale, 100, Radius), 4);
            Assert.Equal(0f, TyphoonProfile.WindAt(gale + 1000f, 100, Radius), 4);
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            // Never display "wind speed NaN" because of a broken reading.
            Assert.Equal(0f, TyphoonProfile.WindAt(float.NaN, 100, Radius), 4);
            Assert.Equal(0f, TyphoonProfile.WindAt(-1f, 100, Radius), 4);
        }

        [Fact]
        public void StepIsMonotonicBoundedAndClamped()
        {
            int prev = -1;
            for (int i = 0; i <= 1000; i++)
            {
                int step = TyphoonProfile.StepOf(i / 1000f);
                Assert.True(step >= prev, "step decreased at " + i);
                Assert.InRange(step, 0, TyphoonProfile.Steps);
                prev = step;
            }
            Assert.Equal(0, TyphoonProfile.StepOf(-5f));
            Assert.Equal(TyphoonProfile.Steps, TyphoonProfile.StepOf(5f));
            Assert.Equal(0, TyphoonProfile.StepOf(float.NaN));
        }

        [Fact]
        public void BarIsAsciiOnlyAndItsLengthIsFixed()
        {
            // The same decision as feature no. 1's HazardLevel and no. 2's SeismicScale.
            // There is no guarantee that CS's UI font has box-drawing characters.
            for (int i = 0; i <= 1000; i += 13)
            {
                string bar = TyphoonProfile.BarOf(i / 1000f);
                Assert.Equal(TyphoonProfile.Steps, bar.Length);

                int filled = 0;
                foreach (char c in bar)
                {
                    Assert.True(c < 128, "non-ASCII character in bar: " + (int)c);
                    Assert.True(c == TyphoonProfile.FilledChar || c == TyphoonProfile.EmptyChar,
                        "unexpected char: " + c);
                    if (c == TyphoonProfile.FilledChar) filled++;
                }
                Assert.Equal(TyphoonProfile.StepOf(i / 1000f), filled);
            }
        }
    }
}
