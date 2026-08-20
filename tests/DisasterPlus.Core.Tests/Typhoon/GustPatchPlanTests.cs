using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class GustPatchPlanTests
    {
        private const float Pi = 3.14159265f;
        private const float HalfPi = 1.57079633f;

        [Fact]
        public void NeverMoreThanTheDeclaredNumberOfPatchesIsAlive()
        {
            // ★ これが 1 tick あたりの仕事量の上限そのものである。
            for (uint elapsed = 0; elapsed < 20000u; elapsed += 7u)
            {
                uint first, last;
                if (!GustPatchPlan.AliveRange(elapsed, out first, out last)) continue;

                Assert.True(first <= last);
                Assert.True(last - first + 1u <= (uint)GustPatchPlan.MaxActivePatches,
                            "elapsed=" + elapsed + " gave " + (last - first + 1u) + " patches");
            }
        }

        [Fact]
        public void APatchIsAliveExactlyWhileItsLifetimeLasts()
        {
            const uint ordinal = 3u;
            uint birth = GustPatchPlan.BirthFrameOf(ordinal);

            uint first, last;

            // 生まれた瞬間は生きている。
            Assert.True(GustPatchPlan.AliveRange(birth, out first, out last));
            Assert.True(first <= ordinal && ordinal <= last);

            // 寿命の 1 フレーム前も生きている。
            Assert.True(GustPatchPlan.AliveRange(birth + GustPatchPlan.LifetimeFrames - 1u,
                                                 out first, out last));
            Assert.True(first <= ordinal && ordinal <= last);

            // 寿命を過ぎたら生きていない。
            Assert.True(GustPatchPlan.AliveRange(birth + GustPatchPlan.LifetimeFrames,
                                                 out first, out last));
            Assert.True(ordinal < first,
                        "patch " + ordinal + " outlived its lifetime (first=" + first + ")");
        }

        [Fact]
        public void PatchesAreShortLivedComparedToAStorm()
        {
            // 「短命であること」を数字で固定する。台風は m_activeDuration
            // （数千〜数万フレーム）だけ生きるので、パッチはその何十分の一。
            Assert.True(GustPatchPlan.LifetimeFrames <= 1024u);
            Assert.True(GustPatchPlan.SpawnIntervalFrames < GustPatchPlan.LifetimeFrames);
        }

        [Fact]
        public void LifePhaseRunsFromZeroToOneAndStopsThere()
        {
            const uint ordinal = 2u;
            uint birth = GustPatchPlan.BirthFrameOf(ordinal);

            Assert.Equal(0f, GustPatchPlan.LifePhase(ordinal, birth), 5);
            Assert.Equal(0f, GustPatchPlan.LifePhase(ordinal, birth - 1u > birth ? 0u : birth), 5);
            Assert.InRange(GustPatchPlan.LifePhase(ordinal, birth + GustPatchPlan.LifetimeFrames / 2u),
                           0.4f, 0.6f);
            Assert.Equal(1f, GustPatchPlan.LifePhase(ordinal, birth + GustPatchPlan.LifetimeFrames), 5);
            Assert.Equal(1f, GustPatchPlan.LifePhase(ordinal, birth + 99999u), 5);
        }

        [Fact]
        public void MostPatchesLandOnTheDangerousSemicircle()
        {
            // 「危険半円に寄る」を分布で固定する。反対側にも出るが多数派ではない。
            int onDangerous = 0;
            const int samples = 400;

            for (uint ordinal = 0; ordinal < samples; ordinal++)
            {
                float angle, orbit, radius, strength;
                GustPatchPlan.Patch(17, ordinal, false, out angle, out orbit, out radius,
                                    out strength);

                // 北半球の危険半円は相対角 -90 度。相対角の sin が負なら右側。
                if (System.Math.Sin(angle) < 0.0) onDangerous++;
            }

            Assert.True(onDangerous > samples * 0.62,
                        "only " + onDangerous + "/" + samples + " patches were on the "
                        + "dangerous side");
            Assert.True(onDangerous < samples,
                        "every single patch was on the dangerous side; the other half should "
                        + "still see some");
        }

        [Fact]
        public void TheSouthernHemisphereMirrorsThePatchDistribution()
        {
            int onLeft = 0;
            const int samples = 400;

            for (uint ordinal = 0; ordinal < samples; ordinal++)
            {
                float angle, orbit, radius, strength;
                GustPatchPlan.Patch(17, ordinal, true, out angle, out orbit, out radius,
                                    out strength);
                if (System.Math.Sin(angle) > 0.0) onLeft++;
            }

            Assert.True(onLeft > samples * 0.62,
                        "only " + onLeft + "/" + samples + " patches were on the left");
        }

        [Fact]
        public void EveryPatchIsInsideTheDeclaredRanges()
        {
            for (uint ordinal = 0; ordinal < 200; ordinal++)
            {
                float angle, orbit, radius, strength;
                GustPatchPlan.Patch(9, ordinal, false, out angle, out orbit, out radius,
                                    out strength);

                Assert.InRange(angle, -HalfPi - Pi - 0.001f, -HalfPi + Pi + 0.001f);
                Assert.InRange(orbit, GustPatchPlan.MinOrbitFraction,
                               GustPatchPlan.MaxOrbitFraction);
                Assert.InRange(radius, GustPatchPlan.MinRadiusMetres,
                               GustPatchPlan.MaxRadiusMetres);
                Assert.InRange(strength, 0.6f, 1f);
            }
        }

        [Fact]
        public void ThePlacementNeverMovesForTheSamePatch()
        {
            // フレームを混ぜていないことの試験。混ぜるとパッチが毎 tick 瞬間移動する。
            for (uint ordinal = 0; ordinal < 20; ordinal++)
            {
                float a1, o1, r1, s1, a2, o2, r2, s2;
                GustPatchPlan.Patch(41, ordinal, false, out a1, out o1, out r1, out s1);
                GustPatchPlan.Patch(41, ordinal, false, out a2, out o2, out r2, out s2);

                Assert.Equal(a1, a2, 6);
                Assert.Equal(o1, o2, 6);
                Assert.Equal(r1, r2, 6);
                Assert.Equal(s1, s2, 6);
            }
        }

        [Fact]
        public void DifferentTyphoonsGetDifferentPatches()
        {
            float a1, o1, r1, s1, a2, o2, r2, s2;
            GustPatchPlan.Patch(3, 0u, false, out a1, out o1, out r1, out s1);
            GustPatchPlan.Patch(4, 0u, false, out a2, out o2, out r2, out s2);

            Assert.True(a1 != a2 || o1 != o2 || r1 != r2 || s1 != s2);
        }
    }
}
