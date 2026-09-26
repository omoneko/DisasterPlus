using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// Reproduces the <c>installed at (8,1094)</c> seen in-game (on a screen 1080 tall) and
    /// pins down the fix.
    /// </summary>
    public class ScreenSlotTests
    {
        private const float ButtonSize = 32f;
        private const float StepY = ButtonSize + 4f;   // the same as InfoHub
        private const float ViewHeight = 1080f;
        private const float ViewWidth = 1920f;
        private const int MaxTries = 30;

        [Fact]
        public void TheOldSearchWalkedOffTheBottomOfTheScreen()
        {
            // It used to descend unconditionally for 30 tries. The 30th candidate is
            // y = 50 + 36*29 = 1094.
            float lastOldCandidate = 50f + StepY * (MaxTries - 1);
            Assert.Equal(1094f, lastOldCandidate, 3);
            Assert.False(ScreenSlot.FitsWithin(lastOldCandidate, ButtonSize, ViewHeight));
        }

        [Fact]
        public void CandidatesStopAtTheBottomEdgeInsteadOfLeavingTheScreen()
        {
            int count = ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, ViewHeight, MaxTries);

            // Every candidate, including the last, must be on screen.
            Assert.True(count > 0);
            Assert.True(count < MaxTries, "the bottom edge must cut the search short");

            for (int i = 0; i < count; i++)
            {
                float y = 50f + StepY * i;
                Assert.True(ScreenSlot.FitsWithin(y, ButtonSize, ViewHeight),
                            "candidate " + i + " at y=" + y + " must be on screen");
            }

            // Exactly one step further is off screen (the boundary is not lax).
            float justPast = 50f + StepY * count;
            Assert.False(ScreenSlot.FitsWithin(justPast, ButtonSize, ViewHeight));
        }

        [Fact]
        public void ATallerViewAllowsMoreCandidatesButNeverMoreThanMaxTries()
        {
            Assert.True(ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, 1440f, MaxTries)
                        > ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, 1080f, MaxTries));

            // At 4320 all 30 tries fit on screen. It never returns more than maxTries.
            Assert.Equal(MaxTries,
                ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, 4320f, MaxTries));
        }

        [Fact]
        public void AZeroOrNegativeStepTestsExactlyOneCandidate()
        {
            Assert.Equal(1, ScreenSlot.CandidatesInside(50f, ButtonSize, 0f, ViewHeight, MaxTries));
            Assert.Equal(1, ScreenSlot.CandidatesInside(50f, ButtonSize, -8f, ViewHeight, MaxTries));
        }

        [Fact]
        public void AStartOutsideTheScreenYieldsNoCandidatesAtAll()
        {
            // Going further down only takes it further off screen, so the search itself is
            // pointless.
            Assert.Equal(0, ScreenSlot.CandidatesInside(1094f, ButtonSize, StepY, ViewHeight, MaxTries));
            Assert.Equal(0, ScreenSlot.CandidatesInside(-1f, ButtonSize, StepY, ViewHeight, MaxTries));
            Assert.Equal(0, ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, ViewHeight, 0));
        }

        [Fact]
        public void AnUnreadableViewSizeDoesNotBlockPlacement()
        {
            // Deciding that an unreadable size means "off screen" would leave us unable to
            // place a single button.
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.False(ScreenSlot.IsUsableExtent(bad));
                Assert.True(ScreenSlot.FitsWithin(9999f, ButtonSize, bad));
                Assert.Equal(50f, ScreenSlot.ClampInto(50f, ButtonSize, bad), 3);
                Assert.Equal(MaxTries,
                    ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, bad, MaxTries));
            }
        }

        [Fact]
        public void ClampBringsAPositionBackInsideAndPrefersTheTopLeft()
        {
            Assert.Equal(50f, ScreenSlot.ClampInto(50f, ButtonSize, ViewHeight), 3);
            Assert.Equal(ViewHeight - ButtonSize,
                         ScreenSlot.ClampInto(1094f, ButtonSize, ViewHeight), 3);
            Assert.Equal(0f, ScreenSlot.ClampInto(-40f, ButtonSize, ViewHeight), 3);

            // A rectangle larger than the screen is pushed to the top left
            // (never placed at a negative position).
            Assert.Equal(0f, ScreenSlot.ClampInto(10f, 2000f, ViewHeight), 3);

            // Even broken input points somewhere on screen.
            Assert.Equal(0f, ScreenSlot.ClampInto(float.NaN, ButtonSize, ViewHeight), 3);
            Assert.Equal(0f, ScreenSlot.ClampInto(float.NegativeInfinity, ButtonSize, ViewHeight), 3);
        }

        // ── The two-axis version (the search for lining up along the top row) ─────────────
        //    The owner's request: "so that it lines up at the same height as the CSWARFRONT
        //    button and the SIREN Alert button". A search that can only move vertically can
        //    never line up in that row.

        [Fact]
        public void HorizontalSearchStopsAtTheRightEdgeOfTheScreen()
        {
            // From x = 8 at the left edge, rightwards in steps of 40 px. The number of times
            // a 32 px button fits within a width of 1920.
            int tries = ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                    40f, 0f, ViewWidth, ViewHeight, 200);

            // The last x that fits is 8 + 40k <= 1920 - 32 = 1888 → k <= 47.0 → 48 candidates.
            Assert.Equal(48, tries);

            float lastX = 8f + 40f * (tries - 1);
            Assert.True(ScreenSlot.FitsWithin(lastX, ButtonSize, ViewWidth),
                        "the last candidate " + lastX + " must fit");
            Assert.False(ScreenSlot.FitsWithin(lastX + 40f, ButtonSize, ViewWidth),
                         "one step further must not fit");
        }

        [Fact]
        public void HorizontalSearchNeverLeavesTheTopRow()
        {
            // stepY = 0, so however many tries are made y never moves. **It never drops to
            // the row below.**
            int tries = ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                    40f, 0f, ViewWidth, ViewHeight, 12);
            Assert.Equal(12, tries);
        }

        [Fact]
        public void TwoAxisSearchRefusesAStartThatIsAlreadyOffScreen()
        {
            // Off screen horizontally.
            Assert.Equal(0, ScreenSlot.CandidatesInside(ViewWidth + 10f, 10f,
                                                        ButtonSize, ButtonSize,
                                                        40f, 0f, ViewWidth, ViewHeight, 30));
            // Off screen vertically (this is the old (8,1094)).
            Assert.Equal(0, ScreenSlot.CandidatesInside(8f, 1094f,
                                                        ButtonSize, ButtonSize,
                                                        40f, 0f, ViewWidth, ViewHeight, 30));
        }

        [Fact]
        public void TwoAxisSearchWithNoStepTestsExactlyOneCandidate()
        {
            Assert.Equal(1, ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                        0f, 0f, ViewWidth, ViewHeight, 30));
        }

        [Fact]
        public void TwoAxisSearchAgreesWithTheVerticalOneWhenOnlyYMoves()
        {
            int oneAxis = ScreenSlot.CandidatesInside(50f, ButtonSize, 36f, ViewHeight, 30);
            int twoAxis = ScreenSlot.CandidatesInside(8f, 50f, ButtonSize, ButtonSize,
                                                      0f, 36f, ViewWidth, ViewHeight, 30);
            Assert.Equal(oneAxis, twoAxis);
        }

        [Fact]
        public void UnreadableScreenWidthDoesNotStopTheHorizontalSearch()
        {
            // In an environment where the width cannot be read, it must not collapse into
            // "nothing can be placed".
            Assert.Equal(30, ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                         40f, 0f, 0f, ViewHeight, 30));
        }

        [Fact]
        public void EveryClampedPositionFitsOnScreen()
        {
            foreach (float y in new[] { -5000f, -1f, 0f, 50f, 1000f, 1094f, 100000f })
            {
                float clamped = ScreenSlot.ClampInto(y, ButtonSize, ViewHeight);
                Assert.True(ScreenSlot.FitsWithin(clamped, ButtonSize, ViewHeight),
                            "clamped " + y + " -> " + clamped + " must fit");
            }
        }
    }
}
