using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    /// <summary>
    /// A rebuild in answer to the owner's report (2026-08-22): "I can still see something
    /// like smoke —— could you display the white cloud used for the mushroom-cloud effect in
    /// MissileDisaster up high, arranged as a vortex?".
    ///
    /// ★★ What is pinned down here are two things: <b>that no holes open up</b> and
    ///    <b>that the shape is the same every frame</b>. We learned both by getting them
    ///    wrong in <c>tools/TyphoonPreview</c>.
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
            // The first version, which simply placed the 88, gave "dots rather than a vortex"
            // (puff area ÷ vortex area = 0.41).
            Assert.True(VortexPuffCrowd.TotalCount > VortexPuffLayout.PuffCount * 5,
                        VortexPuffCrowd.TotalCount + " puffs is not enough to fill "
                        + VortexPuffLayout.PuffCount + " discs");
        }

        [Fact]
        public void ThePuffsCoverTheVortexWithoutHoles()
        {
            CrowdPuff[] crowd = Build();

            // The total puff area against the vortex's outer circle (radius ratio 1).
            // **Below 1 holes open up** (they actually did in the preview).
            float area = 0f;
            foreach (CrowdPuff p in crowd)
            {
                area += 3.14159265f * p.SizeFraction * p.SizeFraction;
            }

            float ratio = area / 3.14159265f;
            Assert.True(ratio > 1.2f, "coverage was only " + ratio);

            // Check the upper end too. Cover it ten times over and the shape of the arms
            // disappears into a single sheet of wadding (that was the second version).
            Assert.True(ratio < 6f, "coverage was " + ratio + "; the arms will not read");
        }

        [Fact]
        public void EveryPuffIsACloudSizedTurretNotABlobTheSizeOfTheArm()
        {
            // ★ Making the size proportional to the disc turned just the outer ring into
            //   1.8 km blobs (the second version).
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
            // Fill in the eye and it stops looking like a typhoon. **Even the innermost puff
            // must be outside the eye.**
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

                // The arms may reach a little past the outer circle, but never by an order
                // of magnitude.
                Assert.InRange(p.RadiusFraction, 0f, 1.35f);
                Assert.InRange(p.HeightFraction, 0f, 1.35f);
                Assert.InRange(p.DensityFraction, 0f, 1f);
            }
        }

        [Fact]
        public void TheSameCrowdComesBackEveryTime()
        {
            // ★★ The guarantee that the frame number is not mixed in. Mix it in and you get
            //    television static.
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
            // Reduce it to the deck alone or the canopy alone and the cloud loses its depth.
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
