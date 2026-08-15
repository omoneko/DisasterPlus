using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class CloudBandAlphaTests
    {
        private static byte[] Built()
        {
            var a = new byte[CloudBandAlpha.Size * CloudBandAlpha.Size];
            CloudBandAlpha.Build(a);
            return a;
        }

        [Fact]
        public void BothEdgesOfTheRibbonFadeToNothing()
        {
            // ここが 0 でないと、メッシュの縁がそのまま見えて硬い帯に戻る
            // （そのために UV を使い始めたのだから、これがこの型の存在理由）。
            var a = Built();
            int last = CloudBandAlpha.Size - 1;
            for (int x = 0; x < CloudBandAlpha.Size; x++)
            {
                Assert.Equal(0, a[0 * CloudBandAlpha.Size + x]);
                Assert.Equal(0, a[last * CloudBandAlpha.Size + x]);
            }
        }

        [Fact]
        public void TheCentreOfTheRibbonKeepsFullOpacity()
        {
            // **雲を濃くも薄くもしない**約束。中央が 255 なら、雲の濃さは今までどおり
            // マテリアルのティントの α だけで決まり、テクスチャは縁を落とすだけになる。
            var a = Built();
            int mid = (CloudBandAlpha.Size - 1) / 2;
            int peak = 0;
            for (int x = 0; x < CloudBandAlpha.Size; x++)
            {
                int value = a[mid * CloudBandAlpha.Size + x];
                if (value > peak) peak = value;
            }
            Assert.Equal(255, peak);
            Assert.Equal(1f, CloudBandAlpha.PeakAlpha);
        }

        [Fact]
        public void EveryTexelIsMonotonicTowardsTheCentreOfTheBand()
        {
            // 縁から中央へ向かって単調に濃くなること。途中に山や谷があると、
            // リボンの中に線が見える（v 方向は 1 本のグラデーションであるべき）。
            var a = Built();
            int mid = (CloudBandAlpha.Size - 1) / 2;
            for (int x = 0; x < CloudBandAlpha.Size; x++)
            {
                for (int y = 1; y <= mid; y++)
                {
                    Assert.True(a[y * CloudBandAlpha.Size + x]
                                >= a[(y - 1) * CloudBandAlpha.Size + x],
                        "alpha must rise towards the middle at column " + x);
                }
            }
        }

        [Fact]
        public void AShortBufferIsLeftAlone()
        {
            // 途中まで書くと「縁の片側だけ硬い」という最も調べにくい形になる。
            var a = new byte[CloudBandAlpha.Size];
            CloudBandAlpha.Build(a);
            for (int i = 0; i < a.Length; i++) Assert.Equal(0, a[i]);

            CloudBandAlpha.Build(null);   // 落ちないこと
        }

        [Fact]
        public void TheSameCallAlwaysProducesTheSameTexture()
        {
            var a = Built();
            var b = Built();
            Assert.Equal(a, b);
        }
    }
}
