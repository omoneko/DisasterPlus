using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 火口が**高さプロファイルの一部**であること（実機の指摘①）。
    ///
    /// ここで固定するのは 4 つ:
    ///   1. 半径 R の外はきっかり 0（準備が届いていない場所を持ち上げない。設計書 §1.2）
    ///   2. 最終高 H を 1 mm も超えない（天井 §C-10）
    ///   3. **火口が実際に窪んでいる** —— 中心が縁より低い。素朴な引き算だと
    ///      成層火山でここが逆転する（<see cref="VolcanoCrater"/> のクラス doc）
    ///   4. **隆起の最初から窪んでいる** —— 進捗を上げていくと、縁と底の差が
    ///      単調に増えて depth で頭打ちになる（減らない）
    /// </summary>
    public class VolcanoCraterTests
    {
        private static readonly VolcanoForm[] AllForms =
            { VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome };

        private static readonly uint Seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));

        [Fact]
        public void TheProfileStaysInsideTheRadiusAndUnderTheHeight()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, VolcanoRelief.MaxStrengthUnit);

                for (float dz = -r - 32f; dz <= r + 32f; dz += 16f)
                {
                    for (float dx = -r - 32f; dx <= r + 32f; dx += 16f)
                    {
                        float v = VolcanoCrater.ProfileAt(relief, dx, dz, r, h);
                        Assert.InRange(v, 0f, h);

                        double d = Math.Sqrt(dx * dx + dz * dz);
                        if (d >= r) Assert.Equal(0f, v, 6);
                    }
                }
            }
        }

        [Fact]
        public void TheSummitIsTheCraterRimAndItReachesExactlyTheHeight()
        {
            // 起伏 0（＝滑らかな円錐）なら、火口の縁はきっかり H に届く。
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, 0f);

                float crater = VolcanoShape.CraterRadiusOf(r);
                float rim = VolcanoCrater.ProfileAt(relief, crater, 0f, r, h);
                Assert.Equal(h, rim, 2);
            }
        }

        [Fact]
        public void TheCentreIsLowerThanTheRimByTheFullDepth()
        {
            // ★ ここが指摘①の核心。素朴に「円錐から窪みを引く」と成層火山では
            //   中心のほうが高くなる（円錐が火口半径のあいだに 72 m 下がるのに、
            //   火口の深さは 60 m しかない）。
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, 0f);

                float depth = VolcanoShape.CraterDepthOf(h);
                float centre = VolcanoCrater.ProfileAt(relief, 0f, 0f, r, h);

                Assert.Equal(h - depth, centre, 2);
                Assert.True(centre < h - depth * 0.9f,
                    "the summit is not a depression for " + form);
            }
        }

        [Fact]
        public void TheFloorIsFlatAcrossTheVent()
        {
            // 噴出口の炎は半径を持つ円盤なので、底が椀だと縁で地面に潜る。
            const VolcanoForm form = VolcanoForm.Strato;
            float r = VolcanoShape.DefaultRadiusOf(form);
            float h = VolcanoShape.DefaultHeightOf(form);
            var relief = VolcanoRelief.For(form, Seed, 0f);

            float crater = VolcanoShape.CraterRadiusOf(r);
            float centre = VolcanoCrater.ProfileAt(relief, 0f, 0f, r, h);

            for (float d = 0f; d <= crater * VolcanoCrater.FloorFraction; d += 2f)
            {
                Assert.Equal(centre, VolcanoCrater.ProfileAt(relief, d, 0f, r, h), 2);
            }
        }

        [Fact]
        public void TheDepressionIsThereFromTheFirstTickAndNeverGetsShallower()
        {
            // 隆起は max(0, profile - H(1-p)) を書く。縁と底の差が p とともに
            // 単調に増えて depth で頭打ちになる ＝「最初から窪みとして在る」。
            const VolcanoForm form = VolcanoForm.Strato;
            float r = VolcanoShape.DefaultRadiusOf(form);
            float h = VolcanoShape.DefaultHeightOf(form);
            var relief = VolcanoRelief.For(form, Seed, 0f);

            float depth = VolcanoShape.CraterDepthOf(h);
            float rimProfile = VolcanoCrater.ProfileAt(relief, VolcanoShape.CraterRadiusOf(r),
                                                       0f, r, h);
            float floorProfile = VolcanoCrater.ProfileAt(relief, 0f, 0f, r, h);

            float previous = -1f;
            for (float p = 0f; p <= 1.0001f; p += 0.02f)
            {
                float rim = UpliftSchedule.GrowthMetresAt(rimProfile, h, p);
                float floor = UpliftSchedule.GrowthMetresAt(floorProfile, h, p);
                float sink = rim - floor;

                Assert.True(sink >= previous - 0.001f,
                    "the crater got shallower at p=" + p);
                Assert.True(sink <= depth + 0.001f,
                    "the crater is deeper than its final depth at p=" + p);
                previous = sink;
            }

            Assert.Equal(depth, previous, 2);
        }

        [Fact]
        public void TheFloorFollowsTheSummitAsItRises()
        {
            float h = 600f;
            float depth = VolcanoShape.CraterDepthOf(h);

            Assert.Equal(0f, VolcanoCrater.FloorMetresAt(0f, h), 4);
            Assert.Equal(0f, VolcanoCrater.FloorMetresAt(depth * 0.5f, h), 4);
            Assert.Equal(h - depth, VolcanoCrater.FloorMetresAt(h, h), 4);

            Assert.False(VolcanoCrater.FullDepthReached(depth * 0.5f, h));
            Assert.True(VolcanoCrater.FullDepthReached(depth, h));
            Assert.True(VolcanoCrater.FullDepthReached(h, h));
        }

        [Fact]
        public void TheConeIsRaisedJustEnoughForTheRimToReachTheHeight()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float scale = VolcanoCrater.SummitScale(form, r);

                Assert.InRange(scale, 1f, VolcanoCrater.MaxSummitScale);

                float crater = VolcanoShape.CraterRadiusOf(r);
                float rim = VolcanoShape.ProfileAt(form, crater, r, scale);
                Assert.Equal(1f, rim, 3);
            }

            // 成層火山は cr = 0.12R がどの半径でも成り立つので、倍率は一定である。
            Assert.Equal(1f / 0.88f, VolcanoCrater.SummitScale(VolcanoForm.Strato, 1200f), 3);
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            Assert.Equal(0f, VolcanoCrater.ProfileAt(null, 0f, 0f, 1000f, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, float.NaN, 0f, 1000f, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, 0f, float.NaN, 1000f, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, 0f, 0f, float.NaN, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, 0f, 0f, 1000f, float.NaN), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, 0f, 0f, 0f, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.ProfileAt(relief, 0f, 0f, 1000f, 0f), 4);

            Assert.Equal(1f, VolcanoCrater.SummitScale(VolcanoForm.Strato, float.NaN), 4);
            Assert.Equal(1f, VolcanoCrater.SummitScale(VolcanoForm.Strato, 0f), 4);
            Assert.Equal(0f, VolcanoCrater.ConeHeightMetres(VolcanoForm.Strato, 1000f, float.NaN), 4);
            Assert.Equal(0f, VolcanoCrater.CeilingMetres(10f, 1000f, float.NaN), 4);
            Assert.Equal(400f, VolcanoCrater.CeilingMetres(float.NaN, 1000f, 400f), 4);
            Assert.Equal(0f, VolcanoCrater.FloorMetresAt(float.NaN, 400f), 4);
        }

        [Fact]
        public void TheGrowthFrontLeadsTheProgressByTheConeScale()
        {
            // 倍率 1 なら今までどおり進捗そのもの。
            Assert.Equal(0.5f, UpliftSchedule.GrowthFrontUnit(0.5f, 1f), 4);
            Assert.Equal(0f, UpliftSchedule.GrowthFrontUnit(0f, 1f), 4);
            Assert.Equal(1f, UpliftSchedule.GrowthFrontUnit(1f, 1f), 4);

            // 立て直した円錐では前線が先に出る（＝準備もそのぶん先へ走らせる）。
            float scale = VolcanoCrater.SummitScale(VolcanoForm.Strato, 1200f);
            Assert.True(UpliftSchedule.GrowthFrontUnit(0.5f, scale) > 0.5f);
            Assert.Equal(1f, UpliftSchedule.GrowthFrontUnit(1f, scale), 4);

            // 異常入力でも [0,1] を出ない。
            Assert.Equal(0f, UpliftSchedule.GrowthFrontUnit(float.NaN, scale), 4);
            Assert.InRange(UpliftSchedule.GrowthFrontUnit(-5f, scale), 0f, 1f);
            Assert.InRange(UpliftSchedule.GrowthFrontUnit(5f, scale), 0f, 1f);
            Assert.Equal(0.25f, UpliftSchedule.GrowthFrontUnit(0.25f, float.NaN), 4);
        }
    }
}
