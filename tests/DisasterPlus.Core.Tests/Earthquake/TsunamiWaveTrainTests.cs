using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 所有者の指示（2026-08-29）:
    ///
    /// &gt; 地震→すぐに震源地に海面の巨大な隆起が生成→隆起が台地状に拡大→
    /// &gt; ある程度大きくなったら中央は緩やかに沈下して元の海面に（ドーナツ状）→
    /// &gt; 同心円状の水の壁が、減衰することなく拡散する
    ///
    /// ★★ ここが固定するのは<b>その 4 段が順番どおりに起きること</b>と、
    ///    <b>最後には必ず 0 へ戻ること</b>である。0 に戻らないと呼び出し側が
    ///    水位を下げる合図を受け取れず、<b>持ち上げた海がセーブに残る</b>。
    /// </summary>
    public class TsunamiWaveTrainTests
    {
        private const float Amplitude = 6f;

        /// <summary>ゲーム速度 1 の目安。**フレームと実秒を混ぜないための注釈。**</summary>
        private const float FramesPerRealSecond = 60f;

        private static float Rise(float d, float f)
        {
            return TsunamiWaveTrain.RiseAt(d, f, Amplitude);
        }

        // ── ★★ 時計の単位 ────────────────────────────────────────

        [Fact]
        public void TheWaveLastsLongEnoughToBeWatched()
        {
            // ★★ **これが 2026-08-29 の「継続力が弱い」の直接の再発防止である。**
            //    前の版は時計がゲーム内秒で、1 sim フレーム = 1.32 ゲーム内秒
            //    （DAYTIME_FRAMES 65536/日）だったため、
            //    「900 秒」が 683 フレーム ≒ 11 実秒にしかならなかった。
            //    環が広がる区間に至っては 94 フレーム ≒ 1.6 実秒。
            float ringSeconds =
                (TsunamiWaveTrain.RingLeavesAtFrame - TsunamiWaveTrain.CollapseFrames)
                / FramesPerRealSecond;

            Assert.True(ringSeconds > 25f,
                        "the ring only spreads for " + ringSeconds
                        + " real seconds; that is a blink, not a tsunami");

            float total = TsunamiWaveTrain.TotalFrames / FramesPerRealSecond;
            Assert.True(total > 45f, "the whole wave is over in " + total + " real seconds");
        }

        [Fact]
        public void NothingIsLeftRunningAfterTheRingHasGone()
        {
            // 環が届く限界へ出たあとに長い空回りを残さない
            //（水源を抱えたまま何も起きない時間になる）。
            float idle = TsunamiWaveTrain.TotalFrames - TsunamiWaveTrain.RingLeavesAtFrame;

            Assert.True(idle >= 0f, "the wave ends before the ring has left");
            Assert.True(idle / FramesPerRealSecond < 15f,
                        "the wave idles for " + (idle / FramesPerRealSecond)
                        + " real seconds after the ring has gone");
        }

        // ── ① 隆起 ─────────────────────────────────────────────

        [Fact]
        public void TheBulgeAppearsAtTheEpicentreImmediately()
        {
            // 「すぐに震源地に海面の巨大な隆起が生成」。
            float f = TsunamiWaveTrain.BulgeFrames * 0.8f;

            Assert.True(Rise(0f, f) > Amplitude * 0.5f,
                        "there is no bulge at the epicentre at frame " + f);

            // まだ小さい。台地の広さには育っていない。
            Assert.Equal(0f, Rise(TsunamiWaveTrain.PlateauRadiusMetres, f), 3);
        }

        [Fact]
        public void TheBulgeRisesRatherThanPoppingIntoExistence()
        {
            float a = Rise(0f, TsunamiWaveTrain.BulgeFrames * 0.2f);
            float b = Rise(0f, TsunamiWaveTrain.BulgeFrames * 0.5f);
            float c = Rise(0f, TsunamiWaveTrain.BulgeFrames);

            Assert.True(a < b && b < c, a + " " + b + " " + c);
            Assert.True(a < Amplitude * 0.5f, "the bulge is already full at 20% of stage 1");
        }

        // ── ② 台地 ─────────────────────────────────────────────

        [Fact]
        public void TheBulgeGrowsIntoAPlateau()
        {
            // 「隆起が台地状に拡大」。縁が外へ動き、天面は平らなまま。
            float early = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.BulgeFrames);
            float late = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.SpreadFrames);

            Assert.True(late > early * 2f, early + " -> " + late);
            Assert.Equal(TsunamiWaveTrain.PlateauRadiusMetres, late, 1);

            // 天面が平ら ＝ 中心と、縁の少し内側が同じ高さ。
            float f = TsunamiWaveTrain.SpreadFrames;
            float centre = Rise(0f, f);
            float mid = Rise(TsunamiWaveTrain.PlateauRadiusMetres * 0.4f, f);

            Assert.Equal(centre, mid, 2);
            Assert.True(centre > Amplitude * 0.9f);
        }

        // ── ③ ドーナツ ──────────────────────────────────────────

        [Fact]
        public void TheCentreSinksBackToSeaLevelLeavingARing()
        {
            // 「中央は緩やかに沈下して元の海面に（ドーナツ状）」。
            float f = TsunamiWaveTrain.CollapseFrames;

            Assert.Equal(0f, Rise(0f, f), 3);

            float ring = TsunamiWaveTrain.RingRadiusAt(f);
            Assert.True(Rise(ring, f) > Amplitude,
                        "the ring is not standing at " + ring + " m");
        }

        [Fact]
        public void TheCentreSinksGraduallyRatherThanDropping()
        {
            // 「緩やかに沈下」。段差で落ちないこと。
            float a = Rise(0f, TsunamiWaveTrain.SpreadFrames + 1f);
            float b = Rise(0f, (TsunamiWaveTrain.SpreadFrames
                                + TsunamiWaveTrain.CollapseFrames) * 0.5f);
            float c = Rise(0f, TsunamiWaveTrain.CollapseFrames - 1f);

            Assert.True(a > b && b > c, a + " " + b + " " + c);
            Assert.True(b > Amplitude * 0.2f && b < Amplitude * 0.9f,
                        "the centre jumped straight down instead of sinking (" + b + ")");
        }

        [Fact]
        public void TheRingIsTallerThanTheBulgeItCameFrom()
        {
            // 中央の水が環へ寄せられるので、環は元の隆起より高くなる。
            float bulge = Rise(0f, TsunamiWaveTrain.BulgeFrames);

            float f = TsunamiWaveTrain.CollapseFrames;
            float ring = Rise(TsunamiWaveTrain.RingRadiusAt(f), f);

            Assert.True(ring > bulge, bulge + " -> " + ring);
        }

        // ── ④ 減衰しない拡散 ─────────────────────────────────────

        [Fact]
        public void TheRingSpreadsWithoutDecaying()
        {
            // ★★ 「同心円状の水の壁が、**減衰することなく**拡散する」。
            //    これが今回いちばん大事な要求である。
            float first = 0f;

            for (float f = TsunamiWaveTrain.CollapseFrames + 30f;
                 f < TsunamiWaveTrain.TotalFrames * TsunamiWaveTrain.FadeFromFraction;
                 f += 60f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(f);
                if (ring > TsunamiWaveTrain.ReachEdgeMetres) break;

                float h = Rise(ring, f);
                if (first <= 0f) { first = h; continue; }

                Assert.Equal(first, h, 2);
            }

            Assert.True(first > Amplitude, "the ring never even reached the amplitude");
        }

        [Fact]
        public void TheRingKeepsMovingOutward()
        {
            float a = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseFrames + 100f);
            float b = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseFrames + 400f);
            float c = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseFrames + 800f);

            Assert.True(b > a && c > b, a + " " + b + " " + c);
        }

        [Fact]
        public void TheRingReachesTheEdgeOfItsReach()
        {
            Assert.Equal(TsunamiWaveTrain.ReachEdgeMetres,
                         TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.RingLeavesAtFrame), 1);
        }

        [Fact]
        public void NothingIsRaisedAheadOfTheRing()
        {
            // ★★ **これが「同心円」の要である。** 前線より外がわずかでも
            //    上がっていると、輪ではなく「一様に持ち上がった海」に見える。
            for (float f = 20f; f < TsunamiWaveTrain.TotalFrames * 0.5f; f += 40f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(f);
                float ahead = ring + TsunamiWaveTrain.RingWidthMetres + 100f;
                if (ahead >= TsunamiWaveTrain.ReachEdgeMetres) continue;

                Assert.Equal(0f, Rise(ahead, f), 4);
            }
        }

        [Fact]
        public void NothingIsRaisedInsideTheRingOnceItHasHollowedOut()
        {
            // ★★ 「現在のような海面全体の隆起は不要です」。
            //    環の内側は<b>平常の海</b>でなければならない。
            for (float f = TsunamiWaveTrain.CollapseFrames + 100f;
                 f < TsunamiWaveTrain.TotalFrames * 0.6f; f += 100f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(f);
                float inside = ring - TsunamiWaveTrain.RingWidthMetres * 1.5f;
                if (inside < 100f) continue;

                Assert.True(Rise(inside, f) < Amplitude * 0.05f,
                            "the sea inside the ring is still up by " + Rise(inside, f)
                            + " m at frame " + f);
            }
        }

        [Fact]
        public void TheInnerEdgeFollowsTheRingOnceItIsHollow()
        {
            // 水源を並べる帯の内側。ドーナツになるまでは 0（中央まで詰まっている）。
            Assert.Equal(0f, TsunamiWaveTrain.RingInnerRadiusAt(TsunamiWaveTrain.BulgeFrames), 3);
            Assert.Equal(0f, TsunamiWaveTrain.RingInnerRadiusAt(TsunamiWaveTrain.SpreadFrames), 3);

            // ★★ **陥没の途中も 0。** 中央はまだ海面より上で沈んでいる最中なので、
            //    ここを切り上げると沈めるはずの水を担当する水源がいなくなる
            //    （2026-08-29 の Codex レビュー P2）。
            float mid = (TsunamiWaveTrain.SpreadFrames
                         + TsunamiWaveTrain.CollapseFrames) * 0.5f;
            Assert.True(TsunamiWaveTrain.Hollowness(mid) > 0f);
            Assert.True(TsunamiWaveTrain.RiseAt(0f, mid, 6f) > 0f);
            Assert.Equal(0f, TsunamiWaveTrain.RingInnerRadiusAt(mid), 3);

            float f = TsunamiWaveTrain.CollapseFrames + 600f;
            Assert.Equal(TsunamiWaveTrain.RingRadiusAt(f) - TsunamiWaveTrain.RingWidthMetres,
                         TsunamiWaveTrain.RingInnerRadiusAt(f), 2);
        }

        // ── 後始末 ─────────────────────────────────────────────

        [Fact]
        public void TheSeaAlwaysGoesBackDown()
        {
            // ★★ **ここが破れると海が戻らず、水源がセーブに残る。**
            for (float d = 0f; d <= TsunamiWaveTrain.ReachEdgeMetres; d += 250f)
            {
                Assert.Equal(0f, Rise(d, TsunamiWaveTrain.TotalFrames), 4);
                Assert.Equal(0f, Rise(d, TsunamiWaveTrain.TotalFrames + 600f), 4);
            }

            Assert.Equal(0f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalFrames), 4);
        }

        [Fact]
        public void TheEnvelopeHoldsAtOneUntilTheVeryEnd()
        {
            // 「減衰することなく」なので、包絡線は最後まで 1 のままであること。
            Assert.Equal(1f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalFrames * 0.5f), 4);
            Assert.Equal(1f, TsunamiWaveTrain.EnvelopeAt(
                TsunamiWaveTrain.TotalFrames * TsunamiWaveTrain.FadeFromFraction), 4);

            Assert.True(TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalFrames * 0.995f) < 0.1f);
        }

        [Fact]
        public void TheStagesAreInOrder()
        {
            Assert.True(TsunamiWaveTrain.BulgeFrames < TsunamiWaveTrain.SpreadFrames);
            Assert.True(TsunamiWaveTrain.SpreadFrames < TsunamiWaveTrain.CollapseFrames);
            Assert.True(TsunamiWaveTrain.CollapseFrames < TsunamiWaveTrain.TotalFrames);
        }

        [Fact]
        public void TheRiseIsNeverNegativeAndNeverRunsAway()
        {
            float ceiling = Amplitude * TsunamiWaveTrain.RingPeakFactor * 1.02f;

            for (float f = 0f; f < TsunamiWaveTrain.TotalFrames; f += 25f)
            {
                for (float d = 0f; d <= 10000f; d += 100f)
                {
                    float r = Rise(d, f);
                    Assert.True(r >= 0f, "negative rise at d=" + d + " frame=" + f);
                    Assert.True(r <= ceiling,
                                "the sea reached " + r + " m for a " + Amplitude + " m wave");
                }
            }
        }

        [Fact]
        public void AStrongerQuakeMakesAHigherWave()
        {
            Assert.True(TsunamiWaveTrain.AmplitudeOf(255) > TsunamiWaveTrain.AmplitudeOf(100));
            Assert.InRange(TsunamiWaveTrain.AmplitudeOf(255),
                           TsunamiWaveTrain.MinAmplitudeMetres,
                           TsunamiWaveTrain.MaxAmplitudeMetres);
            Assert.True(TsunamiWaveTrain.AmplitudeOf(1) >= TsunamiWaveTrain.MinAmplitudeMetres);
        }

        [Fact]
        public void BrokenInputRaisesNoWater()
        {
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(float.NaN, 100f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, float.NaN, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 100f, float.NaN), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(-100f, 100f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, -100f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 100f, 0f), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RingRadiusAt(float.NaN), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RingInnerRadiusAt(float.NaN), 4);
        }
    }
}
