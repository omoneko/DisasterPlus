using DisasterPlus.Core.Typhoon;
using Xunit;

namespace DisasterPlus.Core.Tests.Typhoon
{
    public class SquallLayoutTests
    {
        [Fact]
        public void NothingIsDrawnOutsideTheStorm()
        {
            // ★★ **台風の外に居るのに飛沫が舞ってはいけない。**
            //   TyphoonProfile.WindAt は強風域の外でちょうど 0 を返すので、
            //   ここが 0 を返せば「外では 1 粒も出ない」が構造で保証される。
            Assert.Equal(0f, SquallLayout.StrengthOf(0f), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(SquallLayout.MinWindUnit), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(float.NaN), 6);
            Assert.Equal(0f, SquallLayout.StrengthOf(-1f), 6);
        }

        [Fact]
        public void StrengthRisesToOneAndStopsThere()
        {
            Assert.Equal(1f, SquallLayout.StrengthOf(SquallLayout.FullWindUnit), 6);
            Assert.Equal(1f, SquallLayout.StrengthOf(1f), 6);
            Assert.Equal(1f, SquallLayout.StrengthOf(4f), 6);

            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float s = SquallLayout.StrengthOf(i / 20f);
                Assert.InRange(s, 0f, 1f);
                Assert.True(s >= previous, "strength is not monotonic at " + (i / 20f));
                previous = s;
            }
        }

        [Fact]
        public void TheWindTurnsAroundTheEyeAndLeansInward()
        {
            // 台風の二次循環。接線（渦の回る向き＝角度が増える向き）＋ 吸い込み。
            // ここが逆だと、雲と雨が逆向きに流れる。
            float wx, wz;
            SquallLayout.WindDirection(1000f, 0f, out wx, out wz);

            // 単位ベクトルであること。
            Assert.Equal(1f, wx * wx + wz * wz, 4);

            // (+X, 0) では接線は +Z、吸い込みは -X。
            Assert.True(wz > 0f, "the wind does not turn the same way as the vortex");
            Assert.True(wx < 0f, "the wind does not lean towards the eye");
        }

        [Fact]
        public void TheCentreOfTheEyeDoesNotProduceNaN()
        {
            // 中心のちょうど真上は向きが決まらない。**例外も NaN も出さない。**
            float wx, wz;
            SquallLayout.WindDirection(0f, 0f, out wx, out wz);
            Assert.False(float.IsNaN(wx));
            Assert.False(float.IsNaN(wz));
            Assert.Equal(1f, wx * wx + wz * wz, 4);

            SquallLayout.WindDirection(float.NaN, float.NaN, out wx, out wz);
            Assert.False(float.IsNaN(wx));
            Assert.False(float.IsNaN(wz));
        }

        [Fact]
        public void EveryPatchIsInsideTheDrawableRanges()
        {
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch p = SquallLayout.PatchAt(i);

                Assert.InRange(p.HeightFraction, 0f, 1f);
                Assert.InRange(p.DiscFraction, 0f, 1f);
                Assert.InRange(p.BandFraction, 0f, 1f);
                Assert.InRange(p.DensityFraction, 0f, 1f);

                // 散らばりの半径を大きくはみ出さない（カメラの周りに留まる）。
                float r = p.OffsetXFraction * p.OffsetXFraction
                          + p.OffsetZFraction * p.OffsetZFraction;
                Assert.True(r <= 1.25f, "patch " + i + " is thrown too far from the camera");
            }
        }

        [Fact]
        public void TheLayoutIsTheSameEveryTime()
        {
            // 添字だけの関数。フレームを混ぜると飛沫の湧く場所が毎フレーム跳ぶ。
            for (int i = 0; i < SquallLayout.PatchCount; i++)
            {
                SquallPatch a = SquallLayout.PatchAt(i);
                SquallPatch b = SquallLayout.PatchAt(i);
                Assert.Equal(a.OffsetXFraction, b.OffsetXFraction, 6);
                Assert.Equal(a.OffsetZFraction, b.OffsetZFraction, 6);
                Assert.Equal(a.HeightFraction, b.HeightFraction, 6);
                Assert.Equal(a.DensityFraction, b.DensityFraction, 6);
            }
        }

        [Fact]
        public void OutOfRangeIndicesDoNotThrow()
        {
            SquallPatch a = SquallLayout.PatchAt(-1);
            SquallPatch b = SquallLayout.PatchAt(SquallLayout.PatchCount + 50);
            Assert.Equal(a.OffsetXFraction, b.OffsetXFraction, 6);
        }

        [Fact]
        public void TheSprayFallsAndStaysLow()
        {
            // 雨である。**浮いてはいけない**（重力は正）。
            Assert.True(SquallLayout.GravityModifier > 0f);
            // ほぼ水平に散る（軸が上向きなので、角度が大きいほど横向きになる）。
            Assert.True(SquallLayout.SpawnAngleMinDegrees >= 60f);
            // 短命（飛んで落ちて消える）。渦の粒より遥かに短いこと。
            Assert.True(SquallLayout.LifeMaxSeconds
                        < VortexCloudProfile.Tower.LifeMinSeconds);
            // ★ 0 にすると 1 粒も出ない（§D-2 の罠）。
            Assert.True(SquallLayout.RateOverTime > 0f);
            // 都市が見えなくなってはいけない。
            Assert.InRange(SquallLayout.Alpha, 0.05f, 0.5f);
        }
    }
}
