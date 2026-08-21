using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 土煙の扇（実機の指摘⑤「火砕流は溶岩流の上だけでなく、もっと裾野に広がるはず」）。
    ///
    /// ここで固定するのは 5 つ:
    ///   1. 舌は火口のまわりに**散らばる**（溶岩の上に重ならない）
    ///   2. 下るほど**広がる**
    ///   3. 谷へは**裾へ行くほどだけ**引かれる（源では尾根を越える）
    ///   4. 溶岩が 1 本も無くても扇は出る
    ///   5. 粒子の総量は増えない（帯の面積で正規化してある）
    /// </summary>
    public class PyroclasticSurgeTests
    {
        private static readonly uint Seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));

        private static readonly Vec2 Vent = new Vec2(100f, -50f);

        [Fact]
        public void TheLobesAreSpreadAroundTheVent()
        {
            var seen = new float[PyroclasticSurge.LobeCount];
            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                seen[i] = PyroclasticSurge.LobeAzimuth(Seed, i, PyroclasticSurge.LobeCount);
            }

            // 隣どうしが入れ替わらない（＝ゆらぎが等間隔の半分を超えない）。
            float step = (float)(2.0 * Math.PI / PyroclasticSurge.LobeCount);
            for (int i = 1; i < seen.Length; i++)
            {
                float gap = seen[i] - seen[i - 1];
                Assert.InRange(gap, step * 0.3f, step * 1.7f);
            }

            // 番号が範囲外でも壊れない（呼び出し側のループの外側を守る）。
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, -3, 5)));
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, 99, 5)));
            Assert.False(Bad(PyroclasticSurge.LobeAzimuth(Seed, 0, 0)));
        }

        [Fact]
        public void TheBandWidensAsItDescends()
        {
            float near = PyroclasticSurge.HalfWidthMetres(0f);
            float mid = PyroclasticSurge.HalfWidthMetres(600f);
            float far = PyroclasticSurge.HalfWidthMetres(100000f);

            Assert.Equal(PyroclasticSurge.HalfWidthBaseMetres, near, 3);
            Assert.True(mid > near * 2f, "the surge should fan out as it descends");
            Assert.Equal(PyroclasticSurge.HalfWidthMaxMetres, far, 3);

            Assert.Equal(PyroclasticSurge.HalfWidthBaseMetres,
                         PyroclasticSurge.HalfWidthMetres(float.NaN), 3);
        }

        [Fact]
        public void TheValleyPullsOnlyNearTheFoot()
        {
            // ★ 源では尾根を越え、裾で谷に集まる。
            Assert.Equal(0f, PyroclasticSurge.ChannelPullAt(0f), 4);
            Assert.True(PyroclasticSurge.ChannelPullAt(0.25f) < 0.1f);
            Assert.Equal(PyroclasticSurge.ChannelPullMax, PyroclasticSurge.ChannelPullAt(1f), 4);

            // **半分を超えない** —— 超えると「溶岩の上だけを流れる」に戻る。
            Assert.True(PyroclasticSurge.ChannelPullMax <= 0.5f);

            for (float t = 0f; t <= 1.2f; t += 0.05f)
            {
                Assert.InRange(PyroclasticSurge.ChannelPullAt(t), 0f,
                               PyroclasticSurge.ChannelPullMax);
            }
            Assert.Equal(0f, PyroclasticSurge.ChannelPullAt(float.NaN), 4);
        }

        [Fact]
        public void TheLobeLeavesTheVentOnItsOwnBearingAndTurnsTowardTheValley()
        {
            const float reach = 900f;
            const float baseAzimuth = 0f;                 // +X へ出る
            float channel = (float)(Math.PI * 0.5);       // 谷は +Z

            Vec2 near = PyroclasticSurge.PointAt(Vent, baseAzimuth, channel - baseAzimuth,
                                                 reach, 60f);
            Vec2 far = PyroclasticSurge.PointAt(Vent, baseAzimuth, channel - baseAzimuth,
                                                reach, reach);

            // 源のすぐそばは、ほぼ舌そのものの向き（谷へは曲がっていない）。
            float nearAngle = (float)Math.Atan2(near.Z - Vent.Z, near.X - Vent.X);
            Assert.InRange(nearAngle, -0.6f, 0.6f);

            // 裾では谷の側へ回り込んでいる（ただし谷そのものにはならない）。
            float farAngle = (float)Math.Atan2(far.Z - Vent.Z, far.X - Vent.X);
            Assert.True(farAngle > nearAngle + 0.2f, "the lobe never turned toward the valley");
            Assert.True(farAngle < channel - 0.2f, "the lobe collapsed onto the lava path");
        }

        [Fact]
        public void TheFanIsThereEvenWhenNoLavaIsFlowing()
        {
            bool found;
            float azimuth = 1.2f;
            Assert.Equal(azimuth, PyroclasticSurge.NearestChannel(azimuth, null, 0, out found), 4);
            Assert.False(found);

            Assert.Equal(azimuth,
                         PyroclasticSurge.NearestChannel(azimuth, new float[0], 0, out found), 4);
            Assert.False(found);

            // 舌そのものは出る（谷に引かれないだけ）。
            Vec2 a, b, c, d;
            Assert.True(PyroclasticSurge.TryLobe(Vent, azimuth, azimuth, 900f, 400f,
                                                 out a, out b, out c, out d));
            Assert.False(Bad(a.X) || Bad(a.Z) || Bad(d.X) || Bad(d.Z));
        }

        [Fact]
        public void TheNearestValleyIsFoundAcrossTheWrapAround()
        {
            bool found;
            var bearings = new float[] { 3.0f, 0.5f };

            // -3.1 の近くは 3.0（+π と −π を跨ぐ）。差は 0.18 で、0.5 との差より小さい。
            float result = PyroclasticSurge.NearestChannel(-3.1f, bearings, 2, out found);
            Assert.True(found);
            Assert.Equal(-3.1f + -0.1831853f, result, 3);

            // NaN が混じっていても落ちない。
            var dirty = new float[] { float.NaN, 0.4f };
            result = PyroclasticSurge.NearestChannel(0.5f, dirty, 2, out found);
            Assert.True(found);
            Assert.Equal(0.4f, result, 3);
        }

        [Fact]
        public void TheHeadRunsDownTheLobeAndStartsOver()
        {
            const float path = 900f;
            float cycle = PyroclasticSurge.CycleSeconds(path);

            Assert.Equal(0f, PyroclasticSurge.HeadMetres(0f, path), 2);
            Assert.Equal(0f, PyroclasticSurge.HeadMetres(cycle, path), 1);
            Assert.True(PyroclasticSurge.HeadMetres(cycle * 0.5f, path) > 0f);

            Assert.True(PyroclasticSurge.CycleSeconds(0f) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(float.NaN) > 0f);
            Assert.True(PyroclasticSurge.CycleSeconds(-100f) > 0f);

            // 舌ごとに位相がずれている（5 本が隊列を組まない）。
            float previous = -1f;
            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                float phase = PyroclasticSurge.LobePhaseSeconds(i, PyroclasticSurge.LobeCount,
                                                                path);
                Assert.InRange(phase, 0f, cycle);
                Assert.True(phase > previous);
                previous = phase;
            }
        }

        [Fact]
        public void TheParticleBudgetDoesNotGrowWithTheFan()
        {
            // ★ ベジェ帯の粒子数は 2 x halfWidth x 経路長 x pps（IL §B-5）。
            //   幅を 90 -> 260 m、本数を 2 -> 5 に増やしても、扇ぜんぶの
            //   「面積 x magnitude」が従来の 2 本ぶんを超えないこと。
            const float path = 1200f;
            float worst = 0f;

            for (float head = 0f; head <= path; head += 25f)
            {
                float half = PyroclasticSurge.HalfWidthMetres(head);
                float m = PyroclasticSurge.Magnitude(1f, head, path, half);
                float area = 2f * half * PyroclasticSurge.BandLengthMetres
                             * PyroclasticSurge.LobeCount;
                float budget = area * m;
                if (budget > worst) worst = budget;
            }

            float before = PyroclasticSurge.ReferenceAreaSquareMetres
                           * PyroclasticSurge.MagnitudeMax;
            Assert.True(worst <= before * 1.01f,
                "the fan emits more particles than the two old bands did");
        }

        [Fact]
        public void NothingIsDrawnBeforeTheHeadHasLeftTheVentOrAfterItHasGone()
        {
            const float path = 900f;

            // 帯が経路に 1 mm も載っていないあいだは 0（＝呼び出し側は描かない）。
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, 0f, path, 50f), 4);

            // 経路が短すぎる山では 1 本も出さない（山頂に灰の球が乗る）。
            Assert.Equal(0f, PyroclasticSurge.Magnitude(
                1f, 40f, PyroclasticSurge.MinPathMetres - 1f, 50f), 4);

            Vec2 a, b, c, d;
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, 40f, 20f,
                                                  out a, out b, out c, out d));
            Assert.Equal(Vent.X, a.X, 3);
        }

        [Fact]
        public void TheReachGrowsWithTheEruptionAndVariesByLobe()
        {
            const float radius = 1200f;

            float weak = PyroclasticSurge.ReachMetres(radius, 0f, Seed, 0);
            float strong = PyroclasticSurge.ReachMetres(radius, 1f, Seed, 0);
            Assert.True(strong > weak);
            Assert.InRange(strong, radius * 0.7f, radius * 1.2f);

            // 舌ごとに違う（全部同じだと扇の縁が真円になる）。
            float first = PyroclasticSurge.ReachMetres(radius, 1f, Seed, 0);
            bool differs = false;
            for (int i = 1; i < PyroclasticSurge.LobeCount; i++)
            {
                if (Math.Abs(PyroclasticSurge.ReachMetres(radius, 1f, Seed, i) - first) > 1f)
                {
                    differs = true;
                }
            }
            Assert.True(differs, "every lobe reaches exactly as far as the others");

            Assert.Equal(0f, PyroclasticSurge.ReachMetres(float.NaN, 1f, Seed, 0), 4);
            Assert.Equal(0f, PyroclasticSurge.ReachMetres(0f, 1f, Seed, 0), 4);
        }

        [Fact]
        public void GarbageInputIsNeverNaN()
        {
            Vec2 bad = new Vec2(float.NaN, 0f);
            Vec2 a, b, c, d;

            Assert.False(PyroclasticSurge.TryLobe(bad, 0f, 0f, 900f, 400f,
                                                  out a, out b, out c, out d));
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, float.NaN, 400f,
                                                  out a, out b, out c, out d));
            Assert.False(PyroclasticSurge.TryLobe(Vent, 0f, 0f, 900f, float.NaN,
                                                  out a, out b, out c, out d));

            // 強さが読めないときは**いちばん薄い帯**（0 ではない）。
            // EruptionEffectPlan と同じ扱いで、「読めない」を「出さない」にしない。
            Assert.True(PyroclasticSurge.Magnitude(float.NaN, 300f, 900f, 50f) >= 0f);
            Assert.False(Bad(PyroclasticSurge.Magnitude(float.NaN, 300f, 900f, 50f)));
            Assert.Equal(0f, PyroclasticSurge.Magnitude(1f, 100f, float.NaN, 50f), 4);
            Assert.True(PyroclasticSurge.Magnitude(1f, 300f, 900f, float.NaN) >= 0f);

            Vec2 p = PyroclasticSurge.PointAt(Vent, 0f, 0f, 900f, float.NaN);
            Assert.Equal(Vent.X, p.X, 3);
        }

        [Fact]
        public void TheLavaBearingPointsWhereTheFlowWent()
        {
            var points = new[]
            {
                new Vec2(100f, -50f), new Vec2(160f, -50f), new Vec2(240f, -50f),
            };

            float bearing;
            Assert.True(PyroclasticSurge.TryBearing(points, 0, points.Length, Vent, out bearing));
            Assert.Equal(0f, bearing, 2);   // +X へ流れた

            // 点が足りない・壊れているときは false（**推測で向きを作らない**）。
            Assert.False(PyroclasticSurge.TryBearing(null, 0, 3, Vent, out bearing));
            Assert.False(PyroclasticSurge.TryBearing(points, 0, 1, Vent, out bearing));
            Assert.False(PyroclasticSurge.TryBearing(
                new[] { Vent, Vent }, 0, 2, Vent, out bearing));
        }

        private static bool Bad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
