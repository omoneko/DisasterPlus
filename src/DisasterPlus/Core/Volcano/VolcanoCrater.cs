using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 山頂の火口。**高さプロファイルの一部**であって、あとから彫る穴ではない。
    ///
    /// ── なぜ <c>MakeCrater</c> をやめたのか（2026-08-22、実機の指摘①）────────────
    ///
    /// 所有者の指摘:
    ///
    /// > 噴火口が一番最後に生成されるのではなく最初から窪みとして生成される方がいいと思います。
    ///
    /// 以前は隆起の**最後に 1 回だけ** <c>DisasterHelpers.MakeCrater</c> を呼んでいた。
    /// あれは先頭で <c>TerrainModify.RefreshAllModifications()</c> を呼ぶ（IL 事実 §C-8）ので
    /// **呼ぶたびに地形の強制フラッシュが 1 回走り**、毎 tick の経路には置けない。
    /// だから「最後に 1 回」だったのであって、**それが窪みの生まれる時刻を決めていた**。
    ///
    /// 火口を<b>プロファイルそのものに畳み込む</b>と、この制約はまるごと消える。
    /// 隆起は毎 tick「その時刻における絶対目標」を書いているので（<see cref="UpliftSchedule"/>）、
    /// 目標の形に最初から窪みが在れば、窪みも山と一緒に育つ。
    /// <c>MakeCrater</c> は⑤のどこからも呼ばれなくなった。
    ///
    /// ── 2 つの硬い制約は掛け算と <c>min</c> だけで守る ────────────────────
    ///
    /// <list type="number">
    /// <item><b>半径 R を 1 mm も超えない</b> —— 準備（破壊）が届いていない場所を持ち上げると
    ///   道路がセルを引き戻し、山の中に平らな溝が残る（設計書 §1.2）。
    ///   火口は <c>min</c> でしか働かないので、R の外の 0 は 0 のままである。</item>
    /// <item><b>最終高 H を 1 mm も超えない</b> —— 天井 1023.98 m（§C-10）と
    ///   <see cref="VolcanoShape.HeightFor"/> が H を基準に考えている。
    ///   <see cref="CeilingMetres"/> は必ず H 以下なので、
    ///   <c>min(起伏, 天井)</c> も必ず H 以下である。</item>
    /// </list>
    ///
    /// ── ★★ 円錐は「火口の縁で H に届く」ように立てる（<see cref="SummitScale"/>）────
    ///
    /// 素朴に「円錐から窪みを引く」と、**成層火山では火口が消える**。
    /// 円錐は火口半径 <c>cr = 0.12R</c> のあいだに <c>H × 0.12</c>（既定で 72 m）下がるのに、
    /// 火口の深さは <c>CraterDepthOf</c> の上限で 60 m しかない。引き算では
    /// 中心が縁より高いままになり、窪みではなく**丸い頂**になる（実測で確認した）。
    ///
    /// 正しい形は「火口は円錐の頂を切り落とした跡である」という現実そのもので、
    /// **縁の高さが山の高さ**である。したがって円錐は
    ///
    /// <code>
    /// coneHeight = H / profileFraction(cr)      profileFraction = VolcanoShape.ProfileAt(form, cr, R, 1)
    /// </code>
    ///
    /// まで立てて、その内側を天井で削り落とす。結果の最大値は <b>d = cr でちょうど H</b> で、
    /// 火口の底は <c>H − depth</c> である。仮想の頂（<c>coneHeight</c>）は地形に 1 セルも書かれない。
    /// 成層火山で <c>coneHeight = 1.136 H</c>、鐘状で 1.029 H、盾状で 1.0004 H である
    /// （盾状の頂はもともと平らなので、ほとんど変わらない）。
    ///
    /// 山肌の傾斜はそのぶん急になる。**これは副作用ではなく同じ事実の別の面**で、
    /// 火口を持つ円錐の斜面は、切り落とされた頂へ向かって延びている。
    ///
    /// ── 育ち方（隆起と噛み合っていること）──────────────────────────
    ///
    /// <c>UpliftSchedule.GrowthMetresAt(profile, H, p) = max(0, profile − H(1−p))</c> なので:
    ///
    /// <code>
    /// 縁   growth = H·p                    （プロファイルが H だから）
    /// 底   growth = max(0, H·p − depth)    （プロファイルが H − depth だから）
    /// </code>
    ///
    /// つまり<b>縁が先に出て、底は depth だけ遅れて追う</b>。窪みの深さは
    /// <c>min(H·p, depth)</c> で、進捗 <c>depth/H</c>（既定の成層火山で 10 %）で満杯になり、
    /// **そこから先はずっと同じ深さのまま山と一緒に上がる**。
    /// 「最初から窪みとして在る」は、この 1 行の式が構造的に保証している。
    ///
    /// ★ <see cref="FloorMetresAt"/> が返すのがその底の高さで、**噴出口（炎・噴煙・噴石）は
    ///   これに乗せる**。山頂に乗せると、育っているあいだ中ずっと窪みの上に浮く。
    /// </summary>
    public static class VolcanoCrater
    {
        /// <summary>
        /// 火口底が平らな範囲（火口半径に対する比）。ここから縁までを滑らかに立ち上げる。
        /// **平らな底が要る** —— 噴出口の炎は半径を持つ円盤なので、底が椀だと縁で地面に潜る。
        /// </summary>
        public const float FloorFraction = 0.55f;

        /// <summary>
        /// 火口の中で山肌の起伏をどこまで効かせるか（0 = 滑らかな円錐そのもの）。
        ///
        /// ★ **これが無いと窪みが浅くなる。** 16 m 格子で実測すると、起伏（強さ 1）は
        ///   火口の縁を 20〜30 m 削り、既定の成層火山で <b>60 m の窪みが 32 m に減る</b>
        ///   （<c>tools/VolcanoPreview</c> の crater 表）。底のほうは天井が平らに
        ///   クランプするので削られず、**差だけが消える**。
        ///
        /// 物理的にも縁の内側は削れていない側が正しい —— 斜面を刻む放射谷は
        /// 火口の縁から下で始まるものである。
        /// </summary>
        public const float ReliefInsideCrater = 0.35f;

        /// <summary>起伏が満額に戻る距離（火口半径に対する比）。縁で段差を作らないため。</summary>
        public const float ReliefBlendRadiusFactor = 1.8f;

        /// <summary>
        /// 円錐を立て直す倍率の上限。**形態の帯（<c>MinRadiusOf</c>）では届かない値**だが、
        /// <c>.cgs</c> は手で編集されうるので、山肌が垂直に立つ前にここで止める。
        /// </summary>
        public const float MaxSummitScale = 2f;

        /// <summary>
        /// 火口の縁が山の高さ H に届くように円錐を立てる倍率（&gt;= 1）。クラス doc の式。
        /// **火口が成立しない入力（R &lt;= 0 / H &lt;= 0 / cr &gt;= R / NaN）では 1 を返す** ——
        /// そのとき <see cref="CeilingMetres"/> も H を返すので、形は今までの円錐そのものになる。
        /// </summary>
        public static float SummitScale(VolcanoForm form, float radiusMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 1f;

            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            if (!(crater > 0f) || crater >= radiusMetres) return 1f;

            // 単位高さの円錐が火口半径のところで残している割合。
            float fraction = VolcanoShape.ProfileAt(form, crater, radiusMetres, 1f);
            if (IsBad(fraction) || fraction <= 0f) return 1f;

            float scale = 1f / fraction;
            if (IsBad(scale) || scale < 1f) return 1f;
            return scale > MaxSummitScale ? MaxSummitScale : scale;
        }

        /// <summary>
        /// 起伏へ渡す「仮想の頂の高さ」（m）。**地形には 1 セルも書かれない**
        /// （内側は <see cref="CeilingMetres"/> が必ず削り落とす）。
        /// </summary>
        public static float ConeHeightMetres(VolcanoForm form, float radiusMetres,
                                             float heightMetres)
        {
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            return heightMetres * SummitScale(form, radiusMetres);
        }

        /// <summary>
        /// この距離で許される高さの上限（m）。中心から <see cref="FloorFraction"/> ×火口半径 までは
        /// <c>H − depth</c>、そこから火口半径で <c>H</c> へ滑らかに戻り、**外はずっと H**。
        ///
        /// **必ず <c>[0, heightMetres]</c> を返す。** これがクラス doc の制約 2 の全てである。
        /// </summary>
        public static float CeilingMetres(float distanceMetres, float radiusMetres,
                                          float heightMetres)
        {
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            if (IsBad(distanceMetres) || distanceMetres < 0f) return heightMetres;
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return heightMetres;

            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(crater > 0f) || !(depth > 0f)) return heightMetres;
            if (distanceMetres >= crater) return heightMetres;

            float ramp = SmoothStep(FloorFraction * crater, crater, distanceMetres);
            float ceiling = heightMetres - depth * (1f - ramp);
            if (ceiling < 0f) return 0f;
            return ceiling > heightMetres ? heightMetres : ceiling;
        }

        /// <summary>
        /// **⑤が地形へ書く最終形はこの 1 本である**（<c>Game/Volcano/VolcanoUplift</c> と
        /// <c>tools/VolcanoPreview</c> の両方がここを呼ぶ。式を 2 か所に書かないこと）。
        ///
        /// 起伏（<see cref="VolcanoRelief"/>）を**仮想の頂の高さ**で評価し、火口の天井で
        /// 切り落とす。返り値は必ず <c>[0, heightMetres]</c> で、
        /// <c>√(dx²+dz²) &gt;= radiusMetres</c> なら必ずきっかり 0 である。
        /// </summary>
        public static float ProfileAt(VolcanoRelief relief, float dx, float dz,
                                      float radiusMetres, float heightMetres)
        {
            if (relief == null) return 0f;
            if (IsBad(dx) || IsBad(dz)) return 0f;
            if (IsBad(radiusMetres) || IsBad(heightMetres)) return 0f;
            if (radiusMetres <= 0f || heightMetres <= 0f) return 0f;

            float cone = ConeHeightMetres(relief.Form, radiusMetres, heightMetres);
            float raised = relief.ProfileAt(dx, dz, radiusMetres, cone);
            if (!(raised > 0f)) return 0f;

            float d = (float)Math.Sqrt(dx * dx + dz * dz);

            // ★ 火口の中では起伏を薄める（<see cref="ReliefInsideCrater"/>）。
            //   起伏は必ず削る向きにしか働かない（あちらのクラス doc）ので、
            //   薄めた値は必ず raised と滑らかな円錐のあいだに入る —— つまり
            //   **上限は滑らかな円錐のまま**で、制約 2 は 1 mm も緩まない。
            float crater = VolcanoShape.CraterRadiusOf(radiusMetres);
            if (crater > 0f && d < crater * ReliefBlendRadiusFactor)
            {
                float smooth = VolcanoShape.ProfileAt(relief.Form, d, radiusMetres, cone);
                if (smooth > raised)
                {
                    float blend = SmoothStep(crater, crater * ReliefBlendRadiusFactor, d);
                    float keep = ReliefInsideCrater + (1f - ReliefInsideCrater) * blend;
                    raised = smooth + (raised - smooth) * keep;
                }
            }

            float ceiling = CeilingMetres(d, radiusMetres, heightMetres);
            return raised < ceiling ? raised : ceiling;
        }

        /// <summary>
        /// 今この瞬間の火口底の盛り上がり（m。元の地形高さからの相対量）。
        /// <paramref name="summitMetres"/> は縁の盛り上がり（<c>VolcanoUplift.SummitMetres</c>）。
        ///
        /// **噴出口の Y はこれで決める。** 縁に乗せると、育っているあいだ中ずっと
        /// 炎が窪みの上に浮いて見える（実機の指摘②）。
        /// </summary>
        public static float FloorMetresAt(float summitMetres, float heightMetres)
        {
            if (IsBad(summitMetres) || summitMetres <= 0f) return 0f;

            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(depth > 0f)) return summitMetres;

            float floor = summitMetres - depth;
            return floor > 0f ? floor : 0f;
        }

        /// <summary>
        /// 火口が満杯の深さに達したか（＝「窪みが完成した」と名乗ってよいか）。
        /// 縁が <c>depth</c> 上がった時点である。
        /// </summary>
        public static bool FullDepthReached(float summitMetres, float heightMetres)
        {
            float depth = VolcanoShape.CraterDepthOf(heightMetres);
            if (!(depth > 0f)) return false;
            return !IsBad(summitMetres) && summitMetres >= depth;
        }

        private static float SmoothStep(float from, float to, float t)
        {
            if (!(to > from)) return t >= to ? 1f : 0f;
            float u = (t - from) / (to - from);
            if (u < 0f) u = 0f;
            if (u > 1f) u = 1f;
            return u * u * (3f - 2f * u);
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
