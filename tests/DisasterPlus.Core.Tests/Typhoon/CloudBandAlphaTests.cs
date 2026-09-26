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
            // Unless this is 0, the edge of the mesh shows through and it goes back to being
            // a hard-edged band (we started using UVs for precisely this, so it is the
            // reason this type exists).
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
            // The promise **not to make the cloud any denser or any thinner**. With 255 in
            // the middle, the cloud's opacity is still decided solely by the alpha of the
            // material's tint, and the texture does nothing but fade the edges.
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
            // It must grow denser monotonically from the edge towards the middle. A peak or
            // a trough on the way makes a line visible inside the ribbon (along v it should
            // be a single gradient).
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
            // Writing part of the way gives "only one edge is hard", the hardest form of all
            // to investigate.
            var a = new byte[CloudBandAlpha.Size];
            CloudBandAlpha.Build(a);
            for (int i = 0; i < a.Length; i++) Assert.Equal(0, a[i]);

            CloudBandAlpha.Build(null);   // it must not fall over
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
