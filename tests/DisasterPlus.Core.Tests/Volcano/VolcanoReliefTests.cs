using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 山肌の凹凸。**固定しているのは 2 つの硬い制約と「軸対称でないこと」**である。
    /// 起伏の見た目そのものは tools/VolcanoPreview が描いて目で確かめる。
    /// </summary>
    public class VolcanoReliefTests
    {
        private static readonly VolcanoForm[] AllForms =
            new VolcanoForm[] { VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome };

        private const uint Seed = 0x1A2B3C4Du;

        [Fact]
        public void StrengthZeroIsExactlyTheProfileWeShipToday()
        {
            // ★★ 設定を 0 にした人が得るのは「起伏の小さい山」ではなく**今日の出力そのもの**。
            //    1 bit でもずれると「戻せる」という約束が嘘になる。
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, 0f);

                for (float dz = -r - 32f; dz <= r + 32f; dz += 16f)
                {
                    for (float dx = -r - 32f; dx <= r + 32f; dx += 16f)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        Assert.Equal(VolcanoShape.ProfileAt(form, d, r, h),
                                     relief.ProfileAt(dx, dz, r, h));
                    }
                }
            }
        }

        [Fact]
        public void NegativeAndNaNStrengthFallBackToTheSmoothCone()
        {
            // .cgs は手で編集されうる。読み捨てず、いちばん安全な側（今日の形）へ落とす。
            foreach (float bad in new float[] { -1f, float.NaN })
            {
                var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, bad);
                Assert.Equal(0f, relief.StrengthUnit, 6);
                Assert.Equal(VolcanoShape.ProfileAt(VolcanoForm.Strato, 400f, 1200f, 600f),
                             relief.ProfileAt(400f, 0f, 1200f, 600f));
            }
        }

        [Fact]
        public void StrengthIsClampedToTheDeclaredCeiling()
        {
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 99f);
            Assert.Equal(VolcanoRelief.MaxStrengthUnit, relief.StrengthUnit, 5);
        }

        [Fact]
        public void NothingIsRaisedBeyondTheRadius()
        {
            // ★★ 制約 1。半径の外に 1 mm でも残ると、準備が届いていないセルを持ち上げる
            //    ことになり、道路が毎フラッシュ押し戻して山の中に平らな溝が残る（設計書 §1.2）。
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.DefaultRadiusOf(form);
                float h = VolcanoShape.DefaultHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, VolcanoRelief.MaxStrengthUnit);

                // 16 m 格子の全セルを見る。**判定は「そのセルの実際の距離」で行う** ——
                // 極座標で作った点は丸めで半径のわずかに内側へ落ちることがあり、
                // それは「半径の外」ではない。
                for (float dz = -r - 64f; dz <= r + 64f; dz += 16f)
                {
                    for (float dx = -r - 64f; dx <= r + 64f; dx += 16f)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dz * dz);
                        if (d < r) continue;
                        Assert.Equal(0f, relief.ProfileAt(dx, dz, r, h));
                    }
                }

                // 円周のすぐ外側も 1 mm も残らないこと。
                for (int a = 0; a < 720; a++)
                {
                    double th = 2.0 * Math.PI * a / 720.0;
                    foreach (float d in new float[] { r + 1f, r + 16f, r * 2f })
                    {
                        Assert.Equal(0f, relief.ProfileAt((float)(Math.Cos(th) * d),
                                                          (float)(Math.Sin(th) * d), r, h));
                    }
                }
            }
        }

        [Fact]
        public void TheProfileNeverExceedsTheFinalHeight()
        {
            // ★★ 制約 2。HeightFor / HeadroomMetres と 1024 m の生の天井（§C-10）が
            //    全部 H を基準に考えている。**渡された H を超えない**が約束であって、
            //    火口のぶん立て直した仮想の頂を渡すのは呼び出し側の話である
            //    （VolcanoCrater が必ず切り落とす）。
            for (int i = 0; i < AllForms.Length; i++)
            {
                VolcanoForm form = AllForms[i];
                float r = VolcanoShape.MaxRadiusOf(form);
                float h = VolcanoShape.MaxHeightOf(form);
                var relief = VolcanoRelief.For(form, Seed, VolcanoRelief.MaxStrengthUnit);

                for (float dz = -r; dz <= r; dz += 16f)
                {
                    for (float dx = -r; dx <= r; dx += 16f)
                    {
                        float v = relief.ProfileAt(dx, dz, r, h);
                        Assert.InRange(v, 0f, h);
                    }
                }
            }
        }

        [Fact]
        public void TheSummitIsStillExactlyTheFinalHeight()
        {
            // 山頂がずれると、火口の縁が山の高さ H に届かなくなる
            //    （VolcanoCrater.SummitScale はここが厳密に H であることを前提にしている）。
            for (int i = 0; i < AllForms.Length; i++)
            {
                var relief = VolcanoRelief.For(AllForms[i], Seed, 1f);
                Assert.Equal(400f, relief.ProfileAt(0f, 0f, 1000f, 400f), 4);
            }
        }

        [Fact]
        public void TheShapeIsNoLongerAxisymmetric()
        {
            // ★★ 本タスクそのもの。**同じ距離なら方位によらず同じ高さ**という
            //    「旋盤で挽いた円錐」を壊したことを固定する。
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float min = float.MaxValue, max = float.MinValue;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float d = 0.55f * r;
                float v = relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h);
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float smooth = VolcanoShape.ProfileAt(VolcanoForm.Strato, 0.55f * r, r, h);
            Assert.True(max - min > 0.15f * smooth,
                "the flank is still axisymmetric: spread " + (max - min) + " m at 0.55R");
        }

        [Fact]
        public void TheFootprintIsNotACircle()
        {
            // 裾が真円だと「山を置いた」ではなく「円盤を置いた」に見える。
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float shortest = float.MaxValue, longest = 0f;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float reach = 0f;
                for (float d = 0f; d <= r; d += 4f)
                {
                    if (relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h) > 0f)
                    {
                        reach = d;
                    }
                }
                if (reach < shortest) shortest = reach;
                if (reach > longest) longest = reach;
            }

            Assert.True(longest <= r, "the footprint reaches " + longest + " m, past R = " + r);
            Assert.True(longest - shortest > 0.04f * r,
                "the footprint is still a circle: " + shortest + " m .. " + longest + " m");
        }

        [Fact]
        public void TheGulliesAreShallowNearTheSummitAndDeepenDownslope()
        {
            // 「上から下へ走る溝」の向きそのもの。逆になると山頂が刻まれた別の山になる。
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            Assert.True(RelativeSpread(relief, r, h, 0.15f) < RelativeSpread(relief, r, h, 0.60f),
                "the relief is not deeper downslope");
        }

        [Fact]
        public void TheAzimuthalReliefIsSuppressedWhereItWouldAliasOnTheSixteenMetreGrid()
        {
            // ★ 山頂に近いほど方位の波長は短い（円周 ÷ 谷の本数）。16 m 格子に
            //   食い込むところで消していないと、山頂まわりが起伏ではなく市松模様になる。
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, VolcanoRelief.MaxStrengthUnit);

            // 半径 48 m（= 3 セル）のところでは方位方向の起伏がほぼ無いこと。
            Assert.True(RelativeSpread(relief, r, h, 48f / r) < 0.02f,
                "the summit ring still carries azimuthal relief and will alias");
        }

        [Fact]
        public void TheShieldStaysCloserToTheSmoothConeThanTheStratovolcano()
        {
            // 形態ごとの性格。盾状火山は実際になめらかで、放射谷は成層火山のものである。
            var shield = VolcanoRelief.For(VolcanoForm.Shield, Seed, 1f);
            var strato = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            float shieldSpread = RelativeSpread(shield, VolcanoShape.DefaultRadiusOf(VolcanoForm.Shield),
                                                VolcanoShape.DefaultHeightOf(VolcanoForm.Shield), 0.55f);
            float stratoSpread = RelativeSpread(strato, VolcanoShape.DefaultRadiusOf(VolcanoForm.Strato),
                                                VolcanoShape.DefaultHeightOf(VolcanoForm.Strato), 0.55f);

            Assert.True(shieldSpread < stratoSpread,
                "the shield is not the smoothest: " + shieldSpread + " vs " + stratoSpread);
        }

        [Fact]
        public void NoWavelengthGoesBelowTheGridsLimit()
        {
            // 16 m 格子で 64 m を割る成分は起伏ではなくノイズに化ける。
            for (int i = 0; i < AllForms.Length; i++)
            {
                var relief = VolcanoRelief.For(AllForms[i], Seed, 1f);
                Assert.True(relief.FineWavelengthMetres >= VolcanoRelief.MinWavelengthMetres,
                    AllForms[i] + " uses " + relief.FineWavelengthMetres + " m");
                Assert.True(relief.FineWavelengthMetres
                            >= 4f * VolcanoShape.RawCellSizeMetres);

                // ★ 4 オクターブ目（いちばん細かい肌）も同じ床を割らないこと。
                Assert.True(relief.MicroWavelengthMetres >= VolcanoRelief.MinWavelengthMetres,
                    AllForms[i] + " micro octave uses " + relief.MicroWavelengthMetres + " m");
                Assert.True(relief.MicroWavelengthMetres
                            >= 4f * VolcanoShape.RawCellSizeMetres);
            }
        }

        [Fact]
        public void TheLowerFlankCarriesMoreChannelsThanTheMainGulliesAlone()
        {
            // 「太い谷の筋だけ」への答え。裾の円周を回って谷の数を数えると、
            // 主谷の本数より**多く**なければ細谷は 1 本も効いていない。
            const float r = 1200f, h = 600f;
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);

            Assert.True(TroughsAround(relief, r, h, 0.85f) > relief.GullyCount,
                "the lower flank has no finer channels than the main gullies");
        }

        [Fact]
        public void SmallConesGetNoRillsAtAll()
        {
            // 溶岩ドーム（既定 R = 350 m）では方位の波長がどこでも足りない。
            // **「無い」を黙って「在る」ことにしない。**
            var relief = VolcanoRelief.For(VolcanoForm.Dome, Seed, 1f);
            Assert.True(relief.RillOnsetRadiusMetres
                        > VolcanoShape.DefaultRadiusOf(VolcanoForm.Dome),
                "the lava dome claims rills the 16 m grid cannot carry");

            // ★ 出はじめの半径は方位の床（96 m）と細谷の次数だけで決まる。
            //   16 m 格子が担げるところより内側へ勝手に降りてこないこと。
            Assert.True(relief.RillOnsetRadiusMetres
                        >= VolcanoRelief.MinAzimuthWavelengthMetres * relief.RillCount
                           / 6.2831853f,
                "the onset radius is closer to the summit than the azimuthal floor allows");
        }

        /// <summary>半径 <paramref name="fraction"/>R の円周に沿った谷の数（極小の数）。</summary>
        private static int TroughsAround(VolcanoRelief relief, float r, float h, float fraction)
        {
            float ring = fraction * r;
            int reversals = 0;
            int sign = 0;
            float previous = 0f;

            for (int a = 0; a < 1440; a++)
            {
                double th = 2.0 * Math.PI * a / 1440.0;
                float v = relief.ProfileAt((float)(Math.Cos(th) * ring),
                                           (float)(Math.Sin(th) * ring), r, h);
                if (a > 0)
                {
                    float delta = v - previous;
                    int next = delta > 0f ? 1 : (delta < 0f ? -1 : sign);
                    if (sign != 0 && next != 0 && next != sign) reversals++;
                    sign = next;
                }
                previous = v;
            }

            return reversals / 2;
        }

        [Fact]
        public void TheSameSeedAlwaysGivesTheSameMountain()
        {
            // 同じ地点に作り直したら同じ山が生えること（セーブ・ロードとテストの再現性）。
            var a = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            var b = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            var other = VolcanoRelief.For(VolcanoForm.Strato, Seed ^ 0xFFFFu, 1f);

            bool differs = false;
            for (float dz = -1200f; dz <= 1200f; dz += 64f)
            {
                for (float dx = -1200f; dx <= 1200f; dx += 64f)
                {
                    float va = a.ProfileAt(dx, dz, 1200f, 600f);
                    Assert.Equal(va, b.ProfileAt(dx, dz, 1200f, 600f));
                    if (Math.Abs(va - other.ProfileAt(dx, dz, 1200f, 600f)) > 0.5f) differs = true;
                }
            }
            Assert.True(differs, "a different seed produced the same mountain");
        }

        [Fact]
        public void BadInputIsZeroAndNeverNaN()
        {
            var relief = VolcanoRelief.For(VolcanoForm.Strato, Seed, 1f);
            Assert.Equal(0f, relief.ProfileAt(float.NaN, 0f, 1200f, 600f));
            Assert.Equal(0f, relief.ProfileAt(0f, float.NaN, 1200f, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, float.NaN, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 1200f, float.NaN));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 0f, 600f));
            Assert.Equal(0f, relief.ProfileAt(10f, 10f, 1200f, 0f));
        }

        /// <summary>その距離の円周上での高さのばらつき（滑らかな円錐の高さに対する比）。</summary>
        private static float RelativeSpread(VolcanoRelief relief, float r, float h, float t)
        {
            float d = t * r;
            float min = float.MaxValue, max = float.MinValue;
            for (int a = 0; a < 360; a++)
            {
                double th = Math.PI * a / 180.0;
                float v = relief.ProfileAt((float)(Math.Cos(th) * d), (float)(Math.Sin(th) * d), r, h);
                if (v < min) min = v;
                if (v > max) max = v;
            }

            float smooth = VolcanoShape.ProfileAt(VolcanoForm.Strato, d, r, h);
            if (!(smooth > 0f)) return 0f;
            return (max - min) / smooth;
        }

        [Fact]
        public void TheSeedComesFromTheDeterministicHashNotSystemRandom()
        {
            // Core は engine-free で、net35 と net8.0 で同じ値を出さなければならない。
            // 種の作り方（DeterministicRandom.Hash(round(X), round(Z))）を固定しておく。
            uint seed = DeterministicRandom.Hash(275u, unchecked((uint)(-283)));
            var a = VolcanoRelief.For(VolcanoForm.Strato, seed, 1f);
            var b = VolcanoRelief.For(VolcanoForm.Strato,
                                      DeterministicRandom.Hash(275u, unchecked((uint)(-283))), 1f);
            Assert.Equal(a.ProfileAt(300f, 200f, 1200f, 600f),
                         b.ProfileAt(300f, 200f, 1200f, 600f));
        }
    }
}
