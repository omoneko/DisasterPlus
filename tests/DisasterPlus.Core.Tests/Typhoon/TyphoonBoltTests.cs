using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 所有者の指示（2026-08-25）「時々台風の雲の中から稲妻を発生させる」。
    ///
    /// ★★ 固定するのは<b>雲の中に収まっていること</b>である。はみ出したら、
    ///    直そうとしていた症状（雷が雲の外で光る）に戻る。
    /// </summary>
    public class TyphoonBoltTests
    {
        private const float Radius = 4200f;
        private const float Thickness = 672f;   // Radius * ThicknessFraction
        private const uint Seed = 4242u;

        private static TyphoonBoltPoint[] Buffer()
        {
            return new TyphoonBoltPoint[TyphoonBolt.PointCount];
        }

        [Fact]
        public void EveryBoltStaysInsideTheCloud()
        {
            var path = Buffer();

            for (int slot = 0; slot < 400; slot++)
            {
                int n = TyphoonBolt.PathInto(path, Seed, slot, Radius, Thickness);
                if (n == 0) continue;

                for (int i = 0; i < n; i++)
                {
                    float d = (float)System.Math.Sqrt(path[i].X * path[i].X
                                                      + path[i].Z * path[i].Z);

                    // ★ 渦の外へ出ない。
                    Assert.True(d <= Radius,
                                "slot " + slot + " point " + i + " reached " + d
                                + " m, outside the storm at " + Radius);

                    // ★★ **雲の厚みから出ない。** ここが破れると雷が雲の上か下で光る。
                    Assert.InRange(path[i].Y, 0f, Thickness);
                }
            }
        }

        [Fact]
        public void NoBoltStrikesInsideTheEye()
        {
            // 目は晴れている。そこで光ったら台風に見えない。
            var path = Buffer();
            float eye = TyphoonCloudParcels.EyeFraction * Radius;

            for (int slot = 0; slot < 400; slot++)
            {
                int n = TyphoonBolt.PathInto(path, Seed, slot, Radius, Thickness);
                if (n == 0) continue;

                // 中心（折れ線の真ん中）が目の外にあること。
                TyphoonBoltPoint mid = path[TyphoonBolt.PointCount / 2];
                float d = (float)System.Math.Sqrt(mid.X * mid.X + mid.Z * mid.Z);

                Assert.True(d >= eye,
                            "slot " + slot + " struck " + d + " m out, inside the eye at " + eye);
            }
        }

        [Fact]
        public void TheBoltIsCrookedNotStraight()
        {
            // 2 点の直線は雷に見えない。途中の点が軸から外れていること。
            var path = Buffer();
            Assert.Equal(TyphoonBolt.PointCount,
                         TyphoonBolt.PathInto(path, Seed, 7, Radius, Thickness));

            // 端点を結ぶ線からの最大のずれ。
            TyphoonBoltPoint a = path[0];
            TyphoonBoltPoint b = path[TyphoonBolt.PointCount - 1];

            float worst = 0f;
            for (int i = 1; i < TyphoonBolt.PointCount - 1; i++)
            {
                float t = i / (float)(TyphoonBolt.PointCount - 1);
                float lx = a.X + (b.X - a.X) * t;
                float lz = a.Z + (b.Z - a.Z) * t;

                float dx = path[i].X - lx;
                float dz = path[i].Z - lz;
                float off = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (off > worst) worst = off;
            }

            Assert.True(worst > 1f, "the bolt is a straight line (worst offset " + worst + ")");
        }

        [Fact]
        public void ItFlashesSometimesNotConstantly()
        {
            // 「時々」である。全部の枠で光ると忙しない。
            int lit = 0;
            const int Slots = 300;

            for (int slot = 0; slot < Slots; slot++)
            {
                // その枠のいちばん明るくなりうる時刻を探す。
                float best = 0f;
                for (int k = 0; k <= 40; k++)
                {
                    float t = slot * TyphoonBolt.SlotSeconds
                              + TyphoonBolt.SlotSeconds * k / 40f;
                    float b = TyphoonBolt.BrightnessAt(Seed, slot, t, 1f);
                    if (b > best) best = b;
                }
                if (best > 0.02f) lit++;
            }

            float ratio = lit / (float)Slots;
            Assert.InRange(ratio, 0.2f, 0.8f);
        }

        [Fact]
        public void AWeakTyphoonFlashesLessThanAStrongOne()
        {
            int weak = 0, strong = 0;
            for (int slot = 0; slot < 300; slot++)
            {
                for (int k = 0; k <= 20; k++)
                {
                    float t = slot * TyphoonBolt.SlotSeconds
                              + TyphoonBolt.SlotSeconds * k / 20f;
                    if (TyphoonBolt.BrightnessAt(Seed, slot, t, 0.2f) > 0.02f) { weak++; break; }
                }
                for (int k = 0; k <= 20; k++)
                {
                    float t = slot * TyphoonBolt.SlotSeconds
                              + TyphoonBolt.SlotSeconds * k / 20f;
                    if (TyphoonBolt.BrightnessAt(Seed, slot, t, 1f) > 0.02f) { strong++; break; }
                }
            }

            Assert.True(strong > weak, weak + " -> " + strong);
        }

        [Fact]
        public void ABoltNeverOutlivesItsFlash()
        {
            // 光っている時間は FlashSeconds を超えないこと。
            for (int slot = 0; slot < 60; slot++)
            {
                float first = -1f, last = -1f;
                for (int k = 0; k <= 400; k++)
                {
                    float t = slot * TyphoonBolt.SlotSeconds
                              + TyphoonBolt.SlotSeconds * k / 400f;
                    if (TyphoonBolt.BrightnessAt(Seed, slot, t, 1f) > 0f)
                    {
                        if (first < 0f) first = t;
                        last = t;
                    }
                }

                if (first < 0f) continue;
                Assert.True(last - first <= TyphoonBolt.FlashSeconds + 0.02f,
                            "slot " + slot + " glowed for " + (last - first) + " s");
            }
        }

        [Fact]
        public void BrokenInputDrawsNothing()
        {
            var path = Buffer();

            Assert.Equal(0, TyphoonBolt.PathInto(path, Seed, 0, float.NaN, Thickness));
            Assert.Equal(0, TyphoonBolt.PathInto(path, Seed, 0, 0f, Thickness));
            Assert.Equal(0, TyphoonBolt.PathInto(null, Seed, 0, Radius, Thickness));

            Assert.Equal(0f, TyphoonBolt.BrightnessAt(Seed, 0, float.NaN, 1f), 4);
            Assert.Equal(0f, TyphoonBolt.BrightnessAt(Seed, -1, 1f, 1f), 4);
            Assert.Equal(0f, TyphoonBolt.BrightnessAt(Seed, 0, 1f, 0f), 4);
        }

        [Fact]
        public void AnUnreadableThicknessStillDrawsSomethingSane()
        {
            // 厚みが読めなくても、渦の半径から代わりの厚みを作って描く。
            var path = Buffer();
            int n = TyphoonBolt.PathInto(path, Seed, 3, Radius, float.NaN);

            Assert.Equal(TyphoonBolt.PointCount, n);
            for (int i = 0; i < n; i++)
            {
                Assert.False(float.IsNaN(path[i].X));
                Assert.False(float.IsNaN(path[i].Y));
                Assert.True(path[i].Y >= 0f);
            }
        }
    }
}
