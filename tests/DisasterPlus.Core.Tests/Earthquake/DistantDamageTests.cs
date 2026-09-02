using DisasterPlus.Core.Earthquake;
using Xunit;

namespace DisasterPlus.Core.Tests.Earthquake
{
    /// <summary>
    /// <b>海溝型地震の遠地被害。</b>（2026-09-02、所有者
    /// 「震源から離れていても一定確率で火災や倒壊が起きるように（地震の規模に合わせて）」）
    ///
    /// ★ ここが固定しているのは<b>依頼の 2 つの言葉</b>である ——
    ///   「離れていても一定確率で」（＝床が残る）と
    ///   「規模に合わせて」（＝強度で大きく変わる）。
    /// </summary>
    public class DistantDamageTests
    {
        private const float Full = DistantDamage.MaxStrength;

        [Fact]
        public void Far_from_the_epicentre_it_still_happens()
        {
            // ★★ **これが依頼の中身である。** バニラの円盤（1 - d/R）は
            //    遠方でほぼ 0 になる。こちらは床で残る。
            float reach = DistantDamage.ReachMetres(200);

            float near = DistantDamage.Falloff(0f, 200);
            float half = DistantDamage.Falloff(reach * 0.5f, 200);

            Assert.Equal(1f, near, 3);

            // 半分の距離で、近傍の 6 割以上が残っていること
            // （床が無ければちょうど 0.5 になる）。
            Assert.True(half > near * 0.6f, "falloff at half reach was " + half);
        }

        [Fact]
        public void The_reach_covers_the_whole_map_for_a_big_quake()
        {
            // 25 タイル全域でも対角の半分は約 12,200 m。震央が沖にあるので、
            // ここが届かないと**街に一切被害が出ない**。
            Assert.True(DistantDamage.ReachMetres(255) > 12200f,
                "reach at full intensity was " + DistantDamage.ReachMetres(255));

            // 既定のスライダー（55）でも 9 タイル（半対角 約 7,330 m）は覆う。
            Assert.True(DistantDamage.ReachMetres(55) > 7330f,
                "reach at intensity 55 was " + DistantDamage.ReachMetres(55));
        }

        [Fact]
        public void It_stops_at_the_edge_without_a_step()
        {
            // ★ 端で段差にすると、マップ上で被害がぷつりと切れるのが見える。
            float reach = DistantDamage.ReachMetres(150);

            Assert.Equal(0f, DistantDamage.Falloff(reach, 150));
            Assert.Equal(0f, DistantDamage.Falloff(reach * 1.5f, 150));

            // 端の直前は既に小さい（テーパーが効いている）。
            float justInside = DistantDamage.Falloff(reach * 0.98f, 150);
            Assert.True(justInside < 0.1f, "just inside the edge was " + justInside);
        }

        [Fact]
        public void It_never_goes_up_with_distance()
        {
            float reach = DistantDamage.ReachMetres(120);
            float previous = float.MaxValue;

            for (float d = 0f; d <= reach * 1.1f; d += reach / 200f)
            {
                float f = DistantDamage.Falloff(d, 120);
                Assert.True(f <= previous + 1e-5f,
                    "falloff rose at " + d + ": " + previous + " -> " + f);
                previous = f;
            }
        }

        [Fact]
        public void A_big_quake_is_far_worse_than_a_moderate_one()
        {
            // ★★ 「地震の規模に合わせて」。二乗なので、強度 5 倍で被害は 25 倍近い。
            float small = DistantDamage.CollapseChance(0f, 51, Full);
            float big = DistantDamage.CollapseChance(0f, 255, Full);

            Assert.True(big > small * 20f,
                "255 gave " + big + " but 51 gave " + small);
        }

        [Fact]
        public void Fire_is_more_likely_than_collapse()
        {
            // 海溝型で街を焼くのは主に火災（クラス doc）。
            Assert.True(DistantDamage.FireChance(1000f, 200, Full)
                        > DistantDamage.CollapseChance(1000f, 200, Full));
        }

        [Fact]
        public void The_slider_scales_it_and_zero_turns_it_off()
        {
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, 0f));
            Assert.Equal(0f, DistantDamage.FireChance(0f, 255, 0f));

            float half = DistantDamage.CollapseChance(0f, 255, Full / 2f);
            float full = DistantDamage.CollapseChance(0f, 255, Full);
            Assert.Equal(full / 2f, half, 4);

            // 目盛りを超えても倍率は 1 で頭打ち（負の値も 0 で止まる）。
            Assert.Equal(full, DistantDamage.CollapseChance(0f, 255, Full * 3f), 4);
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, -5f));
        }

        [Fact]
        public void It_never_exceeds_its_own_ceiling()
        {
            for (int i = 0; i <= 255; i++)
            {
                float c = DistantDamage.CollapseChance(0f, (byte)i, Full);
                float f = DistantDamage.FireChance(0f, (byte)i, Full);

                Assert.InRange(c, 0f, DistantDamage.MaxCollapseChance);
                Assert.InRange(f, 0f, DistantDamage.MaxFireChance);
            }
        }

        [Fact]
        public void Broken_input_damages_nothing()
        {
            // ★ 0 を返す条件は全て「壊さない」側に倒れていること。
            Assert.Equal(0f, DistantDamage.CollapseChance(float.NaN, 255, Full));
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 255, float.NaN));
            Assert.Equal(0f, DistantDamage.CollapseChance(-100f, 255, Full));
            Assert.Equal(0f, DistantDamage.FireChance(float.NaN, 255, Full));

            // 強度 0 は「災害が読めていない」。何も起こさない。
            Assert.Equal(0f, DistantDamage.CollapseChance(0f, 0, Full));
        }

        [Fact]
        public void It_beats_the_vanilla_disc_where_vanilla_gives_up()
        {
            // ★★ これが依頼の定量的な中身である。強度 100、3 km 沖の街:
            //    バニラは 0.02 x (1 - 3000/4000) = 0.5% しか配らない。
            const byte intensity = 100;
            const float distance = 3000f;

            float vanilla = 0.02f * SeismicIntensity.At(distance, intensity);

            // 既定の強さ（6）で、倒壊と出火を合わせてバニラを上回ること。
            float ours = DistantDamage.CollapseChance(distance, intensity, 6f)
                         + DistantDamage.FireChance(distance, intensity, 6f);

            Assert.True(ours > vanilla,
                "ours " + ours.ToString("F5") + " vs vanilla " + vanilla.ToString("F5"));
        }
    }
}
