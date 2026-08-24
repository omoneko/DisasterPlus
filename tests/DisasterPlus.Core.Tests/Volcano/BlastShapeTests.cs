using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 実機報告（2026-08-22）「火山の爆発について、エフェクトが平面的なのと
    /// 場所が少しずれて見えます」。
    ///
    /// ★★ 原因は 2 つとも IL で確かめてある:
    ///
    ///   1. <b>平面的</b> —— 3 引数の <c>EffectInfo.SpawnArea(pos, dir, radius)</c> は
    ///      <c>m_halfHeight = 0</c> を書く（IL_006F-0075）。粒は
    ///      「円盤 ＋ 上×[0, halfHeight)」に湧くので、**厚みゼロの円盤**になる。
    ///   2. <b>ずれ</b> —— 発の散らばりが火口より広かった（横 1.15 倍・縦 1.9 倍）。
    /// </summary>
    public class BlastShapeTests
    {
        private const float Crater = 351f;
        private const uint Seed = 991u;

        [Fact]
        public void EveryBurstHasRealVerticalExtent()
        {
            // ★★ **0 を渡したら平面に戻る。** ここが 0 を許すと、また
            //    3 引数の SpawnArea と同じ絵になる。
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);
                Assert.True(b.HalfHeightMetres > 0f, "burst " + i + " is a flat disc");
                Assert.True(b.HalfHeightMetres >= b.RadiusMetres,
                            "burst " + i + " is flatter than it is wide ("
                            + b.HalfHeightMetres + " vs " + b.RadiusMetres + ")");
            }
        }

        [Fact]
        public void TheBurstsStayInsideTheCrater()
        {
            // 火口の外まで散ると、1 つの大きい爆発ではなく
            // 「火口のまわりでばらばらに弾けている」ように見える。
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);

                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX
                                                  + b.OffsetZ * b.OffsetZ);
                Assert.True(d <= Crater,
                            "burst " + i + " landed " + d + " m out, past the crater rim at "
                            + Crater);
            }
        }

        [Fact]
        public void TheBurstsStayNearTheVentVertically()
        {
            // 爆発は火口で起きる。噴煙柱の高さまで浮かせない。
            int count = BlastCluster.CountFor(1f, 1f, false);
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, false, Crater, 0f, Seed);
                Assert.True(b.OffsetY <= Crater,
                            "burst " + i + " floated " + b.OffsetY + " m above the vent");
                Assert.True(b.OffsetY >= 0f);
            }
        }

        [Fact]
        public void TheRingFissureIsStillAllowedToReachOutFar()
        {
            // ★ 火口に収めるのは**中央の噴火だけ**である。破局噴火の環状火口列は
            //   カルデラのふちまで届かなければならない（BlastCluster のクラス doc）。
            const float ring = 5563f;
            int count = BlastCluster.CountFor(1f, 1f, true);

            float furthest = 0f;
            for (int i = 0; i < count; i++)
            {
                BlastBurst b = BlastCluster.For(i, count, 1f, 1f, true, Crater, ring, Seed);
                float d = (float)System.Math.Sqrt(b.OffsetX * b.OffsetX
                                                  + b.OffsetZ * b.OffsetZ);
                if (d > furthest) furthest = d;
            }

            Assert.True(furthest > ring * 0.8f,
                        "the ring bursts only reached " + furthest + " m of " + ring);
        }
    }
}
