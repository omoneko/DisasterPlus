using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// 所有者の指摘（2026-08-22）「まだ煙のようなものが見えるんですが、
    /// MissileDisaster のキノコ雲のエフェクトに使っている白い雲を
    /// 上空の方で渦上に表示させられますか？」への作り直し。
    ///
    /// ★★ ここが固定するのは<b>穴が空かないこと</b>と<b>毎フレーム同じ形であること</b>の
    ///    2 つである。どちらも <c>tools/TyphoonPreview</c> で外して学んだ。
    /// </summary>
    public class VortexPuffCrowdTests
    {
        private static CrowdPuff[] Build()
        {
            var into = new CrowdPuff[VortexPuffCrowd.TotalCount];
            int n = VortexPuffCrowd.Build(into);
            Assert.Equal(VortexPuffCrowd.TotalCount, n);
            return into;
        }

        [Fact]
        public void TheCrowdIsFarBiggerThanTheDiscsItCameFrom()
        {
            // 88 個をそのまま置いた最初の版は「渦ではなく点々」だった
            // （粒の面積 ÷ 渦の面積 = 0.41）。
            Assert.True(VortexPuffCrowd.TotalCount > VortexPuffLayout.PuffCount * 5,
                        VortexPuffCrowd.TotalCount + " puffs is not enough to fill "
                        + VortexPuffLayout.PuffCount + " discs");
        }

        [Fact]
        public void ThePuffsCoverTheVortexWithoutHoles()
        {
            CrowdPuff[] crowd = Build();

            // 渦の外周（半径の比 1）に対する、粒の面積の合計。
            // **1 を割ると穴が空く**（プレビューで実際に空いた）。
            float area = 0f;
            foreach (CrowdPuff p in crowd)
            {
                area += 3.14159265f * p.SizeFraction * p.SizeFraction;
            }

            float ratio = area / 3.14159265f;
            Assert.True(ratio > 1.2f, "coverage was only " + ratio);

            // 上も見る。10 倍も覆うと、腕の形が消えて 1 枚の綿になる（2 版目がそれ）。
            Assert.True(ratio < 6f, "coverage was " + ratio + "; the arms will not read");
        }

        [Fact]
        public void EveryPuffIsACloudSizedTurretNotABlobTheSizeOfTheArm()
        {
            // ★ 大きさを円盤に比例させると、外周だけ 1.8 km の塊になった（2 版目）。
            CrowdPuff[] crowd = Build();

            float smallest = float.MaxValue;
            float largest = 0f;
            foreach (CrowdPuff p in crowd)
            {
                if (p.SizeFraction < smallest) smallest = p.SizeFraction;
                if (p.SizeFraction > largest) largest = p.SizeFraction;
            }

            Assert.True(largest < 0.12f, "the biggest puff was " + largest + " of the vortex");
            Assert.True(smallest > 0.01f, "the smallest puff was " + smallest);
            Assert.True(largest / smallest < 4f,
                        "the sizes span " + (largest / smallest) + "x; clouds are not that uneven");
        }

        [Fact]
        public void TheEyeStaysOpen()
        {
            // 眼が埋まると台風に見えない。**いちばん内側の粒でも眼の外**であること。
            CrowdPuff[] crowd = Build();

            int inside = 0;
            foreach (CrowdPuff p in crowd)
            {
                if (p.RadiusFraction < VortexPuffLayout.EyeFraction * 0.5f) inside++;
            }

            Assert.True(inside < VortexPuffCrowd.TotalCount / 40,
                        inside + " puffs landed deep inside the eye");
        }

        [Fact]
        public void EverythingStaysInsideTheVortexAndTheCloudDeck()
        {
            CrowdPuff[] crowd = Build();

            foreach (CrowdPuff p in crowd)
            {
                Assert.False(float.IsNaN(p.RadiusFraction));
                Assert.False(float.IsNaN(p.AngleRadians));

                // 腕は外周より少しだけ出てよいが、桁で外れてはいけない。
                Assert.InRange(p.RadiusFraction, 0f, 1.35f);
                Assert.InRange(p.HeightFraction, 0f, 1.35f);
                Assert.InRange(p.DensityFraction, 0f, 1f);
            }
        }

        [Fact]
        public void TheSameCrowdComesBackEveryTime()
        {
            // ★★ フレーム番号を混ぜていないことの担保。混ぜると砂嵐になる。
            CrowdPuff[] a = Build();
            CrowdPuff[] b = Build();

            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal(a[i].AngleRadians, b[i].AngleRadians, 5);
                Assert.Equal(a[i].RadiusFraction, b[i].RadiusFraction, 5);
                Assert.Equal(a[i].HeightFraction, b[i].HeightFraction, 5);
                Assert.Equal(a[i].SizeFraction, b[i].SizeFraction, 5);
            }
        }

        [Fact]
        public void AllThreeLayersAreRepresented()
        {
            // 甲板だけ・傘だけになると、雲の厚みが消える。
            CrowdPuff[] crowd = Build();

            var seen = new bool[3];
            foreach (CrowdPuff p in crowd) seen[(int)p.Layer] = true;

            Assert.True(seen[0] && seen[1] && seen[2], "a whole layer is missing from the crowd");
        }

        [Fact]
        public void ATooSmallBufferDrawsNothingInsteadOfOverflowing()
        {
            Assert.Equal(0, VortexPuffCrowd.Build(null));
            Assert.Equal(0, VortexPuffCrowd.Build(new CrowdPuff[VortexPuffCrowd.TotalCount - 1]));
        }
    }
}
