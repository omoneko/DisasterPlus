using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 実機報告（2026-08-22）「台風の雲のエフェクトは一瞬だけ現れて消えてしまいます
    /// …可能な限り上空を巨大な台風雲がゆっくりと回転して通過しながら…」。
    ///
    /// ★★ <b>原因は方位が出発点と無関係だったことである。</b> マップの端の近くを
    ///    指して外向きの目が出ると、台風は数百 m でマップを出て
    ///    <c>TyphoonController.Stop()</c> に掛かる —— <b>雲が一瞬出て消える。</b>
    ///    宿主のバニラの雷雨は別の寿命で動いているので、そちらだけが残る
    ///    （報告の「そこからはただの雷雨が続く」）。
    /// </summary>
    public class TyphoonBearingTests
    {
        /// <summary>ThunderStormAI の実測値。</summary>
        private const uint HostDuration = 8192u;

        private static float Speed
        {
            get { return TyphoonTrack.SpeedFor(TyphoonTrack.LifetimeFramesFor(HostDuration)); }
        }

        private static uint Lifetime
        {
            get { return TyphoonTrack.LifetimeFramesFor(HostDuration); }
        }

        /// <summary>マップの端ぎわの地点をいくつか。**いちばん壊れやすい置き方である。**</summary>
        private static Vec2[] EdgePoints()
        {
            const float e = TyphoonTrack.MapHalfExtent * 0.92f;
            return new[]
            {
                new Vec2(e, 0f), new Vec2(-e, 0f), new Vec2(0f, e), new Vec2(0f, -e),
                new Vec2(e, e), new Vec2(-e, e), new Vec2(e, -e), new Vec2(-e, -e),
            };
        }

        [Fact]
        public void ATyphoonPlacedAtTheEdgeHeadsInland()
        {
            // ★★ どの種でも、端に置いたら**内側へ**向かうこと。
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 40; seed++)
                {
                    float bearing = TyphoonTrack.BearingFrom(origin, seed);

                    // 進行方向の単位ベクトルと、中心へ向かう単位ベクトルの内積。
                    float dx = (float)System.Math.Cos(bearing);
                    float dz = (float)System.Math.Sin(bearing);

                    float len = (float)System.Math.Sqrt(origin.X * origin.X
                                                        + origin.Z * origin.Z);
                    float tx = -origin.X / len;
                    float tz = -origin.Z / len;

                    float dot = dx * tx + dz * tz;

                    // spread が 0.62 rad なので cos(0.62) = 0.81 より下にはならない。
                    Assert.True(dot > 0.8f,
                                "seed " + seed + " at (" + origin.X + "," + origin.Z
                                + ") heads away from the map (dot=" + dot + ")");
                }
            }
        }

        [Fact]
        public void ATyphoonPlacedAtTheEdgeStaysOverTheMapForMostOfItsLife()
        {
            // ★★ これが依頼そのものである —— 「可能な限り上空を…通過」。
            //    以前はここが 0 になる種が普通に出た。
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 20; seed++)
                {
                    int inside = 0;
                    const int Samples = 64;

                    for (int i = 0; i < Samples; i++)
                    {
                        uint elapsed = (uint)((long)Lifetime * i / Samples);
                        Vec2 centre = TyphoonTrack.CentreAt(origin, seed, elapsed, Speed);
                        if (TyphoonTrack.IsInsideMap(centre)) inside++;
                    }

                    Assert.True(inside > Samples / 2,
                                "seed " + seed + " at (" + origin.X + "," + origin.Z
                                + ") spends only " + inside + "/" + Samples
                                + " of its life over the map");
                }
            }
        }

        [Fact]
        public void APointNearTheCentreStillGetsAVariedHeading()
        {
            // ★ 中心のすぐ近くでは「中心へ向かう向き」が定義できない。
            //   種だけで決めるので、方位は散らばること。
            var origin = new Vec2(0f, 0f);
            var seen = new System.Collections.Generic.HashSet<int>();

            for (uint seed = 1; seed < 60; seed++)
            {
                seen.Add((int)(TyphoonTrack.BearingFrom(origin, seed) * 4f));
            }

            Assert.True(seen.Count > 8,
                        "every typhoon from the centre heads the same way (" + seen.Count + ")");
        }

        [Fact]
        public void TheSamePlaceAndSeedAlwaysGivesTheSameTrack()
        {
            // 同じセーブで再現できること（設計書 §4.1）。
            var origin = new Vec2(3000f, -2000f);
            for (uint seed = 1; seed < 20; seed++)
            {
                Assert.Equal(TyphoonTrack.BearingFrom(origin, seed),
                             TyphoonTrack.BearingFrom(origin, seed), 5);
            }
        }

        [Fact]
        public void TheBearingIsAlwaysAUsableAngle()
        {
            foreach (Vec2 origin in EdgePoints())
            {
                for (uint seed = 1; seed < 30; seed++)
                {
                    float b = TyphoonTrack.BearingFrom(origin, seed);
                    Assert.False(float.IsNaN(b));
                    Assert.InRange(b, 0f, 6.28318531f);
                }
            }
        }
    }
}
