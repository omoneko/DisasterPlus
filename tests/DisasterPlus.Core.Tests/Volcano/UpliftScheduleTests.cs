using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class UpliftScheduleTests
    {
        private const ushort BaseRaw = 2560;   // 40 m (sea level). Same conversion as §D-4.

        [Fact]
        public void ProgressZeroIsTheOriginalGroundAndProgressOneIsTheFinalHeight()
        {
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, 400f, 0f));
            Assert.Equal((ushort)(BaseRaw + 400 * 64), UpliftSchedule.RawTargetAt(BaseRaw, 400f, 1f));
        }

        [Fact]
        public void TheSummitAdvancesByAtLeastOneRawUnitEveryTick()
        {
            // ★ Trap 2. If one tick changes by less than 1/64 m it vanishes in the rounding
            //   and the uplift stops dead, silently (the `if (n != raw)` of §C-8).
            //   TotalTicksFor prevents that structurally. The right fix to make this check
            //   pass is not to enlarge the "increment" but to **cut down the tick count**.
            const float h = 300f;
            int total = UpliftSchedule.TotalTicksFor(h, 100000);
            ushort previous = UpliftSchedule.RawTargetAt(BaseRaw, h, UpliftSchedule.ProgressAt(0, total));
            for (int t = 1; t <= total; t++)
            {
                ushort now = UpliftSchedule.RawTargetAt(BaseRaw, h, UpliftSchedule.ProgressAt(t, total));
                Assert.True(now > previous,
                    "the summit did not move at tick " + t + " (raw stayed at " + now + ")");
                previous = now;
            }
        }

        [Fact]
        public void AskingForMoreTicksThanTheQuantumAllowsIsTruncated()
        {
            // A 20 m mountain can only be sliced into at most 20*64 = 1280 ticks.
            Assert.True(UpliftSchedule.TotalTicksFor(20f, 100000) <= 1280);
            // A sufficiently short request passes through unchanged.
            Assert.Equal(600, UpliftSchedule.TotalTicksFor(600f, 600));
            // Even a request of 0 or a negative one returns at least 1 tick
            // (so no division by zero is created).
            Assert.True(UpliftSchedule.TotalTicksFor(600f, 0) >= 1);
            Assert.True(UpliftSchedule.TotalTicksFor(600f, -5) >= 1);
            Assert.True(UpliftSchedule.TotalTicksFor(0f, 500) >= 1);
        }

        [Fact]
        public void WalkingEveryTickReachesExactlyTheFinalRawValue()
        {
            // The target is absolute, so however many ticks a cell failed to move along the
            // way, the final value always matches.
            const float h = 173.4f;
            int total = UpliftSchedule.TotalTicksFor(h, 900);
            ushort last = 0;
            for (int t = 0; t <= total; t++)
            {
                last = UpliftSchedule.RawTargetAt(BaseRaw, h * 0.13f, UpliftSchedule.ProgressAt(t, total));
            }
            Assert.Equal(UpliftSchedule.RawTargetAt(BaseRaw, h * 0.13f, 1f), last);
        }

        [Fact]
        public void TheRawTargetSaturatesAtTheCeilingInsteadOfWrapping()
        {
            // A ushort wrap-around (the summit dropping back to sea level) is the most
            // painful way for this to break.
            Assert.Equal((ushort)UpliftSchedule.MaxRaw,
                         UpliftSchedule.RawTargetAt(60000, 400f, 1f));
            Assert.Equal((ushort)0, UpliftSchedule.RawTargetAt(0, -400f, 1f));
        }

        [Fact]
        public void NothingIsRaisedOutsideTheClearedRadius()
        {
            // ★ Trap 1. Raise a place the preparation has not reached and the roads and
            //   buildings push the terrain back on every flush (§A-2), leaving flat trenches
            //   and craters inside the mountain.
            Assert.Equal(300f, UpliftSchedule.ActiveRadiusMetres(1200f, 300f), 3);
            Assert.Equal(1200f, UpliftSchedule.ActiveRadiusMetres(1200f, 5000f), 3);
        }

        [Fact]
        public void WithNothingClearedYetTheActiveRadiusIsZero()
        {
            // ★ The most dangerous way into trap 1. Mistake "nothing has been demolished yet"
            //   for "no limit" and the uplift suddenly raises the whole area.
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, 0f), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, -1f), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(1200f, float.NaN), 4);
            Assert.Equal(0f, UpliftSchedule.ActiveRadiusMetres(float.NaN, 300f), 4);
        }

        [Fact]
        public void TheClearingFrontStaysAheadOfTheUpliftFront()
        {
            const float r = 1200f;
            const float lead = 96f;
            for (float p = 0f; p <= 1f; p += 0.05f)
            {
                float front = UpliftSchedule.ClearingFrontMetres(r, p, lead);
                Assert.True(front >= r * p, "the clearing front fell behind at p=" + p);
                Assert.InRange(front, 0f, r);
            }
            Assert.Equal(r, UpliftSchedule.ClearingFrontMetres(r, 1f, lead), 3);
            // Even with a lead of 0 it never falls behind.
            Assert.True(UpliftSchedule.ClearingFrontMetres(r, 0.5f, 0f) >= r * 0.5f);
        }

        [Fact]
        public void TheBlockHeightCatchUpIsTwoMetresPerSixtyFourFrames()
        {
            // §A-2: in game mode it rises by 2 m / 64 sim frames. 300 m takes 9600 frames.
            Assert.Equal(9600, UpliftSchedule.BlockHeightCatchUpFrames(300f));
            Assert.Equal(64, UpliftSchedule.BlockHeightCatchUpFrames(2f));
            Assert.Equal(128, UpliftSchedule.BlockHeightCatchUpFrames(2.5f));
            Assert.Equal(0, UpliftSchedule.BlockHeightCatchUpFrames(0f));
            Assert.Equal(0, UpliftSchedule.BlockHeightCatchUpFrames(float.NaN));
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, float.NaN, 0.5f));
            Assert.Equal(BaseRaw, UpliftSchedule.RawTargetAt(BaseRaw, 400f, float.NaN));
            Assert.Equal(0f, UpliftSchedule.ClearingFrontMetres(float.NaN, 0.5f, 96f), 4);
            Assert.Equal(0f, UpliftSchedule.ClearingFrontMetres(1200f, float.NaN, 96f), 4);
        }

        [Fact]
        public void ProgressIsClampedAtBothEnds()
        {
            Assert.Equal(0f, UpliftSchedule.ProgressAt(-5, 100), 4);
            Assert.Equal(1f, UpliftSchedule.ProgressAt(500, 100), 4);
            Assert.Equal(0f, UpliftSchedule.ProgressAt(5, 0), 4);
            Assert.Equal(0f, UpliftSchedule.ProgressAt(5, -3), 4);
            Assert.Equal(0.5f, UpliftSchedule.ProgressAt(50, 100), 4);
        }
        // ── Uplift spreading outward from the summit (GrowthMetresAt) ──────────────────

        [Fact]
        public void GrowthStartsAtNothingAndEndsAtTheFinalProfile()
        {
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(300f, 600f, 0f), 4);
            Assert.Equal(300f, UpliftSchedule.GrowthMetresAt(300f, 600f, 1f), 4);
            Assert.Equal(600f, UpliftSchedule.GrowthMetresAt(600f, 600f, 1f), 4);
        }

        [Fact]
        public void TheSummitStillRisesInProportionToProgress()
        {
            // The profile at the summit is H, so the summit's rise stays H×progress
            // (this does not change the meaning of the "current summit" the panel and the
            // diagnostics report).
            for (float p = 0f; p <= 1f; p += 0.05f)
            {
                Assert.Equal(600f * p, UpliftSchedule.GrowthMetresAt(600f, 600f, p), 3);
            }
        }

        [Fact]
        public void TheFrontExpandsOutwardAsProgressAdvances()
        {
            // ★★ The task itself. The mountain must not swell uniformly; it must spread
            //    outward from the summit. For a straight cone (stratovolcano), what has
            //    surfaced at progress p is d < R·p.
            const float r = 1200f, h = 600f;
            float previousFront = -1f;

            for (float p = 0.1f; p <= 1.0f; p += 0.1f)
            {
                float front = 0f;
                for (float d = 0f; d <= r; d += 1f)
                {
                    float profile = VolcanoShape.ProfileAt(VolcanoForm.Strato, d, r, h);
                    if (UpliftSchedule.GrowthMetresAt(profile, h, p) > 0f) front = d;
                }

                Assert.True(front > previousFront, "the front went backwards at p=" + p);
                Assert.True(front <= r, "the front left the radius at p=" + p);
                // For a straight cone the front is exactly R·p (found one 1 m scan step inside).
                Assert.InRange(front, r * p - 2f, r * p);
                previousFront = front;
            }
        }

        [Fact]
        public void EveryGrowingCellRisesByTheSameAmountEachTick()
        {
            // ★★ This is if anything stronger against trap 2. With profile × progress, the
            //    further out you go the smaller one tick's change becomes, and cells whose
            //    total rise was small vanished in the rounding. With this formula every
            //    growing cell rises by H/totalTicks.
            const float h = 600f;
            int ticks = UpliftSchedule.TotalTicksFor(h, 85);
            float expected = h / ticks;

            Assert.True(expected >= 1f / UpliftSchedule.RawUnitsPerMetre,
                "a tick moves less than one raw unit: " + expected);

            foreach (float profile in new float[] { 600f, 300f, 120f, 20f, 0.5f })
            {
                for (int t = 1; t < ticks; t++)
                {
                    float a = UpliftSchedule.GrowthMetresAt(profile, h,
                                  UpliftSchedule.ProgressAt(t - 1, ticks));
                    float b = UpliftSchedule.GrowthMetresAt(profile, h,
                                  UpliftSchedule.ProgressAt(t, ticks));

                    Assert.True(b >= a, "a cell went down between ticks");
                    // While still growing (neither 0 nor capped), the rise is H/ticks.
                    if (a > 0f && b < profile) Assert.Equal(expected, b - a, 2);
                }
            }
        }

        [Fact]
        public void GrowthIsNeverNegativeAndNeverPassesTheProfile()
        {
            for (float p = -1f; p <= 2f; p += 0.05f)
            {
                float v = UpliftSchedule.GrowthMetresAt(250f, 600f, p);
                Assert.InRange(v, 0f, 250f);
            }
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(float.NaN, 600f, 0.5f), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(250f, float.NaN, 0.5f), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(250f, 600f, float.NaN), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthMetresAt(-5f, 600f, 0.5f), 4);
        }
    }
}
