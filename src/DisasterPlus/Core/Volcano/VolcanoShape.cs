namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 火山の形態。**値は設定ファイル（.cgs）に保存される公開契約なので詰め直さない。**
    /// 番号を入れ替えると、既に保存されている設定が黙って別の形態に化ける。
    /// </summary>
    public enum VolcanoForm
    {
        /// <summary>楯状火山。裾が広く頂が平ら（既定 R=2000 m / H=200 m、平均勾配 1:10）。</summary>
        Shield = 0,

        /// <summary>成層火山。直線の円錐（既定 R=1200 m / H=600 m、平均勾配 1:2）。**⑤の既定。**</summary>
        Strato = 1,

        /// <summary>溶岩ドーム。小さく急峻（既定 R=350 m / H=300 m、平均勾配 1:1.17）。</summary>
        Dome = 2,
    }

    /// <summary>
    /// 3 形態の高さプロファイル。**ここにある数字は全て本 MOD が発明したものである。**
    /// バニラに「火山」という現象は無く、溶岩・マグマ・溶融物のプレハブもマテリアルも
    /// シェーダも、DLL の文字列ヒープにすら 1 件も存在しない（IL 事実文書 §B-5）。
    ///
    /// 唯一の例外が<b>盾状火山のプロファイル</b>で、これは
    /// DisasterHelpers.MakeCrater(pos, R, -H, raiseEdges:false) が実際に描く形
    /// （§C-8 の実測式 1 - (d/0.837R)^4 と、0.73R 以遠の 4(u-1.197)^2）を
    /// **そのまま写したもの**である。
    ///
    /// **ではなぜ MakeCrater を呼ばないのか。** MakeCrater は先頭で
    /// TerrainModify.RefreshAllModifications() を呼ぶ（§C-8 IL_0006）ため、
    /// **呼ぶたびに地形の強制フラッシュが 1 回走る**。段階的隆起で毎 tick 呼べば
    /// バニラのバッチ最適化を毎 tick 無効化することになる（§A-1 の設計上の結論 3）。
    /// 形は同じで、コストだけが違う。**「MakeCrater 1 発で済む」と言って
    /// 戻さないこと。**
    ///
    /// ★ 山頂の火口も 2026-08-22 に <c>MakeCrater</c> をやめた（実機の指摘①
    /// 「噴火口が最初から窪みとして生成される方がいい」）。火口は
    /// <see cref="VolcanoCrater"/> が**高さプロファイルの一部**として返すので、
    /// 山と一緒に育つ。⑤は <c>MakeCrater</c> をもうどこからも呼ばない。
    ///
    /// 勾配クランプも侵食も平滑化パスも存在しない（§C-9）ので、急峻な円錐は潰れない。
    /// 制約は「16 m 格子」と「1/64 m 量子」の 2 つだけである。だから
    /// **溶岩ドームの半径を 250 m（片側 8 raw セル）より小さくしない**。
    /// それ以下は「山」ではなく地面のノイズに見える。
    ///
    /// 高さの天井は 65535/64 = 1023.984375 m で、**超えても例外は出ず無言で
    /// 山頂が平らな台地になる**（§C-10）。<see cref="HeightFor"/> が起点の地形高さを
    /// 先に引き、<see cref="HeightWasLimitedByCeiling"/> が
    /// 「切ったかどうか」を別に返す。呼び出し側はそれをプレイヤーに先に見せること。
    /// </summary>
    public static class VolcanoShape
    {
        /// <summary>1 メートルあたりの raw 単位（<c>RawHeights</c> は <c>raw/64</c> メートル、§C-8）。</summary>
        public const float RawUnitsPerMetre = 64f;

        /// <summary>raw 1 単位のメートル値（= 0.015625 m）。**これ未満の変化は丸めで消える。**</summary>
        public const float MetresPerRawUnit = 1f / 64f;

        /// <summary>地形高さの表現上限（= 65535/64 m、§C-10）。超えても例外は出ない。</summary>
        public const float MaxTerrainMetres = 1023.984375f;

        /// <summary>raw セルの一辺（m）。§C-8 の <c>cell = 16</c>。</summary>
        public const float RawCellSizeMetres = 16f;

        /// <summary>溶岩ドームの最小半径（m）。片側 8 raw セル。§C-9。</summary>
        public const float MinDomeRadiusMetres = 250f;

        /// <summary>盾状の減衰基準（§C-8 の <c>radius * 0.837</c>）。</summary>
        public const float ShieldFalloff = 0.837f;

        /// <summary>盾状の内側分岐の境目（§C-8 の <c>d &lt; radius * 0.73</c>）。</summary>
        public const float ShieldInnerFraction = 0.73f;

        /// <summary>盾状の外側分岐のオフセット（§C-8 の <c>u - 1.197</c>）。</summary>
        public const float ShieldOuterOffset = 1.197f;

        /// <summary>火口半径の比（山の半径に対して）。上下限つき。</summary>
        private const float CraterRadiusFraction = 0.12f;

        /// <summary>火口深さの比（山の高さに対して）。上下限つき。</summary>
        private const float CraterDepthFraction = 0.12f;

        /// <summary>
        /// 火口の深さが山の高さに対して超えてはいけない比。**低い山を貫かないため**で、
        /// これが無いと <see cref="MinCraterDepthMetres"/>（10 m）が 15 m の山に
        /// 10 m の穴を空け、火口の底が元の地面まで抜ける。
        /// </summary>
        private const float MaxCraterDepthOfHeight = 0.45f;

        private const float MinCraterRadiusMetres = 40f;
        private const float MaxCraterRadiusMetres = 400f;
        private const float MinCraterDepthMetres = 10f;
        private const float MaxCraterDepthMetres = 60f;

        /// <summary>
        /// 設定値（.cgs に保存される int）から形態へ。**範囲外は既定の
        /// <see cref="VolcanoForm.Strato"/> に落とす**（設定ファイルは手で編集されうる）。
        /// </summary>
        public static VolcanoForm FormOf(int settingValue)
        {
            switch (settingValue)
            {
                case 0: return VolcanoForm.Shield;
                case 2: return VolcanoForm.Dome;
                default: return VolcanoForm.Strato;
            }
        }

        /// <summary>形態ごとの既定半径（m）。</summary>
        public static float DefaultRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 2000f;
                case VolcanoForm.Dome: return 350f;
                default: return 1200f;
            }
        }

        /// <summary>形態ごとの最小半径（m）。ドームだけ <see cref="MinDomeRadiusMetres"/>（§C-9）。</summary>
        public static float MinRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 800f;
                case VolcanoForm.Dome: return MinDomeRadiusMetres;
                default: return 400f;
            }
        }

        /// <summary>形態ごとの最大半径（m）。</summary>
        public static float MaxRadiusOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 3000f;
                case VolcanoForm.Dome: return 800f;
                default: return 2000f;
            }
        }

        /// <summary>形態ごとの既定高さ（m）。</summary>
        public static float DefaultHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 200f;
                case VolcanoForm.Dome: return 300f;
                default: return 600f;
            }
        }

        /// <summary>形態ごとの最小高さ（m）。</summary>
        public static float MinHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 50f;
                default: return 100f;
            }
        }

        /// <summary>形態ごとの最大高さ（m）。</summary>
        public static float MaxHeightOf(VolcanoForm form)
        {
            switch (form)
            {
                case VolcanoForm.Shield: return 300f;
                case VolcanoForm.Dome: return 400f;
                default: return 700f;
            }
        }

        /// <summary>
        /// 要求半径を形態の帯へクランプする。NaN は既定値。
        /// **読み捨てない** —— .cgs は公開契約で、手で編集されうる。
        /// </summary>
        public static float RadiusFor(VolcanoForm form, float requestedRadiusMetres)
        {
            if (float.IsNaN(requestedRadiusMetres)) return DefaultRadiusOf(form);
            return Clamp(requestedRadiusMetres, MinRadiusOf(form), MaxRadiusOf(form));
        }

        /// <summary>
        /// 起点の地形高さから天井までの余裕（m）。NaN は 0。負にはならない。
        /// </summary>
        public static float HeadroomMetres(float baseHeightMetres)
        {
            if (float.IsNaN(baseHeightMetres)) return 0f;
            float headroom = MaxTerrainMetres - baseHeightMetres;
            return headroom > 0f ? headroom : 0f;
        }

        /// <summary>
        /// 要求高さを形態の帯へクランプしたうえで、**天井（§C-10）の分だけさらに切る**。
        ///
        /// ★ かつてここは火口の縁の余裕（<c>CraterRimHeadroomOf</c>）も引いていた。
        ///   <c>MakeCrater(raiseEdges:true)</c> が <b>山頂より上に</b> 環状の縁を盛っていて、
        ///   その分が天井を突き抜けると縁だけが無言で平らになったからである。
        ///   火口を高さプロファイルへ畳み込んだ（<see cref="VolcanoCrater"/>）いま、
        ///   **⑤が書く最大の高さはちょうど <c>base + H</c> である** ——
        ///   火口の縁が H そのもので、そこから上には 1 mm も書かない。
        ///   したがって引くものはもう無い。
        ///
        /// 標高の高い場所では <c>limit</c> が <see cref="MinHeightOf"/> を割ることがある。
        /// そのときは最小値へ引き上げず、**そのまま小さい値を返す** ——
        /// 呼び出し側が <see cref="HeightWasLimitedByCeiling"/> を見て
        /// 「この場所には作れません」と先に断るためである。
        /// </summary>
        public static float HeightFor(VolcanoForm form, float requestedHeightMetres,
                                      float baseHeightMetres)
        {
            float h = float.IsNaN(requestedHeightMetres)
                ? DefaultHeightOf(form)
                : Clamp(requestedHeightMetres, MinHeightOf(form), MaxHeightOf(form));

            float limit = HeadroomMetres(baseHeightMetres);
            if (limit < h) h = limit;
            return Clamp(h, 0f, MaxHeightOf(form));
        }

        /// <summary>
        /// 天井のせいで要求より低い山になるか。**黙って低い山を作らない**ための口。
        /// </summary>
        public static bool HeightWasLimitedByCeiling(VolcanoForm form, float requestedHeightMetres,
                                                     float baseHeightMetres)
        {
            float requested = float.IsNaN(requestedHeightMetres)
                ? DefaultHeightOf(form)
                : Clamp(requestedHeightMetres, MinHeightOf(form), MaxHeightOf(form));
            return HeightFor(form, requestedHeightMetres, baseHeightMetres) < requested;
        }

        /// <summary>
        /// 中心から <paramref name="distanceMetres"/> の地点での**地形からの盛り上がり**（m）。
        ///
        /// **半径の外はきっかり 0 を返す。** 1 mm でも残ると <c>UpdateArea</c> の矩形が
        /// 際限なく広がる。異常入力（NaN・負の距離・R&lt;=0・H&lt;=0）も 0。
        /// </summary>
        public static float ProfileAt(VolcanoForm form, float distanceMetres,
                                      float radiusMetres, float heightMetres)
        {
            if (float.IsNaN(distanceMetres) || float.IsNaN(radiusMetres) || float.IsNaN(heightMetres)) return 0f;
            if (distanceMetres < 0f || radiusMetres <= 0f || heightMetres <= 0f) return 0f;
            if (distanceMetres >= radiusMetres) return 0f;

            switch (form)
            {
                case VolcanoForm.Shield:
                    return ShieldProfile(distanceMetres, radiusMetres, heightMetres);

                case VolcanoForm.Dome:
                {
                    float t = distanceMetres / radiusMetres;
                    float k = 1f - t * t;
                    return heightMetres * k * k;
                }

                default:
                    return heightMetres * (1f - distanceMetres / radiusMetres);
            }
        }

        /// <summary>
        /// §C-8 の <c>MakeCrater(pos, R, -H, raiseEdges:false)</c> の実測式そのもの。
        /// 内側 <c>H(1 - u^4)</c>、0.73R 以遠 <c>4H(u - 1.197)^2</c>（u = d / 0.837R）。
        /// </summary>
        private static float ShieldProfile(float distanceMetres, float radiusMetres,
                                           float heightMetres)
        {
            float u = distanceMetres / (ShieldFalloff * radiusMetres);
            if (distanceMetres < ShieldInnerFraction * radiusMetres)
            {
                float u2 = u * u;
                return heightMetres * (1f - u2 * u2);
            }

            float w = u - ShieldOuterOffset;
            return heightMetres * 4f * w * w;
        }

        /// <summary>山頂の火口の半径（m）。**上限 400 m** ——
        /// 火口 1 個の矩形が 128 セル（2048 m）を跨がないようにするため。</summary>
        public static float CraterRadiusOf(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return 0f;
            return Clamp(radiusMetres * CraterRadiusFraction,
                         MinCraterRadiusMetres, MaxCraterRadiusMetres);
        }

        /// <summary>
        /// 山頂の火口の深さ（m）。**山を貫かないよう 2 段の上限**がある ——
        /// 絶対値の <see cref="MaxCraterDepthMetres"/> と、山の高さに対する
        /// <see cref="MaxCraterDepthOfHeight"/> である。後者が無いと、天井ぎりぎりで
        /// 15 m しか立てられなかった山に 10 m の穴が空く。
        /// </summary>
        public static float CraterDepthOf(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0f;

            float depth = Clamp(heightMetres * CraterDepthFraction,
                                MinCraterDepthMetres, MaxCraterDepthMetres);

            float limit = heightMetres * MaxCraterDepthOfHeight;
            return depth < limit ? depth : limit;
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
