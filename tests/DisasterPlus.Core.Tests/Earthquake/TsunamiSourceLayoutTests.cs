using System;
using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// 2026-08-29、実機報告「津波がやはり高さと波の継続力がとても弱いです」の
    /// 原因 ② と ③ に対する再発防止。
    ///
    /// <list type="number">
    /// <item>水源の作用直径より格子の間隔が広く、<b>隣どうしが一度も繋がらなかった</b></item>
    /// <item>撒いた水源が前線についていけず、<b>途中から持ち上げる相手が居なくなった</b></item>
    /// </list>
    /// </summary>
    public class TsunamiSourceLayoutTests
    {
        /// <summary><c>TsunamiSurge.Rate</c> と同じ値。**変えたらここも変える。**</summary>
        private const uint Rate = 900000u;

        /// <summary><c>TsunamiSurge.MaxSources</c> と同じ値。</summary>
        private const int MaxSources = 240;

        // ── 作用半径（IL 実測の式）─────────────────────────────────

        [Fact]
        public void TheReachMatchesTheGamesOwnFormula()
        {
            // IL: r = Sqrt(rate) * 0.4 + 10
            Assert.Equal(210f, TsunamiSourceLayout.RadiusForRate(250000u), 1);
            Assert.Equal(389.5f, TsunamiSourceLayout.RadiusForRate(900000u), 1);
        }

        [Fact]
        public void TheRateAndTheReachAreInverses()
        {
            for (float r = 60f; r <= 800f; r += 40f)
            {
                uint rate = TsunamiSourceLayout.RateForRadius(r);
                Assert.Equal(r, TsunamiSourceLayout.RadiusForRate(rate), 0);
            }

            Assert.Equal(0u, TsunamiSourceLayout.RateForRadius(10f));
            Assert.Equal(0u, TsunamiSourceLayout.RateForRadius(float.NaN));
        }

        // ── ★★ 原因 ②: 隣と繋がっていなかった ────────────────────────

        [Fact]
        public void NeighbouringSourcesAlwaysOverlap()
        {
            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            float step = TsunamiSourceLayout.StepFor(radius);

            Assert.True(step < radius * 2f,
                        "neighbours " + step + " m apart cannot overlap discs of radius "
                        + radius + " m — the raised sea would be isolated puddles, "
                        + "which is exactly the bug this replaced");
        }

        [Fact]
        public void TheOldSettingWouldHaveLeftAGapBetweenSources()
        {
            // ★ 直したことの記録。旧値は rate 250,000（半径 210 m）を 480 m 間隔で
            //   並べていた。直径 420 m < 480 m ＝ **60 m の隙間**。
            float old = TsunamiSourceLayout.RadiusForRate(250000u);

            Assert.True(old * 2f < 480f,
                        "the old layout is supposed to be the broken one");
            Assert.True(TsunamiSourceLayout.StepFor(
                            TsunamiSourceLayout.RadiusForRate(Rate)) < 480f * 2f);
        }

        // ── ★★ 原因 ③: 前線についていけなかった ───────────────────────

        [Fact]
        public void TheBudgetCoversTheRingEvenAtTheFarthestReach()
        {
            // いちばん苦しいのは、環が届く限界に居て帯が全幅ある瞬間。
            float outer = TsunamiWaveTrain.ReachEdgeMetres;
            float inner = outer - TsunamiWaveTrain.RingWidthMetres;

            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            float step = TsunamiSourceLayout.StepFor(radius);

            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(inner, outer, step, 0f, xz, MaxSources);

            // いちばん外の輪が**丸ごと**入っていること。ここが欠けると
            // 前線に穴が空いて、輪ではなく点線になる。
            int onCrest = 0;
            for (int i = 0; i < used; i++)
            {
                float d = Hypot(xz[i * 2], xz[i * 2 + 1]);
                if (Math.Abs(d - outer) < 1f) onCrest++;
            }

            int needed = (int)Math.Ceiling(6.2831853 * outer / step);
            Assert.True(onCrest >= needed,
                        "only " + onCrest + " of the " + needed
                        + " sources the crest needs at " + outer + " m fit in the budget");
        }

        [Fact]
        public void SourcesOnTheCrestAreCloseEnoughToTouch()
        {
            float outer = TsunamiWaveTrain.ReachEdgeMetres;
            float radius = TsunamiSourceLayout.RadiusForRate(Rate);
            float step = TsunamiSourceLayout.StepFor(radius);

            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(outer - TsunamiWaveTrain.RingWidthMetres,
                                                outer, step, 0f, xz, MaxSources);

            int count = 0;
            for (int i = 0; i < used; i++)
            {
                if (Math.Abs(Hypot(xz[i * 2], xz[i * 2 + 1]) - outer) < 1f) count++;
            }

            // 円周上の隣り合う 2 点の弦の長さ。
            float chord = 2f * outer * (float)Math.Sin(Math.PI / count);
            Assert.True(chord < radius * 2f,
                        "neighbours on the crest are " + chord + " m apart but each source "
                        + "only reaches " + radius + " m");
        }

        // ── 配置そのもの ──────────────────────────────────────

        [Fact]
        public void EveryPointLandsInsideTheBand()
        {
            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(1700f, 2600f, 660f, 0.4f, xz, MaxSources);

            Assert.True(used > 0);

            for (int i = 0; i < used; i++)
            {
                float d = Hypot(xz[i * 2], xz[i * 2 + 1]);
                Assert.InRange(d, 1700f - 1f, 2600f + 1f);
            }
        }

        [Fact]
        public void TheOutermostRingComesFirstSoTheBudgetNeverEatsTheCrest()
        {
            // ★★ 削るなら内側。見えているのは前線である。
            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(0f, 8200f, 660f, 0f, xz, 40);

            Assert.Equal(40, used);
            Assert.Equal(8200f, Hypot(xz[0], xz[1]), 0);
        }

        [Fact]
        public void ADiscIsFilledFromTheCentreOutwards()
        {
            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(0f, 2600f, 660f, 0f, xz, MaxSources);

            Assert.True(used > 4);

            bool sawCentre = false;
            for (int i = 0; i < used; i++)
            {
                if (Hypot(xz[i * 2], xz[i * 2 + 1]) < 1f) sawCentre = true;
            }

            Assert.True(sawCentre, "nothing was placed at the epicentre of a filled disc");
        }

        [Fact]
        public void TheBudgetIsNeverExceeded()
        {
            var xz = new float[MaxSources * 2];

            Assert.True(TsunamiSourceLayout.Fill(0f, 8200f, 100f, 0f, xz, MaxSources)
                        <= MaxSources);
            Assert.Equal(0, TsunamiSourceLayout.Fill(0f, 8200f, 660f, 0f, xz, 0));
            Assert.Equal(0, TsunamiSourceLayout.Fill(0f, 8200f, 660f, 0f, null, 10));

            // 配列が足りないときは配列に合わせる（はみ出して書かない）。
            var small = new float[6];
            Assert.True(TsunamiSourceLayout.Fill(0f, 8200f, 660f, 0f, small, MaxSources) <= 3);
        }

        [Fact]
        public void BrokenInputPlacesNothing()
        {
            var xz = new float[MaxSources * 2];

            Assert.Equal(0, TsunamiSourceLayout.Fill(0f, float.NaN, 660f, 0f, xz, 10));
            Assert.Equal(0, TsunamiSourceLayout.Fill(0f, 2600f, float.NaN, 0f, xz, 10));
            Assert.Equal(0, TsunamiSourceLayout.Fill(0f, 2600f, 0f, 0f, xz, 10));
            Assert.Equal(0, TsunamiSourceLayout.Fill(3000f, 2600f, 660f, 0f, xz, 10));
        }

        // ── 後始末の配置 ─────────────────────────────────────

        [Fact]
        public void TheDrainLayoutSpreadsOverTheWholeReach()
        {
            float step = TsunamiSourceLayout.DrainStepFor(
                TsunamiWaveTrain.ReachEdgeMetres, MaxSources);

            var xz = new float[MaxSources * 2];
            int used = TsunamiSourceLayout.Fill(0f, TsunamiWaveTrain.ReachEdgeMetres,
                                                step, 0f, xz, MaxSources);

            // 予算いっぱいまで使い、いちばん外まで届いていること。
            Assert.True(used > MaxSources / 2, "only " + used + " sources drain the reach");

            float far = 0f;
            for (int i = 0; i < used; i++)
            {
                float d = Hypot(xz[i * 2], xz[i * 2 + 1]);
                if (d > far) far = d;
            }

            Assert.Equal(TsunamiWaveTrain.ReachEdgeMetres, far, 0);
        }

        [Fact]
        public void TheDrainStepIsSaneForBrokenInput()
        {
            Assert.True(TsunamiSourceLayout.DrainStepFor(0f, 10) > 0f);
            Assert.True(TsunamiSourceLayout.DrainStepFor(8200f, 0) > 0f);
            Assert.True(TsunamiSourceLayout.DrainStepFor(float.NaN, 10) > 0f);
        }

        private static float Hypot(float x, float z)
        {
            return (float)Math.Sqrt(x * x + z * z);
        }
    }
}
