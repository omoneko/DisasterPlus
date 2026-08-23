using DisasterPlus.Core.Volcano;
using Xunit;

namespace DisasterPlus.Core.Tests.Volcano
{
    /// <summary>
    /// 実機報告（2026-08-22）「火山の 25.5 スケールが小さすぎるように思います」。
    ///
    /// ★★ <b>原因はスライダーではなく帯だった。</b> 倍率の上端は 255/55 ≒ 4.64 で
    ///    正しく届いていたのに、<see cref="VolcanoShape"/> の最大値が先に頭打ちになり、
    ///    <b>表示 9.2 より上はスライダーを動かしても半径が 1 m も変わらなかった</b>。
    ///
    ///    ここが固定するのは「スライダーのどの位置でも、動かせば大きさが変わる」で
    ///    ある —— 押しても何も変わらない帯を二度と作らないため。
    /// </summary>
    public class VolcanoSliderReachTests
    {
        private static readonly VolcanoForm[] AllForms =
        {
            VolcanoForm.Shield, VolcanoForm.Strato, VolcanoForm.Dome,
        };

        private static float RadiusAt(VolcanoForm form, int raw)
        {
            return VolcanoShape.RadiusFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultRadiusOf(form),
                                             VolcanoSizeScale.ScaleFor(raw)));
        }

        private static float HeightAt(VolcanoForm form, int raw)
        {
            // 海抜 0 m の土地に立てる（天井の余裕をいちばん広く取る）。
            return VolcanoShape.HeightFor(
                form, VolcanoSizeScale.Apply(VolcanoShape.DefaultHeightOf(form),
                                             VolcanoSizeScale.ScaleFor(raw)), 0f);
        }

        [Fact]
        public void TheTopOfTheSliderIsMuchBiggerThanTheDefault()
        {
            foreach (VolcanoForm form in AllForms)
            {
                float atDefault = RadiusAt(form, VolcanoSizeScale.AnchorRaw);
                float atTop = RadiusAt(form, VolcanoSizeScale.MaxRaw);

                // 帯が効く前は 2000/1200 ＝ 1.67 倍しか無かった。
                Assert.True(atTop >= atDefault * 2.5f,
                            form + " only grows from " + atDefault + " m to " + atTop + " m");
            }
        }

        [Fact]
        public void TheTopOfTheSliderIsTallerThanTheDefault()
        {
            foreach (VolcanoForm form in AllForms)
            {
                float atDefault = HeightAt(form, VolcanoSizeScale.AnchorRaw);
                float atTop = HeightAt(form, VolcanoSizeScale.MaxRaw);

                // 成層火山は 700/600 ＝ 1.17 倍しか無かった。**そこが「小さすぎる」の正体。**
                //
                // ★★ <b>高さは倍率いっぱい（4.64 倍）には伸ばせない。</b>
                //   ゲームの地形の天井が海抜 1024 m で、**MOD からは上げられない**
                //   （<c>UpliftSchedule.CeilingMetres</c>）。成層火山の推奨 600 m に
                //   4.64 を掛けると 2784 m ——存在できない山である。
                //   だからここが要求するのは「上端が推奨よりはっきり高い」までで、
                //   **「大きくなった」の主役は半径のほうである**（下の体積の検査）。
                Assert.True(atTop >= atDefault * 1.5f,
                            form + " only grows from " + atDefault + " m to " + atTop + " m");
            }
        }

        [Fact]
        public void TheTopOfTheSliderIsAnOrderOfMagnitudeMoreMountain()
        {
            // ★ 「小さすぎる」に効くのは<b>体積</b>である（r² h）。
            //   帯を上げる前の成層火山は 2000² × 700 / (1200² × 600) ＝ 3.2 倍しか無かった。
            foreach (VolcanoForm form in AllForms)
            {
                float rd = RadiusAt(form, VolcanoSizeScale.AnchorRaw);
                float hd = HeightAt(form, VolcanoSizeScale.AnchorRaw);
                float rt = RadiusAt(form, VolcanoSizeScale.MaxRaw);
                float ht = HeightAt(form, VolcanoSizeScale.MaxRaw);

                float ratio = (rt * rt * ht) / (rd * rd * hd);
                Assert.True(ratio >= 10f,
                            form + " at the top of the slider is only " + ratio
                            + "x the recommended volcano");
            }
        }

        [Fact]
        public void SomethingStillChangesAtTheTopOfTheSlider()
        {
            // ★★ **押しても何も変わらない帯を作らない。**
            //
            //    どちらか片方が先に飽和するのは避けられない ——
            //    成層火山は高さが天井（1024 m）で、楯状火山は半径が実費の上限
            //    （6 km）で止まる。禁じているのは<b>両方いっぺんに止まること</b>で、
            //    そうなった瞬間、プレイヤーからは「スライダーが壊れている」に
            //    しか見えない。
            foreach (VolcanoForm form in AllForms)
            {
                bool radiusMoves = RadiusAt(form, VolcanoSizeScale.MaxRaw - 20)
                                   < RadiusAt(form, VolcanoSizeScale.MaxRaw);
                bool heightMoves = HeightAt(form, VolcanoSizeScale.MaxRaw - 20)
                                   < HeightAt(form, VolcanoSizeScale.MaxRaw);

                Assert.True(radiusMoves || heightMoves,
                            form + " stops changing entirely before the top of the slider");
            }
        }

        [Fact]
        public void NothingReachesTheGamesTerrainCeiling()
        {
            // 天井（1024 m）は MOD からは上げられない。帯がそこを越えていたら、
            // 「設定した高さにならない」が既定の挙動になってしまう。
            foreach (VolcanoForm form in AllForms)
            {
                Assert.True(VolcanoShape.MaxHeightOf(form) < UpliftSchedule.CeilingMetres,
                            form + " can be asked for a summit above the terrain ceiling");
            }
        }

        [Fact]
        public void TheSupereruptionStaysInsideTheCostCeiling()
        {
            // いちばん大きい山でも、膨らみとカルデラの矩形が実費の上限を越えないこと。
            foreach (VolcanoForm form in AllForms)
            {
                float biggest = VolcanoShape.MaxRadiusOf(form);
                Assert.True(SuperEruption.InflationRadiusMetres(biggest)
                            <= SuperEruption.MaxRadiusMetres);
                Assert.True(SuperEruption.CalderaRadiusMetres(biggest)
                            <= SuperEruption.MaxRadiusMetres);
            }
        }

        [Fact]
        public void OnlyTheVeryTopOfTheSliderGetsASupereruption()
        {
            // スライダーを 1 目盛り下げたら、ふつうの噴火に戻ること
            // （所有者の依頼「25.5 のときだけ」）。
            Assert.True(SuperEruption.IsSuper(VolcanoSizeScale.MaxRaw));
            Assert.False(SuperEruption.IsSuper(VolcanoSizeScale.MaxRaw - 1));
            Assert.False(SuperEruption.IsSuper(VolcanoSizeScale.AnchorRaw));
        }
    }
}
