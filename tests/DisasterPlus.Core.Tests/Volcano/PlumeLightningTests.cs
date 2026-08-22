using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 所有者の依頼「噴煙の中で雷（噴石同士が当たって生じるやつ）が発生するのを再現」。
    ///
    /// ★★ 火山雷は**柱の中で完結する**（地面へは落ちない）。
    ///    そして <c>VolcanicTremor</c> と同じく**状態を 1 つも持たない**ので、
    ///    強さが変わっても「もう光っている閃光」が消えてはいけない。
    /// </summary>
    public class PlumeLightningTests
    {
        private const uint Seed = 0x5EED1234u;
        private const float Height = 1200f;

        /// <summary>柱の形の代用。中ほどがいちばん太い。</summary>
        private static float Radius(float t)
        {
            return 40f + 160f * (t < 0.8f ? t / 0.8f : 1f);
        }

        [Fact]
        public void EverySlotHoldsExactlyOneFlashAndItEnds()
        {
            // 枠ごとに 1 本。**光り続けない。**
            for (int slot = 0; slot < 20; slot++)
            {
                float start = PlumeLightning.StartOf(Seed, slot);

                Assert.True(PlumeLightning.BrightnessAt(Seed, slot, start + 0.001f, 1f) > 0f);
                Assert.Equal(0f, PlumeLightning.BrightnessAt(Seed, slot, start - 0.01f, 1f), 5);
                Assert.Equal(0f, PlumeLightning.BrightnessAt(
                    Seed, slot, start + PlumeLightning.FlashSeconds, 1f), 5);
            }
        }

        [Fact]
        public void AFlashStaysInsideItsOwnSlot()
        {
            // はみ出すと、さかのぼる枠の数（MaxBolts）では拾いきれない本が出る。
            for (int slot = 0; slot < 50; slot++)
            {
                float start = PlumeLightning.StartOf(Seed, slot);
                float slotStart = slot * PlumeLightning.SlotSeconds;
                float slotEnd = slotStart + PlumeLightning.SlotSeconds;

                Assert.InRange(start, slotStart, slotEnd - PlumeLightning.FlashSeconds + 1e-4f);
            }
        }

        [Fact]
        public void ChangingTheStrengthDoesNotEraseAFlashThatIsAlreadyLit()
        {
            // ★ ここが VolcanicTremor から引き継いだ規律である。
            //   強さは明るさだけを決め、起きる／起きないは決めない。
            for (int slot = 0; slot < 20; slot++)
            {
                float t = PlumeLightning.StartOf(Seed, slot) + 0.05f;

                Assert.True(PlumeLightning.BrightnessAt(Seed, slot, t, 1.00f) > 0f);
                Assert.True(PlumeLightning.BrightnessAt(Seed, slot, t, 0.05f) > 0f);
            }
        }

        [Fact]
        public void NothingLightsUpWhenTheEruptionIsOver()
        {
            float t = PlumeLightning.StartOf(Seed, 3) + 0.05f;
            Assert.Equal(0f, PlumeLightning.BrightnessAt(Seed, 3, t, 0f), 5);
        }

        [Fact]
        public void TheBoltStaysInsideThePlume()
        {
            var points = new LightningPoint[PlumeLightning.PointCount];

            for (int slot = 0; slot < 60; slot++)
            {
                int n = PlumeLightning.PathInto(points, Seed, slot, Height, Radius);
                Assert.Equal(PlumeLightning.PointCount, n);

                for (int i = 0; i < n; i++)
                {
                    LightningPoint p = points[i];

                    // 高さは柱の中。**地面へは落ちない。**
                    Assert.InRange(p.Y, Height * PlumeLightning.LowFraction - 1e-3f,
                                   Height * PlumeLightning.HighFraction + 1e-3f);

                    // 横方向はその高さの柱の半径の中。
                    float r = Radius(p.Y / Height);
                    float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);
                    Assert.True(d <= r + 1e-3f, "bolt left the column: " + d + " > " + r);
                }
            }
        }

        [Fact]
        public void TheBoltIsNotAStraightLine()
        {
            // 真っ直ぐだと雷に見えない（PointCount の doc）。
            var points = new LightningPoint[PlumeLightning.PointCount];
            PlumeLightning.PathInto(points, Seed, 5, Height, Radius);

            float spread = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                float d = (float)System.Math.Sqrt(points[i].X * points[i].X
                                                  + points[i].Z * points[i].Z);
                if (d > spread) spread = d;
            }

            Assert.True(spread > 1f, "the bolt was straight (spread " + spread + ")");
        }

        [Fact]
        public void TheSameVolcanoAlwaysGetsTheSameBolts()
        {
            var a = new LightningPoint[PlumeLightning.PointCount];
            var b = new LightningPoint[PlumeLightning.PointCount];

            PlumeLightning.PathInto(a, Seed, 9, Height, Radius);
            PlumeLightning.PathInto(b, Seed, 9, Height, Radius);

            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal(a[i].X, b[i].X, 5);
                Assert.Equal(a[i].Y, b[i].Y, 5);
                Assert.Equal(a[i].Z, b[i].Z, 5);
            }
        }

        [Fact]
        public void BrokenInputDrawsNothingInsteadOfThrowing()
        {
            var points = new LightningPoint[PlumeLightning.PointCount];

            Assert.Equal(0, PlumeLightning.PathInto(null, Seed, 1, Height, Radius));
            Assert.Equal(0, PlumeLightning.PathInto(new LightningPoint[2], Seed, 1, Height, Radius));
            Assert.Equal(0, PlumeLightning.PathInto(points, Seed, -1, Height, Radius));
            Assert.Equal(0, PlumeLightning.PathInto(points, Seed, 1, 0f, Radius));
            Assert.Equal(0, PlumeLightning.PathInto(points, Seed, 1, Height, null));
        }
    }
}
