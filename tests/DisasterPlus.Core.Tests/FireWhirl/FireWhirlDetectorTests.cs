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

        /// <summary>半径 r の円内に n 棟を等間隔で並べる。</summary>
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
            // 半径 40m の円周上に 12 棟 → どの棟から見ても R=150m 以内に 12 棟ある
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
            // 同じ 12 棟でも 2km 間隔で散らばっていれば発生しない
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
            // 中心に 1 棟、R をわずかに超えた位置に 11 棟。中心棟から見ると近傍は自分だけ。
            var burning = new List<BurningBuilding> { new BurningBuilding(1, new Vec2(0f, 0f)) };
            var far = Cluster(11, new Vec2(0f, 0f), 150.5f, 2);
            burning.AddRange(far);
            // 外周の 11 棟同士は互いに近いので、そこで 11 棟クラスタになるが閾値 12 に届かない
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
            // 250m 離れた 2 クラスタ。MinSeparation = 300m なので 1 つに統合される。
            var burning = Cluster(12, new Vec2(0f, 0f), 30f, 1);
            burning.AddRange(Cluster(12, new Vec2(250f, 0f), 30f, 100));
            var result = FireWhirlDetector.Detect(burning, Config(sep: 300f), new List<Vec2>());
            Assert.Single(result);
        }

        [Fact]
        public void Detect_SuppressedNearExistingWhirl()
        {
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            var existing = new List<Vec2> { new Vec2(100f, 0f) };  // MinSeparation=300m 以内
            Assert.Empty(FireWhirlDetector.Detect(burning, Config(sep: 300f), existing));
        }

        [Fact]
        public void Detect_StrongestClusterWins_WhenMerged()
        {
            // 統合されるとき、燃焼棟数の多い側が残ること
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
            // CS のマップは原点が中心で、半分は負座標。GridVote のセルキー詰めが
            // 負で壊れるとマップの片側だけ旋風が出ない、という壊れ方をする。
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
            // 原点をまたぐと符号の違うセルに分かれる。セル境界で分断されないこと。
            var burning = Cluster(12, new Vec2(0f, 0f), 40f, 1);
            var result = FireWhirlDetector.Detect(burning, Config(), new List<Vec2>());
            Assert.Single(result);
            Assert.Equal(12, result[0].BurningCount);
        }

        [Fact]
        public void Detect_MirroredClusters_AreNotConfused()
        {
            // (-x, +z) と (+x, -z) はセルキーが畳まれると同一視されうる組み合わせ。
            // 別々の候補として出ること。
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
