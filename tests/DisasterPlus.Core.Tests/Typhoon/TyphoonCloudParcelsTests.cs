using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 所有者の指示（2026-08-22）「台風の雲のエフェクトは一瞬だけ現れて消えて
    /// しまいます。火山の噴火の雲が質感としてはふさわしいので、噴火雲エフェクトを
    /// 応用して台風の雲を作ってください」。
    ///
    /// ★★ ここが固定するのは<b>台風に見えること</b>である ——
    ///    目が空いていること、腕があること、そして<b>止まらないこと</b>。
    /// </summary>
    public class TyphoonCloudParcelsTests
    {
        private const float Radius = 4200f;
        private const uint Seed = 20260822u;

        private static TyphoonParcel P(int i, float t)
        {
            return TyphoonCloudParcels.At(i, t, Radius, 0f, Seed);
        }

        [Fact]
        public void NoParcelEverIntrudesIntoTheEye()
        {
            // ★★ **塊の内側の縁**で見ること。中心までの距離だけ見ていたとき、
            //    壁雲の塊が目の中まではみ出していた（被覆 100%）。
            float eye = TyphoonCloudParcels.EyeFraction * Radius;

            for (float t = 0f; t < TyphoonCloudParcels.LifeSeconds; t += 3f)
            {
                for (int i = 0; i < TyphoonCloudParcels.Count; i += 7)
                {
                    TyphoonParcel p = P(i, t);
                    if (p.Alpha <= 0.02f) continue;

                    float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);
                    Assert.True(d + 1f >= eye,
                                "a parcel sat " + d + " m out, inside the eye at " + eye);
                }
            }
        }

        [Fact]
        public void TheCloudIsBandedNotAUniformRing()
        {
            // ★★ 腕が無いと、質感がどれだけ良くても**ただの丸い塊**に見える。
            //    同じ半径の帯を角度で刻んで、密度に偏りがあることを見る。
            const int Sectors = 36;
            var count = new int[Sectors];

            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                TyphoonParcel p = P(i, 130f);
                if (p.Alpha <= 0.05f) continue;

                float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);
                if (d < Radius * 0.35f || d > Radius * 0.8f) continue;

                double a = System.Math.Atan2(p.Z, p.X);
                if (a < 0) a += 2 * System.Math.PI;
                int s = (int)(a / (2 * System.Math.PI) * Sectors);
                if (s >= Sectors) s = Sectors - 1;
                count[s]++;
            }

            int total = 0, peak = 0, trough = int.MaxValue;
            for (int i = 0; i < Sectors; i++)
            {
                total += count[i];
                if (count[i] > peak) peak = count[i];
                if (count[i] < trough) trough = count[i];
            }

            Assert.True(total > 60, "too few parcels in the band to judge (" + total + ")");

            float mean = total / (float)Sectors;
            Assert.True(peak > mean * 1.6f,
                        "the cloud is a uniform ring: peak " + peak + " vs mean " + mean);
        }

        [Fact]
        public void EveryParcelKeepsMovingSoTheCloudNeverFreezes()
        {
            // ★★ これが「一瞬だけ現れて消える」の反対側の検査である。
            //    以前は添字だけで決まる静止した並びだった。
            int still = 0;
            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                TyphoonParcel a = P(i, 100f);
                TyphoonParcel b = P(i, 100f + 1f / 30f);
                if (a.X == b.X && a.Y == b.Y && a.Z == b.Z) still++;
            }

            Assert.Equal(0, still);
        }

        [Fact]
        public void EveryParcelFadesInAndOut()
        {
            for (int i = 0; i < 40; i++)
            {
                float max = 0f, min = 1f;
                for (int k = 0; k <= 80; k++)
                {
                    float a = P(i, k * TyphoonCloudParcels.LifeSeconds / 80f).Alpha;
                    if (a > max) max = a;
                    if (a < min) min = a;
                }

                Assert.True(max > 0.3f, "parcel " + i + " is never visible");
                Assert.True(min < 0.1f, "parcel " + i + " never fades out");
            }
        }

        [Fact]
        public void NothingEscapesTheStormRadius()
        {
            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                TyphoonParcel p = P(i, 130f);
                float d = (float)System.Math.Sqrt(p.X * p.X + p.Z * p.Z);

                // 塊の半径と乱れのぶんだけは外へ出てよい。
                Assert.True(d < Radius * 1.35f,
                            "a parcel reached " + d + " m, well past the storm at " + Radius);
            }
        }

        [Fact]
        public void TheSameTyphoonAlwaysLooksTheSame()
        {
            for (int i = 0; i < 50; i++)
            {
                TyphoonParcel a = P(i, 77.5f);
                TyphoonParcel b = P(i, 77.5f);
                Assert.Equal(a.X, b.X, 5);
                Assert.Equal(a.Z, b.Z, 5);
            }
        }

        [Fact]
        public void BrokenInputStillProducesADrawableParcel()
        {
            foreach (float t in new[] { float.NaN, -20f, 0f })
            {
                foreach (float r in new[] { float.NaN, -5f, 0f, 4200f })
                {
                    TyphoonParcel p = TyphoonCloudParcels.At(0, t, r, float.NaN, Seed);

                    Assert.False(float.IsNaN(p.X));
                    Assert.False(float.IsNaN(p.Y));
                    Assert.False(float.IsNaN(p.Z));
                    Assert.True(p.RadiusMetres > 0f);
                    Assert.InRange(p.Alpha, 0f, 1f);
                    Assert.InRange(p.Brightness, 0f, 1f);
                }
            }
        }

        [Fact]
        public void AnOutOfRangeIndexStillReturnsAParcel()
        {
            TyphoonParcel p = TyphoonCloudParcels.At(999999, 100f, Radius, 0f, Seed);
            Assert.True(p.RadiusMetres > 0f);
        }
    }
}
