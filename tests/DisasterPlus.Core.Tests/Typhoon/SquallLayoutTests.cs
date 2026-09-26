using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class SquallLayoutTests
    {
        [Fact]
        public void NothingIsDrawnOutsideTheStorm()
        {
            // ★★ **Spray must never blow about while you are outside the typhoon.**
            //   TyphoonProfile.WindAt returns exactly 0 outside the gale-force area, so if
            //   this returns 0 then "not one particle outside" is guaranteed structurally.
            Assert.Equal(0f, SquallLayout.StrengthOf(0f), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(SquallLayout.MinWindUnit), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(float.NaN), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(-1f), 6);
        }

        [Fact]
        public void StrengthRisesToOneAndStopsThere()
        {
            Assert.Equal(1f, SquallLayout.StrengthOf(SquallLayout.FullWindUnit), 6);
            Assert.Equal(1f, SquallLayout.StrengthOf(1f), 6);
            Assert.Equal(1f, SquallLayout.StrengthOf(4f), 6);

            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float s = SquallLayout.StrengthOf(i / 20f);
                Assert.InRange(s, 0f, 1f);
                Assert.True(s >= previous, "strength is not monotonic at " + (i / 20f));
                previous = s;
            }
        }

        [Fact]
        public void TheWindTurnsAroundTheEyeAndLeansInward()
        {
            // The secondary circulation of a typhoon. Tangential (the direction the vortex
            // turns, i.e. increasing angle) plus inflow. Reverse it and the cloud and the
            // rain flow in opposite directions.
            float wx, wz;
            SquallLayout.WindDirection(1000f, 0f, out wx, out wz);

            // It must be a unit vector.
            Assert.Equal(1f, wx * wx + wz * wz, 4);

            // At (+X, 0) the tangent is +Z and the inflow is -X.
            Assert.True(wz > 0f, "the wind does not turn the same way as the vortex");
            Assert.True(wx < 0f, "the wind does not lean towards the eye");
        }

        [Fact]
        public void TheCentreOfTheEyeDoesNotProduceNaN()
        {
            // Directly over the centre the direction is undetermined.
            // **Emit neither an exception nor a NaN.**
            float wx, wz;
            SquallLayout.WindDirection(0f, 0f, out wx, out wz);
            Assert.False(float.IsNaN(wx));
            Assert.False(float.IsNaN(wz));
            Assert.Equal(1f, wx * wx + wz * wz, 4);

            SquallLayout.WindDirection(float.NaN, float.NaN, out wx, out wz);
            Assert.False(float.IsNaN(wx));
            Assert.False(float.IsNaN(wz));
        }

        [Fact]
        public void EveryPatchIsInsideTheDrawableRanges()
        {
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch p = SquallLayout.PatchAt(i);

                Assert.InRange(p.HeightFraction, 0f, 1f);
                Assert.InRange(p.DiscFraction, 0f, 1f);
                Assert.InRange(p.BandFraction, 0f, 1f);
                Assert.InRange(p.DensityFraction, 0f, 1f);

                // It does not stray far beyond the scatter radius (it stays around the camera).
                float r = p.OffsetXFraction * p.OffsetXFraction
                          + p.OffsetZFraction * p.OffsetZFraction;
                Assert.True(r <= 1.25f, "patch " + i + " is thrown too far from the camera");
            }
        }

        [Fact]
        public void TheLayoutIsTheSameEveryTime()
        {
            // A function of the index alone. Mix the frame in and the spray's spawn points
            // jump about every frame.
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch a = SquallLayout.PatchAt(i);
                SquallPatch b = SquallLayout.PatchAt(i);
                Assert.Equal(a.OffsetXFraction, b.OffsetXFraction, 6);
                Assert.Equal(a.OffsetZFraction, b.OffsetZFraction, 6);
                Assert.Equal(a.HeightFraction, b.HeightFraction, 6);
                Assert.Equal(a.DensityFraction, b.DensityFraction, 6);
            }
        }

        [Fact]
        public void OutOfRangeIndicesDoNotThrow()
        {
            SquallPatch a = SquallLayout.PatchAt(-1);
            SquallPatch b = SquallLayout.PatchAt(SquallLayout.PatchCount + 50);
            Assert.Equal(a.OffsetXFraction, b.OffsetXFraction, 6);
        }

        [Fact]
        public void TheSprayFallsAndStaysLow()
        {
            // This is rain. **It must not float** (gravity is positive).
            Assert.True(SquallLayout.GravityModifier > 0f);
            // It scatters almost horizontally (the axis points up, so a larger angle is
            // more sideways).
            Assert.True(SquallLayout.SpawnAngleMinDegrees >= 60f);
            // Short-lived (it flies, falls and is gone). Far shorter than the vortex puffs.
            Assert.True(SquallLayout.LifeMaxSeconds
                        < VortexCloudProfile.Tower.LifeMinSeconds);
            // ★ Set it to 0 and not one particle appears (the trap of §D-2).
            Assert.True(SquallLayout.RateOverTime > 0f);
            // The city must never become invisible.
            Assert.InRange(SquallLayout.Alpha, 0.05f, 0.5f);
        }
    }
}
