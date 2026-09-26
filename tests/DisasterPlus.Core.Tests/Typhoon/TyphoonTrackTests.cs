using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class TyphoonTrackTests
    {
        /// <summary>Stands in for the point the player pointed at.
        /// One point inside the map.</summary>
        private static readonly Vec2 Pointed = new Vec2(1200f, -800f);

        [Fact]
        public void BearingIsInsideOneTurnAndDependsOnTheSeed()
        {
            int distinct = 0;
            float first = TyphoonTrack.BearingOf(1u);
            for (uint s = 1; s < 200; s++)
            {
                float b = TyphoonTrack.BearingOf(s);
                Assert.InRange(b, 0f, 6.2831855f);
                if (b != first) distinct++;
            }
            Assert.True(distinct >= 190, "too few distinct bearings: " + distinct);
        }

        [Fact]
        public void SameSeedAlwaysGivesTheSameTrack()
        {
            // If this breaks, "reproducible from the same save" (design doc §4.1) is a lie.
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 50; s++)
            {
                Vec2 a = TyphoonTrack.CentreAt(Pointed, s, 5000u, speed);
                Vec2 b = TyphoonTrack.CentreAt(Pointed, s, 5000u, speed);
                Assert.Equal(a.X, b.X, 4);
                Assert.Equal(a.Z, b.Z, 4);
            }
        }

        [Fact]
        public void TheStormStartsExactlyWhereThePlayerPointed()
        {
            // ★★ The same promise as the vanilla disaster button (design doc §4.1). It
            //    starts from the point that was pointed at. If it drifts you get "the
            //    typhoon appears somewhere other than where I clicked", and not a single
            //    exception is thrown.
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 100; s++)
            {
                Vec2 start = TyphoonTrack.CentreAt(Pointed, s, 0u, speed);
                Assert.Equal(Pointed.X, start.X, 3);
                Assert.Equal(Pointed.Z, start.Z, 3);
                Assert.True(TyphoonTrack.IsInsideMap(start));
            }
        }

        [Fact]
        public void TheTrackStaysOnTheMapForMostOfItsLifetime()
        {
            // ★★ **The meaning of this was inverted on 2026-08-22.**
            //   Following the owner's remark "make it advance more slowly", NominalPathLength
            //   was halved from one map edge to a half edge, so **the typhoon normally does
            //   not leave the map**.
            //   This test used to pin down "it must leave the map" (i.e. we believed the only
            //   end condition was "it came inside once and then went out").
            //   The normal way it now ends is "it used up its duration", and that path goes
            //   through the same TyphoonController.Stop -> Forget.
            //
            //   What is pinned down here is **that it stays over the city** ——
            //   this feature recreates a raging storm, so past half of its lifetime the
            //   centre must still be inside the map.
            const uint dur = TyphoonPrefabActiveDuration;
            float speed = TyphoonTrack.SpeedFor(dur);

            for (uint s = 1; s < 100; s++)
            {
                Vec2 half = TyphoonTrack.CentreAt(Pointed, s, dur / 2u, speed);
                Assert.True(TyphoonTrack.IsInsideMap(half),
                            "track for seed " + s + " has already left the map at half life");
            }
        }

        [Fact]
        public void TheStormCrossesTheWholeMapInOneLife()
        {
            // ★★ **Put back from a half edge to a full edge on 2026-09-02.** (Owner: "it
            //   appears at the map edge, gradually approaches the clicked point, and then
            //   holds its course and departs".)
            //
            //   Only half the lifetime is available for the approach, so we need a speed
            //   that <b>can cover a map half-edge in that half</b>. When the centre is
            //   pointed at, the distance to the edge is exactly a half edge.
            //   Trying 12,000 m only got 6,000 m back from the centre, so the storm welled
            //   up from inside the map (see the doc on <c>NominalPathLength</c>).
            float speed = TyphoonTrack.SpeedFor(TyphoonPrefabActiveDuration);
            Assert.Equal(2.1094f, speed, 3);

            // Over a full lifetime it crosses one map edge (17280 m).
            float framesToCross = TyphoonTrack.MapHalfExtent * 2f / speed;
            Assert.Equal((float)TyphoonPrefabActiveDuration, framesToCross, 0);

            // ★ The approach uses 25-75% of the lifetime (the flat part of the intensity
            //   trapezium). If it feels "too fast", what to lower is this fraction, not the
            //   speed.
            Assert.Equal(0.25f, TyphoonTrack.MinApproachFraction, 3);
            Assert.Equal(0.75f, TyphoonTrack.ApproachFraction, 3);
        }

        /// <summary>The measured value of <c>ThunderStormAI.m_activeDuration</c>
        /// (recovered from the raw bytes of <c>sharedassets55.assets</c>; IL facts
        /// document §A-0b).</summary>
        private const uint TyphoonPrefabActiveDuration = 8192u;

        [Fact]
        public void ZeroCurvatureIsTheLimitOfSmallCurvature()
        {
            // The κ == 0 branch only breaks for values that are "small but not zero".
            // That can never be found by eye, so it is pinned down here.
            const float speed = 1.3f;
            float theta = 0.7f;
            var entry = new Vec2(-12000f, 0f);

            Vec2 straight = TyphoonTrack.ArcPosition(entry, theta, 0f, 3000f, speed);
            Vec2 nearlyStraight = TyphoonTrack.ArcPosition(entry, theta, 1e-9f, 3000f, speed);

            Assert.Equal(straight.X, nearlyStraight.X, 1);
            Assert.Equal(straight.Z, nearlyStraight.Z, 1);
        }

        [Fact]
        public void SpeedMatchesTheDistanceTravelledPerFrame()
        {
            float speed = TyphoonTrack.SpeedFor(20000u);
            for (uint s = 1; s < 30; s++)
            {
                Vec2 a = TyphoonTrack.CentreAt(Pointed, s, 4000u, speed);
                Vec2 b = TyphoonTrack.CentreAt(Pointed, s, 4001u, speed);
                float d = (float)System.Math.Sqrt(a.DistanceSquaredTo(b));
                Assert.Equal(speed, d, 2);
            }
        }

        [Fact]
        public void SpeedIsDerivedFromTheMeasuredActiveDuration()
        {
            // The longer the storm, the slower it moves. The path length (one map edge) is
            // the same, so it comes out as the reciprocal of the duration.
            Assert.True(TyphoonTrack.SpeedFor(40000u) < TyphoonTrack.SpeedFor(10000u));
            Assert.InRange(TyphoonTrack.SpeedFor(10u),
                           TyphoonTrack.MinSpeedMetresPerFrame,
                           TyphoonTrack.MaxSpeedMetresPerFrame);
            Assert.InRange(TyphoonTrack.SpeedFor(100000000u),
                           TyphoonTrack.MinSpeedMetresPerFrame,
                           TyphoonTrack.MaxSpeedMetresPerFrame);
        }

        [Fact]
        public void UnknownDurationGivesZeroSpeedNotAGuess()
        {
            // ★ The only place that structurally guarantees design doc §6's "if it cannot
            //    be read, do not guess — do nothing". Rewrite this to a "safe default" and
            //    the typhoon will start moving at a guessed speed on any machine where the
            //    prefab cannot be read.
            Assert.Equal(0f, TyphoonTrack.SpeedFor(0u), 5);
        }

        [Fact]
        public void IntensityRisesPlateausAndFallsLikeTheVanillaLightningRamp()
        {
            const byte peak = 200;
            const uint dur = 20000u;

            byte start = TyphoonTrack.IntensityAt(peak, 0u, dur, 0f);
            byte middle = TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 0f);
            byte end = TyphoonTrack.IntensityAt(peak, dur, dur, 0f);

            Assert.True(start < middle, "the storm must ramp up");
            Assert.Equal(peak, middle);
            Assert.True(end < middle, "the storm must ramp down");
        }

        [Fact]
        public void ALiveTyphoonNeverReportsZeroIntensity()
        {
            // This is what produced "intensity=0" in the first in-game test.
            // If the ends of the trapezium are 0 you get a typhoon that is there but does
            // nothing, and it cannot even be told apart from "the value could not be read".
            const uint dur = 8192u;

            foreach (byte peak in new byte[] { 10, 120, 255 })
            {
                Assert.True(TyphoonTrack.IntensityAt(peak, 0u, dur, 0f) > 0,
                            "peak " + peak + " must not start at zero");
                Assert.True(TyphoonTrack.IntensityAt(peak, dur, dur, 0f) > 0,
                            "peak " + peak + " must not end at zero either");
            }
        }

        [Fact]
        public void ZeroIntensityStillMeansUnreadableOrFullyDecayed()
        {
            // The floor above only prevents "being rounded down to 0"; it does not
            // spoil the meaning of 0.
            Assert.Equal(0, TyphoonTrack.IntensityAt(200, 0u, 0u, 0f));      // duration unreadable
            Assert.Equal(0, TyphoonTrack.IntensityAt(200, 4096u, 8192u, 5000f)); // fully decayed
        }

        [Fact]
        public void LandfallWeakensTheStormAndTheSeaGivesItBackSlowly()
        {
            float overLand = TyphoonTrack.DecayAfter(0f, true, 10f);
            Assert.True(overLand > 0f, "the storm must weaken over land");

            float recovered = TyphoonTrack.DecayAfter(overLand, false, 10f);
            Assert.True(recovered < overLand, "the sea must give strength back");
            Assert.True(recovered > 0f, "recovery must be slower than decay");

            // It can recover completely. It never goes negative.
            Assert.Equal(0f, TyphoonTrack.DecayAfter(0f, false, 1000f), 4);
            // It does not pile up without limit.
            Assert.InRange(TyphoonTrack.DecayAfter(0f, true, 100000f), 0f, TyphoonTrack.MaxDecay);
        }

        [Fact]
        public void DecayLowersTheIntensityAndNeverWrapsAround()
        {
            const byte peak = 100;
            const uint dur = 20000u;
            byte weak = TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 90f);
            Assert.True(weak < peak);
            // A byte wrapping round (becoming 255) is the most painful way to break.
            Assert.Equal(0, TyphoonTrack.IntensityAt(peak, dur / 2u, dur, 5000f));
        }

        [Fact]
        public void PhaseWalksForwardsOnlyAndEndsAtGone()
        {
            const uint dur = 20000u;
            Assert.Equal(TyphoonPhase.Approaching, TyphoonTrack.PhaseAt(0u, dur));
            Assert.Equal(TyphoonPhase.Peak, TyphoonTrack.PhaseAt(dur / 2u, dur));
            Assert.Equal(TyphoonPhase.Passing, TyphoonTrack.PhaseAt(dur * 4u / 5u, dur));
            Assert.Equal(TyphoonPhase.Gone, TyphoonTrack.PhaseAt(dur, dur));
            Assert.Equal(TyphoonPhase.Gone, TyphoonTrack.PhaseAt(dur * 10u, dur));

            // Do not claim a phase when the duration has not been read.
            Assert.Equal(TyphoonPhase.Idle, TyphoonTrack.PhaseAt(0u, 0u));
        }
    }
}
