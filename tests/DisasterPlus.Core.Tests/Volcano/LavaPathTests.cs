using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class LavaPathTests
    {
        [Fact]
        public void AStepMovesExactlyTheStepLength()
        {
            Vec2 next;
            Assert.True(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), 12f, out next));
            Assert.Equal(12f, next.X, 3);
            Assert.Equal(0f, next.Z, 3);
        }

        [Fact]
        public void TheDirectionIsNormalisedBeforeTheStepIsTaken()
        {
            // 呼び出し側が長さ 1 でないベクトルを渡しても、歩幅は変わらない。
            Vec2 a, b;
            LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(3f, 4f), 10f, out a);
            LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(30f, 40f), 10f, out b);
            Assert.Equal(a.X, b.X, 3);
            Assert.Equal(a.Z, b.Z, 3);
            float len = (float)System.Math.Sqrt(a.X * a.X + a.Z * a.Z);
            Assert.Equal(10f, len, 3);
        }

        [Fact]
        public void AFlatOrPooledSpotStopsTheFlowInsteadOfDrifting()
        {
            // 平らな場所で「とりあえず前へ」を続けると、溶岩が窪地を素通りして
            // マップの端まで走り続ける。止めて溜まるのが正しい。
            Vec2 next;
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(0f, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f),
                new Vec2(LavaPath.MinSlope * 0.4f, 0f), 12f, out next));
        }

        [Fact]
        public void GarbageInputStopsTheFlowInsteadOfProducingNaN()
        {
            // 「NaN の位置にある溶岩」は描画もグリッド走査も静かに壊す。
            Vec2 next;
            Assert.False(LavaPath.NextPosition(new Vec2(float.NaN, 0f), new Vec2(1f, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(float.NaN, 0f), 12f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), float.NaN, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), 0f, out next));
            Assert.False(LavaPath.NextPosition(new Vec2(0f, 0f), new Vec2(1f, 0f), -12f, out next));
        }

        [Fact]
        public void TheInitialDirectionsAreSpreadAroundTheCrater()
        {
            // 全部同じ向きに出ると、火山の片側だけが溶岩に覆われる。
            const int n = 4;
            Vec2[] dirs = new Vec2[n];
            for (int i = 0; i < n; i++) dirs[i] = LavaPath.InitialDirection(7u, i, n);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float dot = dirs[i].X * dirs[j].X + dirs[i].Z * dirs[j].Z;
                    Assert.True(dot < 0.9f,
                        "flows " + i + " and " + j + " point almost the same way (dot=" + dot + ")");
                }
            }
        }

        [Fact]
        public void TheInitialDirectionsAreUnitLengthAndDeterministic()
        {
            for (int i = 0; i < 8; i++)
            {
                Vec2 a = LavaPath.InitialDirection(11u, i, 8);
                Vec2 b = LavaPath.InitialDirection(11u, i, 8);
                Assert.Equal(a.X, b.X, 5);
                Assert.Equal(a.Z, b.Z, 5);
                float len = (float)System.Math.Sqrt(a.X * a.X + a.Z * a.Z);
                Assert.Equal(1f, len, 3);
            }
        }

        [Fact]
        public void DifferentSeedsGiveDifferentFans()
        {
            Vec2 a = LavaPath.InitialDirection(1u, 0, 4);
            Vec2 b = LavaPath.InitialDirection(2u, 0, 4);
            Assert.True(a.X != b.X || a.Z != b.Z, "the fan does not depend on the seed");
        }

        [Fact]
        public void DegenerateFlowCountsAreHandled()
        {
            Vec2 one = LavaPath.InitialDirection(3u, 0, 1);
            float len = (float)System.Math.Sqrt(one.X * one.X + one.Z * one.Z);
            Assert.Equal(1f, len, 3);
            // 範囲外の添字でも単位ベクトルを返し、NaN を出さない。
            Vec2 bad = LavaPath.InitialDirection(3u, 9, 4);
            Assert.False(float.IsNaN(bad.X) || float.IsNaN(bad.Z));
            Vec2 zero = LavaPath.InitialDirection(3u, 0, 0);
            Assert.False(float.IsNaN(zero.X) || float.IsNaN(zero.Z));
        }

        [Fact]
        public void TheFlowWidensAsItTravelsButIsCapped()
        {
            float near = LavaPath.SpreadRadiusFor(0f);
            float mid = LavaPath.SpreadRadiusFor(1000f);
            float far = LavaPath.SpreadRadiusFor(100000f);
            Assert.Equal(LavaPath.SpreadBaseMetres, near, 3);
            Assert.True(mid > near);
            Assert.Equal(LavaPath.SpreadMaxMetres, far, 3);
            Assert.Equal(LavaPath.SpreadBaseMetres, LavaPath.SpreadRadiusFor(float.NaN), 3);
            Assert.Equal(LavaPath.SpreadBaseMetres, LavaPath.SpreadRadiusFor(-5f), 3);
        }

        [Fact]
        public void TheVentSitsOutsideTheCraterRim()
        {
            // ★ 縁の真上から出すと、下り方向が火口の内側を指して溶岩が窪みに溜まる。
            //   必ず「比」と「絶対値」の両方より外へ出ること。
            for (float crater = 40f; crater <= 400f; crater += 20f)
            {
                float vent = LavaPath.VentRadiusMetres(crater);
                Assert.True(vent >= crater * LavaPath.VentRimClearanceFactor - 0.001f);
                Assert.True(vent >= crater + LavaPath.VentRimClearanceMetres - 0.001f);
                // それでも山の外へ出るほどは離れない（いちばん小さい山でも半径 250 m）。
                Assert.True(vent < 250f || crater > 180f);
            }

            // 読めないときも中心から出さない（0 を返さない）。
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(0f), 3);
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(float.NaN), 3);
            Assert.Equal(LavaPath.StepMetres, LavaPath.VentRadiusMetres(-1f), 3);
        }

        [Fact]
        public void TheStepBudgetIsFiniteAndDeclared()
        {
            // 「止まらない溶岩」を作らない。上限は定数として名乗る。
            Assert.InRange(LavaPath.MaxSteps, 1, 4096);
            Assert.True(LavaPath.StepMetres > 0f);
            Assert.True(LavaPath.MinSlope > 0f);
            Assert.True(LavaPath.SpreadMaxMetres > LavaPath.SpreadBaseMetres);
        }
    }
}
