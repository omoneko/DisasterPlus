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

        private static float Rise(float d, float t)
        {
            return TsunamiWaveTrain.RiseAt(d, t, Amplitude);
        }

        // ── ① 隆起 ─────────────────────────────────────────────

        [Fact]
        public void TheBulgeAppearsAtTheEpicentreImmediately()
        {
            // 「すぐに震源地に海面の巨大な隆起が生成」。
            float t = TsunamiWaveTrain.BulgeSeconds * 0.5f;

            Assert.True(Rise(0f, t) > Amplitude * 0.5f,
                        "there is no bulge at the epicentre at t=" + t);

            // まだ小さい。台地の広さには育っていない。
            Assert.Equal(0f, Rise(TsunamiWaveTrain.PlateauRadiusMetres, t), 3);
        }

        // ── ② 台地 ─────────────────────────────────────────────

        [Fact]
        public void TheBulgeGrowsIntoAPlateau()
        {
            // 「隆起が台地状に拡大」。縁が外へ動き、天面は平らなまま。
            float early = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.BulgeSeconds);
            float late = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.SpreadSeconds);

            Assert.True(late > early * 2f, early + " -> " + late);
            Assert.Equal(TsunamiWaveTrain.PlateauRadiusMetres, late, 1);

            // 天面が平ら ＝ 中心と、縁の少し内側が同じ高さ。
            float t = TsunamiWaveTrain.SpreadSeconds;
            float centre = Rise(0f, t);
            float mid = Rise(TsunamiWaveTrain.PlateauRadiusMetres * 0.4f, t);

            Assert.Equal(centre, mid, 2);
            Assert.True(centre > Amplitude * 0.9f);
        }

        // ── ③ ドーナツ ──────────────────────────────────────────

        [Fact]
        public void TheCentreSinksBackToSeaLevelLeavingARing()
        {
            // 「中央は緩やかに沈下して元の海面に（ドーナツ状）」。
            float t = TsunamiWaveTrain.CollapseSeconds;

            Assert.Equal(0f, Rise(0f, t), 3);

            float ring = TsunamiWaveTrain.RingRadiusAt(t);
            Assert.True(Rise(ring, t) > Amplitude,
                        "the ring is not standing at " + ring + " m");
        }

        [Fact]
        public void TheCentreSinksGraduallyRatherThanDropping()
        {
            // 「緩やかに沈下」。段差で落ちないこと。
            float a = Rise(0f, TsunamiWaveTrain.SpreadSeconds + 1f);
            float b = Rise(0f, (TsunamiWaveTrain.SpreadSeconds
                                + TsunamiWaveTrain.CollapseSeconds) * 0.5f);
            float c = Rise(0f, TsunamiWaveTrain.CollapseSeconds - 1f);

            Assert.True(a > b && b > c, a + " " + b + " " + c);
            Assert.True(b > Amplitude * 0.2f && b < Amplitude * 0.9f,
                        "the centre jumped straight down instead of sinking (" + b + ")");
        }

        [Fact]
        public void TheRingIsTallerThanTheBulgeItCameFrom()
        {
            // 中央の水が環へ寄せられるので、環は元の隆起より高くなる。
            float bulge = Rise(0f, TsunamiWaveTrain.BulgeSeconds);

            float t = TsunamiWaveTrain.CollapseSeconds;
            float ring = Rise(TsunamiWaveTrain.RingRadiusAt(t), t);

            Assert.True(ring > bulge, bulge + " -> " + ring);
        }

        // ── ④ 減衰しない拡散 ─────────────────────────────────────

        [Fact]
        public void TheRingSpreadsWithoutDecaying()
        {
            // ★★ 「同心円状の水の壁が、**減衰することなく**拡散する」。
            //    これが今回いちばん大事な要求である。
            float first = 0f;

            for (float t = TsunamiWaveTrain.CollapseSeconds + 10f;
                 t < TsunamiWaveTrain.TotalSeconds * TsunamiWaveTrain.FadeFromFraction;
                 t += 20f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(t);
                if (ring > TsunamiWaveTrain.ReachEdgeMetres) break;

                float h = Rise(ring, t);
                if (first <= 0f) { first = h; continue; }

                Assert.Equal(first, h, 2);
            }

            Assert.True(first > Amplitude, "the ring never even reached the amplitude");
        }

        [Fact]
        public void TheRingKeepsMovingOutward()
        {
            float a = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseSeconds + 20f);
            float b = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseSeconds + 80f);
            float c = TsunamiWaveTrain.RingRadiusAt(TsunamiWaveTrain.CollapseSeconds + 160f);

            Assert.True(b > a && c > b, a + " " + b + " " + c);
        }

        [Fact]
        public void NothingIsRaisedAheadOfTheRing()
        {
            // ★★ **これが「同心円」の要である。** 前線より外がわずかでも
            //    上がっていると、輪ではなく「一様に持ち上がった海」に見える。
            for (float t = 5f; t < TsunamiWaveTrain.TotalSeconds * 0.5f; t += 15f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(t);
                float ahead = ring + TsunamiWaveTrain.RingWidthMetres + 100f;
                if (ahead >= TsunamiWaveTrain.ReachEdgeMetres) continue;

                Assert.Equal(0f, Rise(ahead, t), 4);
            }
        }

        [Fact]
        public void NothingIsRaisedInsideTheRingOnceItHasHollowedOut()
        {
            // ★★ 「現在のような海面全体の隆起は不要です」。
            //    環の内側は<b>平常の海</b>でなければならない。
            for (float t = TsunamiWaveTrain.CollapseSeconds + 30f;
                 t < TsunamiWaveTrain.TotalSeconds * 0.6f; t += 30f)
            {
                float ring = TsunamiWaveTrain.RingRadiusAt(t);
                float inside = ring - TsunamiWaveTrain.RingWidthMetres * 1.5f;
                if (inside < 100f) continue;

                Assert.True(Rise(inside, t) < Amplitude * 0.05f,
                            "the sea inside the ring is still up by " + Rise(inside, t)
                            + " m at t=" + t);
            }
        }

        // ── 後始末 ─────────────────────────────────────────────

        [Fact]
        public void TheSeaAlwaysGoesBackDown()
        {
            // ★★ **ここが破れると海が戻らず、水源がセーブに残る。**
            for (float d = 0f; d <= TsunamiWaveTrain.ReachEdgeMetres; d += 250f)
            {
                Assert.Equal(0f, Rise(d, TsunamiWaveTrain.TotalSeconds), 4);
                Assert.Equal(0f, Rise(d, TsunamiWaveTrain.TotalSeconds + 120f), 4);
            }

            Assert.Equal(0f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds), 4);
        }

        [Fact]
        public void TheEnvelopeHoldsAtOneUntilTheVeryEnd()
        {
            // 「減衰することなく」なので、包絡線は最後まで 1 のままであること。
            Assert.Equal(1f, TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.5f), 4);
            Assert.Equal(1f, TsunamiWaveTrain.EnvelopeAt(
                TsunamiWaveTrain.TotalSeconds * TsunamiWaveTrain.FadeFromFraction), 4);

            Assert.True(TsunamiWaveTrain.EnvelopeAt(TsunamiWaveTrain.TotalSeconds * 0.99f) < 0.1f);
        }

        [Fact]
        public void TheStagesAreInOrder()
        {
            Assert.True(TsunamiWaveTrain.BulgeSeconds < TsunamiWaveTrain.SpreadSeconds);
            Assert.True(TsunamiWaveTrain.SpreadSeconds < TsunamiWaveTrain.CollapseSeconds);
            Assert.True(TsunamiWaveTrain.CollapseSeconds < TsunamiWaveTrain.TotalSeconds);
        }

        [Fact]
        public void TheRiseIsNeverNegativeAndNeverRunsAway()
        {
            float ceiling = Amplitude * TsunamiWaveTrain.RingPeakFactor * 1.02f;

            for (float t = 0f; t < TsunamiWaveTrain.TotalSeconds; t += 5f)
            {
                for (float d = 0f; d <= 10000f; d += 100f)
                {
                    float r = Rise(d, t);
                    Assert.True(r >= 0f, "negative rise at d=" + d + " t=" + t);
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
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(float.NaN, 10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, float.NaN, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 10f, float.NaN), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(-100f, 10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, -10f, Amplitude), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RiseAt(100f, 10f, 0f), 4);
            Assert.Equal(0f, TsunamiWaveTrain.RingRadiusAt(float.NaN), 4);
        }
    }
}
