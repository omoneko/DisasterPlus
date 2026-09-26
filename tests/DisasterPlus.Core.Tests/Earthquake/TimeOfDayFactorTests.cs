using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class TimeOfDayFactorTests
    {
        [Fact]
        public void MiddayIsTheDayFactor()
        {
            // With the day/night cycle off, the hour is pinned at 12.0 forever
            // (IL facts document §F-1). Pinning down that the factor is exactly 1.0 in
            // that configuration guarantees that "nothing is added at all for people who
            // have it turned off".
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(12f), 5);
        }

        [Fact]
        public void DeepNightIsTheNightFactor()
        {
            Assert.Equal(TimeOfDayFactor.NightFactor, TimeOfDayFactor.Of(2f), 5);
            Assert.Equal(TimeOfDayFactor.NightFactor, TimeOfDayFactor.Of(23f), 5);
        }

        [Fact]
        public void SunriseAndSunsetMatchTheGameConstants()
        {
            // Measured from the IL: SUNRISE_HOUR = 5, SUNSET_HOUR = 20.
            Assert.Equal(5f, TimeOfDayFactor.SunriseHour, 5);
            Assert.Equal(20f, TimeOfDayFactor.SunsetHour, 5);
            Assert.True(TimeOfDayFactor.IsNight(4.9f));
            Assert.False(TimeOfDayFactor.IsNight(5.1f));
            Assert.False(TimeOfDayFactor.IsNight(19.9f));
            Assert.True(TimeOfDayFactor.IsNight(20.1f));
        }

        [Fact]
        public void TheBoundaryIsRampedNotStepped()
        {
            // If it were a step, the extra damage would jump in a single frame at
            // sunrise.
            float justBefore = TimeOfDayFactor.Of(TimeOfDayFactor.SunriseHour - 0.01f);
            float justAfter = TimeOfDayFactor.Of(TimeOfDayFactor.SunriseHour + 0.01f);
            Assert.True(System.Math.Abs(justBefore - justAfter) < 0.01f,
                "the sunrise boundary must not be a step");

            float dusk = TimeOfDayFactor.Of(TimeOfDayFactor.SunsetHour - 0.01f);
            float night = TimeOfDayFactor.Of(TimeOfDayFactor.SunsetHour + 0.01f);
            Assert.True(System.Math.Abs(dusk - night) < 0.01f,
                "the sunset boundary must not be a step either");
        }

        [Fact]
        public void IsAlwaysBetweenTheTwoFactors()
        {
            for (float h = -48f; h < 72f; h += 0.05f)
            {
                float f = TimeOfDayFactor.Of(h);
                Assert.InRange(f, TimeOfDayFactor.DayFactor, TimeOfDayFactor.NightFactor);
            }
        }

        [Fact]
        public void HoursOutsideTheDayAreFolded()
        {
            Assert.Equal(TimeOfDayFactor.Of(2f), TimeOfDayFactor.Of(26f), 5);
            Assert.Equal(TimeOfDayFactor.Of(2f), TimeOfDayFactor.Of(-22f), 5);
        }

        [Fact]
        public void GarbageHourIsTheNeutralFactor()
        {
            // A broken reading must not make the damage multiplier jump.
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.NaN), 5);
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.PositiveInfinity), 5);
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.NegativeInfinity), 5);
            Assert.False(TimeOfDayFactor.IsNight(float.NaN));
        }

        [Fact]
        public void TheCoefficientIsModest()
        {
            // "Damage is greater at night" has no basis anywhere in vanilla (design
            // document §4.3). What is pinned down here is that judgement itself: keep it
            // small enough that it does not cast doubt on the layer 1 figures.
            Assert.Equal(1f, TimeOfDayFactor.DayFactor, 5);
            Assert.True(TimeOfDayFactor.NightFactor > TimeOfDayFactor.DayFactor);
            Assert.True(TimeOfDayFactor.NightFactor <= 1.25f,
                "the invented night penalty must stay small enough not to dominate layer 1");
        }
    }
}
