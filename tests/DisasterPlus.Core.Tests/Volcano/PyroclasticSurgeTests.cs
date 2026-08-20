using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class PyroclasticSurgeTests
    {
        /// <summary>X 方向にまっすぐ <paramref name="metres"/> だけ伸びる経路。</summary>
        private static Vec2[] StraightPath(float metres, int points)
        {
            var a = new Vec2[points];
            for (int i = 0; i < points; i++)
            {
                a[i] = new Vec2(metres * i / (points - 1), 0f);
            }
            return a;
        }

        [Fact]
        public void ThePathLengthIsTheSumOfTheSegments()
        {
            var p = StraightPath(600f, 7);
            Assert.Equal(600f, PyroclasticSurge.PathLengthMetres(p, 0, p.Length), 2);
        }

        [Fact]
        public void ABrokenCoordinateTruncatesThePathInsteadOfPoisoningIt()
        {
            // NaN から先を信じると帯が地図の外へ飛ぶ。そこで打ち切るのが正しい。
            var p = new Vec2[] {
                new Vec2(0f, 0f), new Vec2(100f, 0f), new Vec2(float.NaN, 0f),
                new Vec2(300f, 0f)
            };
            Assert.Equal(2, PyroclasticSurge.UsableCount(p, 0, p.Length));
            Assert.Equal(100f, PyroclasticSurge.PathLengthMetres(p, 0, p.Length), 2);
        }

        [Fact]
        public void APointAtADistanceLandsOnThePath()
        {
            var p = StraightPath(400f, 5);
            Vec2 q;
            Assert.True(PyroclasticSurge.TryPointAt(p, 0, p.Length, 250f, out q));
            Assert.Equal(250f, q.X, 2);
            Assert.Equal(0f, q.Z, 2);
        }

        [Fact]
        public void ADistancePastTheEndIsClampedToTheEnd()
        {
            var p = StraightPath(400f, 5);
            Vec2 q;
            Assert.True(PyroclasticSurge.TryPointAt(p, 0, p.Length, 9000f, out q));
            Assert.Equal(400f, q.X, 2);

            Assert.True(PyroclasticSurge.TryPointAt(p, 0, p.Length, -50f, out q));
            Assert.Equal(0f, q.X, 2);
        }

        [Fact]
        public void TheHeadWrapsRoundAndRestartsAtTheCrater()
        {
            float path = 600f;
            float cycle = PyroclasticSurge.CycleSeconds(path);
            Assert.Equal(0f, PyroclasticSurge.HeadMetres(0f, path), 2);
            Assert.Equal(0f, PyroclasticSurge.HeadMetres(cycle, path), 1);
            Assert.True(PyroclasticSurge.HeadMetres(cycle * 0.5f, path) > 0f);
        }

        [Fact]
        public void TheCycleIsNeverZeroSoTheClockCannotDivideByIt()
        {
            Assert.True(PyroclasticSurge.CycleSeconds(0f) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(float.NaN) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(-100f) > 0f);
        }

        [Fact]
        public void TheBandFadesInAtBothEndsOfThePath()
        {
            float path = 900f;
            float mid = PyroclasticSurge.Magnitude(1f, path * 0.5f, path);
            float justStarted = PyroclasticSurge.Magnitude(
                1f, PyroclasticSurge.BandLengthMetres * 0.1f, path);
            float leaving = PyroclasticSurge.Magnitude(
                1f, path + PyroclasticSurge.BandLengthMetres * 0.9f, path);

            Assert.True(mid > justStarted);
            Assert.True(mid > leaving);
            Assert.True(justStarted > 0f);
        }

        [Fact]
        public void AShortPathGetsNoBandAtAll()
        {
            // 火口のすぐそばの数点に帯を巻くと、山頂に灰の球が乗る。
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, 10f, 20f), 4);

            var p = StraightPath(40f, 4);
            Vec2 a, b, c, d;
            Assert.False(PyroclasticSurge.TryBand(p, 0, p.Length, 20f, out a, out b, out c, out d));
        }

        [Fact]
        public void TheBandRunsFromTailToHeadAlongThePath()
        {
            var p = StraightPath(1000f, 21);
            Vec2 a, b, c, d;
            float head = 600f;
            Assert.True(PyroclasticSurge.TryBand(p, 0, p.Length, head,
                                                 out a, out b, out c, out d));

            Assert.Equal(head - PyroclasticSurge.BandLengthMetres, a.X, 1);
            Assert.Equal(head, d.X, 1);
            Assert.True(a.X < b.X && b.X < c.X && c.X < d.X);
        }

        [Fact]
        public void TheBandIsClippedToThePathWhenTheHeadHasRunOffTheEnd()
        {
            var p = StraightPath(500f, 11);
            Vec2 a, b, c, d;
            Assert.True(PyroclasticSurge.TryBand(p, 0, p.Length, 620f,
                                                 out a, out b, out c, out d));
            Assert.Equal(500f, d.X, 1);
            Assert.True(a.X >= 620f - PyroclasticSurge.BandLengthMetres - 0.01f);
        }

        [Fact]
        public void ABandThatHasLeftThePathCompletelyIsRefused()
        {
            var p = StraightPath(500f, 11);
            Vec2 a, b, c, d;
            float gone = 500f + PyroclasticSurge.BandLengthMetres + 10f;
            Assert.False(PyroclasticSurge.TryBand(p, 0, p.Length, gone,
                                                  out a, out b, out c, out d));
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, gone, 500f), 4);
        }

        [Fact]
        public void TheBandWidensDownhillButIsCapped()
        {
            Assert.True(PyroclasticSurge.HalfWidthMetres(1000f)
                        > PyroclasticSurge.HalfWidthMetres(0f));
            Assert.Equal(PyroclasticSurge.HalfWidthMaxMetres,
                         PyroclasticSurge.HalfWidthMetres(100000f), 2);
            Assert.Equal(PyroclasticSurge.HalfWidthBaseMetres,
                         PyroclasticSurge.HalfWidthMetres(float.NaN), 2);
        }

        [Fact]
        public void NullAndEmptyInputsAreRefusedRatherThanGuessed()
        {
            Vec2 a, b, c, d;
            Assert.False(PyroclasticSurge.TryBand(null, 0, 4, 100f, out a, out b, out c, out d));
            Assert.Equal(0, PyroclasticSurge.UsableCount(null, 0, 4));
            Assert.Equal(0, PyroclasticSurge.UsableCount(new Vec2[3], 5, 4));
            Assert.Equal(0f, PyroclasticSurge.PathLengthMetres(new Vec2[0], 0, 0), 4);
        }

        [Fact]
        public void ASliceInTheMiddleOfAPackedArrayIsHonoured()
        {
            // スナップショットは全流路を 1 本の配列に詰めて渡してくる。
            var packed = new Vec2[] {
                new Vec2(-999f, -999f),
                new Vec2(0f, 0f), new Vec2(200f, 0f), new Vec2(400f, 0f),
                new Vec2(999f, 999f)
            };
            Assert.Equal(3, PyroclasticSurge.UsableCount(packed, 1, 3));
            Assert.Equal(400f, PyroclasticSurge.PathLengthMetres(packed, 1, 3), 2);
        }
    }
}
