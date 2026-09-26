using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// Live report (2026-08-22): "As for the typhoon, the effect disappears almost at
    /// once. Please reproduce the way a typhoon moves slowly."
    ///
    /// ★★ Extending the lifetime **automatically lowers the speed** (because the path
    ///    length is divided by the lifetime). What is pinned here is that relationship,
    ///    and the behaviour when the host's duration cannot be read.
    /// </summary>
    public class TyphoonLifetimeTests
    {
        /// <summary>Measured from ThunderStormAI (sharedassets55).</summary>
        private const uint HostDuration = 8192u;

        [Fact]
        public void TheTyphoonOutlivesItsHostStorm()
        {
            uint life = TyphoonTrack.LifetimeFramesFor(HostDuration);

            Assert.True(life > HostDuration, life + " is not longer than the host's " + HostDuration);
            Assert.Equal(HostDuration * TyphoonTrack.LifetimeMultiplier, life);
        }

        [Fact]
        public void ALongerLifeMeansASlowerTyphoon()
        {
            float before = TyphoonTrack.SpeedFor(HostDuration);
            float after = TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration));

            Assert.True(after < before, after + " should be slower than " + before);

            // The path length has not been changed, so the speed drops by exactly the
            // multiplier (unless it gets clamped to the band).
            Assert.Equal(before / TyphoonTrack.LifetimeMultiplier, after, 4);
        }

        [Fact]
        public void ItCrossesTheWholeMapInOneLifeSoItCanArriveFromOutside()
        {
            // ★★ **The intent was reversed on 2026-09-02.** It used to pin that the
            //   typhoon "does not make it all the way across even with a full lifetime",
            //   but now the typhoon <b>comes in from off the map, passes through the
            //   clicked point, and leaves</b>. That needs one map edge's worth of
            //   distance (see the doc on NominalPathLength).
            //
            // ★ "Looks slow" is created not by the speed but by the <b>fraction spent on
            //   the approach</b>. If it is reported as too fast, the thing to lower is
            //   ApproachFraction.
            float speed = TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration));
            float life = TyphoonTrack.LifetimeFramesFor(HostDuration);

            float travelled = speed * life;
            Assert.True(travelled >= TyphoonTrack.MapHalfExtent * 2f,
                        "it must be able to cross the map to arrive from outside");
        }

        [Fact]
        public void AnUnreadableHostDurationYieldsNothingToStartWith()
        {
            // If it cannot be read, 0. The caller then raises no typhoon at all
            // (design doc §6).
            Assert.Equal(0u, TyphoonTrack.LifetimeFramesFor(0u));
            Assert.Equal(0f, TyphoonTrack.SpeedFor(0u), 5);
        }

        [Fact]
        public void AnAbsurdHostDurationDoesNotOverflow()
        {
            // Both the .cgs and the prefab can be edited by hand. The multiplication
            // must not wrap around.
            uint life = TyphoonTrack.LifetimeFramesFor(uint.MaxValue);
            Assert.True(life >= uint.MaxValue / TyphoonTrack.LifetimeMultiplier,
                        "the lifetime wrapped around");
        }
    }
}
