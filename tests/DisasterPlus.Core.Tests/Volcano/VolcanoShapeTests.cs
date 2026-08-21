using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    public class VolcanoShapeTests
    {
        private static readonly VolcanoForm[] AllForms =
            new VolcanoForm[] { VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome };

        [Fact]
        public void AllThreeFormsPeakAtTheCentre()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                Assert.Equal(400f, VolcanoShape.ProfileAt(AllForms[i], 0f, 1000f, 400f), 3);
            }
        }

        [Fact]
        public void NothingIsRaisedBeyondTheRadius()
        {
            // 半径の外に 1 mm でも残ると、UpdateArea の矩形が際限なく広がる。
            for (int i = 0; i < AllForms.Length; i++)
            {
                Assert.Equal(0f, VolcanoShape.ProfileAt(AllForms[i], 1000f, 1000f, 400f), 3);
                Assert.Equal(0f, VolcanoShape.ProfileAt(AllForms[i], 1500f, 1000f, 400f), 3);
            }
        }

        [Fact]
        public void TheProfileNeverGoesBackUp()
        {
            // 単調非増加でないと、山の中腹に環状の尾根ができる。
            for (int i = 0; i < AllForms.Length; i++)
            {
                float previous = float.MaxValue;
                for (float d = 0f; d <= 1000f; d += 5f)
                {
                    float h = VolcanoShape.ProfileAt(AllForms[i], d, 1000f, 400f);
                    Assert.True(h <= previous + 0.001f,
                        AllForms[i] + " rises again at d=" + d);
                    previous = h;
                }
            }
        }

        [Fact]
        public void TheShieldMatchesTheProfileMakeCraterWouldHaveDrawn()
        {
            // §C-8 の実測表（raiseEdges:false、depth に −H）の 3 点をそのまま固定する。
            // ここがずれると「MakeCrater 1 発と同じ形」という前提が嘘になる。
            const float r = 1000f;
            const float h = 100f;
            Assert.Equal(100.00f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0f, r, h), 2);
            Assert.Equal(95.97f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.375f * r, r, h), 1);
            Assert.Equal(36.23f, VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.750f * r, r, h), 1);

            // 内側分岐と外側分岐の境目（0.73R）が連続していること（誤差 7e-4 まで）。
            float inner = VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.7299f * r, r, h);
            float outer = VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.7301f * r, r, h);
            Assert.True(System.Math.Abs(inner - outer) < 0.01f,
                "the shield profile jumps at the 0.73R branch: " + inner + " vs " + outer);
        }

        [Fact]
        public void EachFormHasItsOwnSignature()
        {
            const float r = 1000f;
            const float h = 400f;

            // 盾状: 頂が平ら。半径の半分でもまだ 8 割以上残っている。
            Assert.True(VolcanoShape.ProfileAt(VolcanoForm.Shield, 0.5f * r, r, h) > 0.80f * h);
            // 成層: 直線。半径の半分でちょうど半分。
            Assert.Equal(0.5f * h, VolcanoShape.ProfileAt(VolcanoForm.Strato, 0.5f * r, r, h), 2);
            // ドーム: 0.7071R で 1/4。
            Assert.Equal(0.25f * h, VolcanoShape.ProfileAt(VolcanoForm.Dome, 0.70711f * r, r, h), 1);

            // 「ドームが急峻」なのはプロファイルではなく**半径が小さい**から（§C-9）。
            // 既定値の平均勾配が 盾状 < 成層 < ドーム の順であること。
            float shield = VolcanoShape.DefaultHeightOf(VolcanoForm.Shield)
                         / VolcanoShape.DefaultRadiusOf(VolcanoForm.Shield);
            float strato = VolcanoShape.DefaultHeightOf(VolcanoForm.Strato)
                         / VolcanoShape.DefaultRadiusOf(VolcanoForm.Strato);
            float dome = VolcanoShape.DefaultHeightOf(VolcanoForm.Dome)
                       / VolcanoShape.DefaultRadiusOf(VolcanoForm.Dome);
            Assert.True(shield < strato, "shield must be the gentlest");
            Assert.True(strato < dome, "the dome must be the steepest");
        }

        [Fact]
        public void TheDomeRadiusIsNeverAllowedBelowTwoHundredAndFiftyMetres()
        {
            // ★ §C-9: 250 m は片側 8 raw セル。それ以下は「山」ではなく地面のノイズ。
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.MinRadiusOf(VolcanoForm.Dome), 3);
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.RadiusFor(VolcanoForm.Dome, 10f), 3);
            Assert.Equal(VolcanoShape.MinDomeRadiusMetres,
                         VolcanoShape.RadiusFor(VolcanoForm.Dome, -5f), 3);
            Assert.True(VolcanoShape.RadiusFor(VolcanoForm.Dome, 100000f)
                        <= VolcanoShape.MaxRadiusOf(VolcanoForm.Dome));
        }

        [Fact]
        public void TheSummitNeverReachesTheTerrainCeiling()
        {
            // ★ §C-10: 天井に当たっても例外は出ず、山頂が無言で平らな台地になる。
            //   ⑤が書く最大の高さは base + H ちょうどである（火口の縁が H で、
            //   そこから上には 1 mm も書かない。VolcanoCrater のクラス doc）。
            const float baseHeight = 900f;
            float h = VolcanoShape.HeightFor(VolcanoForm.Strato, 700f, baseHeight);
            Assert.True(baseHeight + h <= VolcanoShape.MaxTerrainMetres,
                "the summit would be clipped at " + (baseHeight + h));
            Assert.True(VolcanoShape.HeightWasLimitedByCeiling(VolcanoForm.Strato, 700f, baseHeight));

            // 海面 40 m の平地なら 700 m は素通りする（§C-10 の 983.98 m の余裕）。
            Assert.Equal(700f, VolcanoShape.HeightFor(VolcanoForm.Strato, 700f, 40f), 2);
            Assert.False(VolcanoShape.HeightWasLimitedByCeiling(VolcanoForm.Strato, 700f, 40f));
        }

        [Fact]
        public void RadiusAndHeightAreClampedToTheDeclaredRange()
        {
            // .cgs は公開契約で、手で編集されうる。範囲外でも読み捨てずクランプする。
            for (int i = 0; i < AllForms.Length; i++)
            {
                var f = AllForms[i];
                Assert.InRange(VolcanoShape.RadiusFor(f, -1f),
                               VolcanoShape.MinRadiusOf(f), VolcanoShape.MaxRadiusOf(f));
                Assert.InRange(VolcanoShape.RadiusFor(f, 999999f),
                               VolcanoShape.MinRadiusOf(f), VolcanoShape.MaxRadiusOf(f));
                Assert.InRange(VolcanoShape.HeightFor(f, -1f, 40f),
                               VolcanoShape.MinHeightOf(f), VolcanoShape.MaxHeightOf(f));
                Assert.InRange(VolcanoShape.HeightFor(f, 999999f, 40f),
                               VolcanoShape.MinHeightOf(f), VolcanoShape.MaxHeightOf(f));
            }
        }

        [Fact]
        public void GarbageInputIsZeroNotNaN()
        {
            for (int i = 0; i < AllForms.Length; i++)
            {
                var f = AllForms[i];
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, float.NaN, 1000f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, float.NaN, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 1000f, float.NaN), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, -1f, 1000f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 0f, 400f), 4);
                Assert.Equal(0f, VolcanoShape.ProfileAt(f, 100f, 1000f, 0f), 4);
            }
            Assert.Equal(0f, VolcanoShape.HeadroomMetres(float.NaN), 4);
        }

        [Fact]
        public void AnOutOfRangeSettingValueFallsBackToTheDefaultForm()
        {
            // 設定値は .cgs に保存される公開契約。番号は詰め直さない。
            Assert.Equal(VolcanoForm.Shield, VolcanoShape.FormOf(0));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(1));
            Assert.Equal(VolcanoForm.Dome, VolcanoShape.FormOf(2));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(-1));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(3));
            Assert.Equal(VolcanoForm.Strato, VolcanoShape.FormOf(int.MaxValue));
        }

        [Fact]
        public void TheCraterIsSmallerAndShallowerThanTheMountain()
        {
            for (float h = 50f; h <= 700f; h += 25f)
            {
                Assert.True(VolcanoShape.CraterDepthOf(h) < h,
                    "the crater would punch through the mountain at H=" + h);
                Assert.True(VolcanoShape.CraterDepthOf(h) > 0f);
            }
            for (float r = 250f; r <= 3000f; r += 50f)
            {
                Assert.True(VolcanoShape.CraterRadiusOf(r) < r);
                // 火口の矩形が 128 セル（2048 m）を跨がないこと。
                Assert.True(VolcanoShape.CraterRadiusOf(r) <= 400f);
            }
            Assert.Equal(0f, VolcanoShape.CraterDepthOf(float.NaN), 4);
            Assert.Equal(0f, VolcanoShape.CraterRadiusOf(float.NaN), 4);

            // ★ 低い山では「高さの 45 %」の上限のほうが効く（10 m の下限より優先）。
            //   これが無いと 15 m の山に 10 m の穴が空き、火口の底が元の地面まで抜ける。
            Assert.True(VolcanoShape.CraterDepthOf(15f) <= 15f * 0.45f + 0.001f);
            Assert.True(VolcanoShape.CraterDepthOf(15f) > 0f);
        }
    }
}
