using DisasterPlus.Core.Common;
using Xunit;

namespace DisasterPlus.Core.Tests.Common
{
    /// <summary>
    /// 実機で出た <c>installed at (8,1094)</c>（高さ 1080 の画面）の再現と、対処の固定。
    /// </summary>
    public class ScreenSlotTests
    {
        private const float ButtonSize = 32f;
        private const float StepY = ButtonSize + 4f;   // InfoHub と同じ
        private const float ViewHeight = 1080f;
        private const float ViewWidth = 1920f;
        private const int MaxTries = 30;

        [Fact]
        public void TheOldSearchWalkedOffTheBottomOfTheScreen()
        {
            // 以前は 30 回ぶん無条件に降りていた。30 回目の候補は y = 50 + 36*29 = 1094。
            float lastOldCandidate = 50f + StepY * (MaxTries - 1);
            Assert.Equal(1094f, lastOldCandidate, 3);
            Assert.False(ScreenSlot.FitsWithin(lastOldCandidate, ButtonSize, ViewHeight));
        }

        [Fact]
        public void CandidatesStopAtTheBottomEdgeInsteadOfLeavingTheScreen()
        {
            int count = ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, ViewHeight, MaxTries);

            // 最後の候補まで含めて全部が画面の中にあること。
            Assert.True(count > 0);
            Assert.True(count < MaxTries, "the bottom edge must cut the search short");

            for (int i = 0; i < count; i++)
            {
                float y = 50f + StepY * i;
                Assert.True(ScreenSlot.FitsWithin(y, ButtonSize, ViewHeight),
                            "candidate " + i + " at y=" + y + " must be on screen");
            }

            // ちょうど 1 歩ぶん外側は画面外である（境界が甘くない）。
            float justPast = 50f + StepY * count;
            Assert.False(ScreenSlot.FitsWithin(justPast, ButtonSize, ViewHeight));
        }

        [Fact]
        public void ATallerViewAllowsMoreCandidatesButNeverMoreThanMaxTries()
        {
            Assert.True(ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, 1440f, MaxTries)
                        > ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, 1080f, MaxTries));

            // 4320 なら 30 回ぶん全部が画面に入る。maxTries を超えて返さない。
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
            // 下へ進めばもっと外れるので、探索そのものが無意味。
            Assert.Equal(0, ScreenSlot.CandidatesInside(1094f, ButtonSize, StepY, ViewHeight, MaxTries));
            Assert.Equal(0, ScreenSlot.CandidatesInside(-1f, ButtonSize, StepY, ViewHeight, MaxTries));
            Assert.Equal(0, ScreenSlot.CandidatesInside(50f, ButtonSize, StepY, ViewHeight, 0));
        }

        [Fact]
        public void AnUnreadableViewSizeDoesNotBlockPlacement()
        {
            // 寸法が読めないことを「画面外」と決めつけると、ボタンが 1 個も置けなくなる。
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

            // 画面より大きい矩形は左上に寄せる（負の位置に置かない）。
            Assert.Equal(0f, ScreenSlot.ClampInto(10f, 2000f, ViewHeight), 3);

            // 壊れた入力でも画面の中を指す。
            Assert.Equal(0f, ScreenSlot.ClampInto(float.NaN, ButtonSize, ViewHeight), 3);
            Assert.Equal(0f, ScreenSlot.ClampInto(float.NegativeInfinity, ButtonSize, ViewHeight), 3);
        }

        // ── 2 軸版（画面最上段へ横に並べるための探索）─────────────────────
        //    所有者の依頼「CSWARFRONT ボタンや SIREN Alert ボタンと同じ高さで
        //    並んで表示されるように」。縦にしか進めない探索ではあの列に並べない。

        [Fact]
        public void HorizontalSearchStopsAtTheRightEdgeOfTheScreen()
        {
            // 左端 8 から 40 px 刻みで右へ。32 px のボタンが 1920 幅に収まる回数。
            int tries = ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                    40f, 0f, ViewWidth, ViewHeight, 200);

            // 最後に収まる x は 8 + 40k <= 1920 - 32 = 1888 → k <= 47.0 → 48 候補。
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
            // stepY = 0 なので、何回試しても y は動かない。**下の段へ落ちない。**
            int tries = ScreenSlot.CandidatesInside(8f, 10f, ButtonSize, ButtonSize,
                                                    40f, 0f, ViewWidth, ViewHeight, 12);
            Assert.Equal(12, tries);
        }

        [Fact]
        public void TwoAxisSearchRefusesAStartThatIsAlreadyOffScreen()
        {
            // 横に外れている。
            Assert.Equal(0, ScreenSlot.CandidatesInside(ViewWidth + 10f, 10f,
                                                        ButtonSize, ButtonSize,
                                                        40f, 0f, ViewWidth, ViewHeight, 30));
            // 縦に外れている（以前の (8,1094) がこれである）。
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
            // 幅が読めない環境で「1 つも置けない」に倒れないこと。
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
