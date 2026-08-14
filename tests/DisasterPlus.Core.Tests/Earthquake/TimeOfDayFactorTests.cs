using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    public class TimeOfDayFactorTests
    {
        [Fact]
        public void MiddayIsTheDayFactor()
        {
            // 日夜サイクル OFF のとき時刻は永久に 12.0 に固定される（IL 事実文書 §F-1）。
            // その設定で係数がちょうど 1.0 になることを固定しておくと、
            // 「切っている人には何も足されない」が保証できる。
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
            // IL 実測: SUNRISE_HOUR = 5、SUNSET_HOUR = 20。
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
            // 段差にすると、日の出の 1 フレームで追加被害が跳ねる。
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
            // 壊れた読み取りで被害倍率が跳ねないこと。
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.NaN), 5);
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.PositiveInfinity), 5);
            Assert.Equal(TimeOfDayFactor.DayFactor, TimeOfDayFactor.Of(float.NegativeInfinity), 5);
            Assert.False(TimeOfDayFactor.IsNight(float.NaN));
        }

        [Fact]
        public void TheCoefficientIsModest()
        {
            // 「夜の方が被害が大きい」はバニラのどこにも根拠が無い（設計書 §4.3）。
            // 第 1 層の数字を疑わせない程度に抑える、という判断そのものを固定する。
            Assert.Equal(1f, TimeOfDayFactor.DayFactor, 5);
            Assert.True(TimeOfDayFactor.NightFactor > TimeOfDayFactor.DayFactor);
            Assert.True(TimeOfDayFactor.NightFactor <= 1.25f,
                "the invented night penalty must stay small enough not to dominate layer 1");
        }
    }
}
