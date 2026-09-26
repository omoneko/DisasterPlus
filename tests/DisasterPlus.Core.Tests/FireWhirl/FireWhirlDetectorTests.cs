using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using Xunit;

namespace DisasterPlus.Core.Tests.FireWhirl
{
    public class FireWhirlDetectorTests
    {
        private static FireWhirlConfig Config(float radius = 150f, int count = 12, float sep = 300f)
        {
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = radius;
            c.DetectCount = count;
            c.MinSeparation = sep;
            return c;
        }

        /// <summary>Lay out n buildings evenly spaced within a circle of radius r.</summary>
        private static List<BurningBuilding> Cluster(int n, Vec2 center, float r, ushort startId)
        {
            var list = new List<BurningBuilding>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * System.Math.PI * i / n;
                var p = new Vec2(
                    center.X + (float)System.Math.Cos(a) * r,
                    center.Z + (float)System.Math.Sin(a) * r);
                list.Add(new BurningBuilding((ushort)(startId + i), p));
            }
            return list;
        }

        [Fact]
        public void Detect_DenseCluster_ProducesCandidate()
        {
            // 12 buildings on a circle of radius 40 m → seen from any of them there are
            // 12 buildings within R=150 m
            var burning = Cluster(12, new Vec2(1000f, 1000f), 40f, 1);
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Single(result);
            Assert.Equal(1000f, result[0].Center.X, 0);
            Assert.Equal(1000f, result[0].Center.Z, 0);
            Assert.Equal(12, result[0].BurningCount);
        }

        [Fact]
        public void Detect_ScatteredBuildings_ProducesNothing()
        {
            // The same 12 buildings do not trigger anything if they are scattered 2 km apart
            var burning = new List<BurningBuilding>();
            for (ushort i = 0; i < 12; i++)
                burning.Add(new BurningBuilding((ushort)(i + 1), new Vec2(i * 2000f, 0f)));

            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Empty(result);
        }

        [Fact]
        public void Detect_OneBelowThreshold_ProducesNothing()
        {
            var burning = Cluster(11, new Vec2(500f, 500f), 40f, 1);
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_ExactlyAtThreshold_ProducesCandidate()
        {
            var burning = Cluster(12, new Vec2(500f, 500f), 40f, 1);
            Assert.Single(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_JustOutsideRadius_DoesNotCount()
        {
            // One building at the centre, 11 buildings just beyond R. Seen from the
            // centre building, the only neighbour is itself.
            var burning = new List<BurningBuilding> { new BurningBuilding(1, new Vec2(0f, 0f)) };
            var far = Cluster(11, new Vec2(0f, 0f), 150.5f, 2);
            burning.AddRange(far);
            // The 11 on the ring are close to one another, so they form an 11-building
            // cluster there, but that falls short of the threshold of 12
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(count: 12), new List<Vec2>()));
        }

        [Fact]
        public void Detect_TwoDistantClusters_ProducesTwoCandidates()
        {
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            burning.AddRange(Cluster(12, new Vec2(3000f, 0f), 40f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Detect_NearbyCandidates_AreMergedByMinSeparation()
        {
            // Two clusters 250 m apart. MinSeparation = 300 m, so they merge into one.
            var burning = Cluster(12, new Vec2(0f, 0f), 30f, 1);
            burning.AddRange(Cluster(12, new Vec2(250f, 0f), 30f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(sep: 300f), new List<Vec2>());
            Assert.Single(result);
        }

        [Fact]
        public void Detect_SuppressedNearExistingWhirl()
        {
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            var existing = new List<Vec2> { new Vec2(100f, 0f) };  // within MinSeparation=300m
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(sep: 300f), existing));
        }

        [Fact]
        public void Detect_StrongestClusterWins_WhenMerged()
        {
            // When they merge, the side with more burning buildings must be the one left
            var burning = Cluster(12, new Vec2(0f, 0f), 30f, 1);
            burning.AddRange(Cluster(20, new Vec2(200f, 0f), 30f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(sep: 300f), new List<Vec2>());
            Assert.Single(result);
            Assert.True(result[0].BurningCount >= 20, "expected the denser cluster to win");
        }

        [Fact]
        public void Detect_IsDeterministic()
        {
            var burning = Cluster(30, new Vec2(0f, 0f), 60f, 1);
            var a = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            var b = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Center.X, b[i].Center.X, 4);
                Assert.Equal(a[i].Center.Z, b[i].Center.Z, 4);
            }
        }

        [Fact]
        public void Detect_DenseCluster_AtNegativeCoordinates_ProducesCandidate()
        {
            // CS maps have the origin at the centre, so half of the map is in negative
            // coordinates. If GridVote's cell-key packing breaks for negative values, the
            // failure looks like fire whirls never appearing on one half of the map.
            var burning = Cluster(12, new Vec2(-4000f, -3000f), 40f, 1);
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Single(result);
            Assert.Equal(-4000f, result[0].Center.X, 0);
            Assert.Equal(-3000f, result[0].Center.Z, 0);
            Assert.Equal(12, result[0].BurningCount);
        }

        [Fact]
        public void Detect_ClusterStraddlingTheOrigin_ProducesCandidate()
        {
            // Straddling the origin splits it across cells of opposite sign. It must not
            // be cut in two at the cell boundary.
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Single(result);
            Assert.Equal(12, result[0].BurningCount);
        }

        [Fact]
        public void Detect_MirroredClusters_AreNotConfused()
        {
            // (-x, +z) and (+x, -z) are a combination that could be treated as the same
            // if the cell key gets folded. They must come out as separate candidates.
            var burning = Cluster(12, new Vec2(-2000f, 2000f), 40f, 1);
            burning.AddRange(Cluster(12, new Vec2(2000f, -2000f), 40f, 100));

            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Equal(2, result.Count);
            foreach (var c in result) Assert.Equal(12, c.BurningCount);
        }

        [Fact]
        public void Detect_EmptyInput_ProducesNothing()
        {
            Assert.Empty(FireWhirlDetector.Detect(
                new List<BurningBuilding>(), Config(), new List<Vec2>()));
        }
    }
}
