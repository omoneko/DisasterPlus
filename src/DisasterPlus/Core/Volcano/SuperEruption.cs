using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **破局噴火（カルデラ噴火）。** <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 火山の 25.5 スケールが小さすぎるように思います。25.5 のときだけ、
    /// &gt; 破局噴火の再現をできるか調べて実装してください。
    /// &gt; 地下のマグマ上昇による火山の形成 → 数万年かけた巨大なマグマだまりの成長
    /// &gt; → 内圧限界による破局噴火（大爆発） → 地面の自重による大陥没とカルデラ形成。
    ///
    /// ── できるか（調べた結果）────────────────────────────────
    ///
    /// **できる。** ⑤は既に地形を書いており（<c>VolcanoUplift</c>）、
    /// 最終段の書き込み（<c>UpliftSchedule.RawTargetAt</c>）は
    /// <b>プロファイルの符号を問わない</b> —— 火口を彫るのに既に負の
    /// プロファイルを使っている（あちらの doc:「<c>profileMetres</c> は**負を許す**」）。
    ///
    /// ★★ <b>ただし「同じ仕組みでそのまま」ではなかった。</b>（実装時に判明・2026-08-22）
    ///
    ///   円錐は <c>UpliftSchedule.GrowthMetresAt</c> を通って
    ///   「山頂から外へ広がる」育ち方をする。あれの先頭に
    ///
    /// <code>
    ///   if (profileMetres &lt;= 0f) return 0f;
    /// </code>
    ///
    ///   が在るので、<b>負のプロファイルはそこで 0 に潰され、カルデラは
    ///   1 mm も掘れない。</b> だから膨らみと陥没は<b>成長の規則を通さず</b>
    ///   <c>profile × progress</c>（＝全体が一様に動く）で書く
    ///   ——<c>VolcanoUplift.Terrain.cs</c> の <c>UpliftStage</c> 分岐。
    ///   膨らみも陥没も「縁から中心へ広がる」ものではないので、
    ///   一様に動くほうが元々正しい。
    ///
    /// 新しい地形の書き込み経路は 1 本も要らない（矩形の取り方・退避・
    /// フラッシュ・天井の数え方は 3 段とも <c>VolcanoUplift</c> のまま）。
    ///
    /// ── 4 つの段（依頼の順序そのもの）────────────────────────────
    ///
    /// | 依頼の言葉 | ⑤の位相 | 地形に何が起きるか |
    /// |---|---|---|
    /// | 地下のマグマ上昇による火山の形成 | <c>Uplifting</c> | 円錐が立つ（今までどおり） |
    /// | 数万年かけた巨大なマグマだまりの成長 | <c>Inflating</c> | **裾ごと広く持ち上がる** |
    /// | 内圧限界による破局噴火（大爆発） | <c>Erupting</c> | 地形は動かない（噴煙と爆発） |
    /// | 地面の自重による大陥没とカルデラ形成 | <c>Collapsing</c> | **山ごと陥没して平底の窪地** |
    ///
    /// ★★ <b>膨らみは山ではなく「裾ごと」である。</b> マグマだまりは山より深く広いので、
    ///   地表の膨らみは<b>山の何倍もの半径にわたる、ごく低いドーム</b>になる
    ///   （実在のカルデラ火山で観測される地盤の隆起がそれである）。
    ///   山だけを高くすると、ただの「もっと高い山」にしかならない。
    ///
    /// ★★ <b>カルデラは円錐の穴ではない。</b> 屋根が抜けて落ちるので、
    ///   <b>平らな底と切り立った壁</b>になる（<see cref="BowlProfileAt"/>）。
    ///   すり鉢を彫ると「大きい火口」にしか見えない。
    /// </summary>
    public static class SuperEruption
    {
        /// <summary>
        /// 破局噴火になる強度スライダーの生値。**上限ちょうど**（表示 25.5）である。
        ///
        /// 所有者の指示は「25.5 のときだけ」なので、24.9 では起きない。
        /// <c>IntensityUnlock</c> が上限を 255 まで開けているので、
        /// スライダーをいちばん右へ振り切ったときだけこの規模になる。
        /// </summary>
        public const int RawThreshold = 255;

        /// <summary>膨らみの半径が山の半径の何倍か。**裾よりずっと外まで持ち上がる。**</summary>
        public const float InflationRadiusFactor = 2.4f;

        /// <summary>膨らみの高さが山の高さの何割か。**低くて広い**のが要点。</summary>
        public const float InflationHeightFraction = 0.22f;

        /// <summary>カルデラの半径が山の半径の何倍か。</summary>
        public const float CalderaRadiusFactor = 1.9f;

        /// <summary>
        /// カルデラの深さが山の高さの何倍か。1 より大きい ——
        /// **山が消えるだけでなく、元の地面より下まで落ちる。**
        /// </summary>
        public const float CalderaDepthFactor = 1.35f;

        /// <summary>カルデラの底が全体に占める割合（ここまでは平ら）。</summary>
        public const float FloorFraction = 0.55f;

        /// <summary>壁の外側で地形に戻りきる割合。ここから外は 1 m も動かない。</summary>
        public const float RimFraction = 1.0f;

        /// <summary>カルデラの深さの下限（m）。浅いと「大きい火口」に見える。</summary>
        public const float MinDepthMetres = 120f;

        /// <summary>カルデラの深さの上限（m）。</summary>
        public const float MaxDepthMetres = 900f;

        /// <summary>カルデラの半径の上限（m）。マップは一辺 17,280 m しかない。</summary>
        public const float MaxRadiusMetres = 6000f;

        /// <summary>
        /// この強度は破局噴火か。<paramref name="raw"/> はスライダーの生値。
        /// </summary>
        public static bool IsSuper(int raw)
        {
            return raw >= RawThreshold;
        }

        /// <summary>
        /// マグマだまりの膨らみが届く半径（m）。山の半径の
        /// <see cref="InflationRadiusFactor"/> 倍で、<see cref="MaxRadiusMetres"/> で切る。
        /// </summary>
        public static float InflationRadiusMetres(float volcanoRadiusMetres)
        {
            if (IsBad(volcanoRadiusMetres) || volcanoRadiusMetres <= 0f) return 0f;

            float r = volcanoRadiusMetres * InflationRadiusFactor;
            return r > MaxRadiusMetres ? MaxRadiusMetres : r;
        }

        /// <summary>膨らみの最大の高さ（m）。**山の高さより低い。**</summary>
        public static float InflationHeightMetres(float volcanoHeightMetres)
        {
            if (IsBad(volcanoHeightMetres) || volcanoHeightMetres <= 0f) return 0f;
            return volcanoHeightMetres * InflationHeightFraction;
        }

        /// <summary>
        /// 膨らみの形（m、**正**）。中心がいちばん高く、
        /// <paramref name="reachMetres"/> でちょうど 0 になる<b>とても平たいドーム</b>。
        ///
        /// <c>cos</c> の膨らみを使うのは、縁で高さも傾きも 0 になるからである ——
        /// 放物線だと縁に折れ目が出て、そこが崖に見える。
        /// </summary>
        public static float InflationAt(float distanceMetres, float reachMetres,
                                        float heightMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(reachMetres) || reachMetres <= 0f) return 0f;
            if (IsBad(heightMetres) || heightMetres <= 0f) return 0f;
            if (distanceMetres >= reachMetres) return 0f;

            float t = distanceMetres / reachMetres;
            // (1 + cos(pi t)) / 2 —— t=0 で 1、t=1 で 0、両端で傾き 0。
            float bell = 0.5f * (1f + (float)Math.Cos(Math.PI * t));
            return heightMetres * bell;
        }

        /// <summary>カルデラの半径（m）。</summary>
        public static float CalderaRadiusMetres(float volcanoRadiusMetres)
        {
            if (IsBad(volcanoRadiusMetres) || volcanoRadiusMetres <= 0f) return 0f;

            float r = volcanoRadiusMetres * CalderaRadiusFactor;
            return r > MaxRadiusMetres ? MaxRadiusMetres : r;
        }

        /// <summary>
        /// カルデラの深さ（m、**正の値**）。<paramref name="volcanoHeightMetres"/> は
        /// 山の高さで、それより深く落ちる（<see cref="CalderaDepthFactor"/>）。
        /// </summary>
        public static float CalderaDepthMetres(float volcanoHeightMetres)
        {
            // ★★ **壊れた高さから深さを合成しない。** 0 を返せば
            //   <see cref="BowlProfileAt"/> が 1 mm も掘らない ——
            //   ここで <c>MinDepthMetres</c> を返すと、読めなかった値から
            //   120 m の窪地が生まれる（他の形の関数はどれも 0 を返す）。
            //   Codex のセカンダリレビューが拾った。
            if (IsBad(volcanoHeightMetres) || volcanoHeightMetres <= 0f) return 0f;

            float d = volcanoHeightMetres * CalderaDepthFactor;
            if (d < MinDepthMetres) return MinDepthMetres;
            if (d > MaxDepthMetres) return MaxDepthMetres;
            return d;
        }

        /// <summary>
        /// カルデラの形（m、**負**）。<b>平らな底と切り立った壁</b>で、
        /// <paramref name="calderaRadiusMetres"/> の外はきっかり 0。
        ///
        /// ★★ すり鉢（円錐の穴）にしないこと —— それは「大きい火口」にしか見えない。
        ///   屋根が抜けて<b>塊のまま落ちる</b>のがカルデラなので、
        ///   底は平ら（<see cref="FloorFraction"/> まで）で、そこから縁までを
        ///   なめらかな段で繋ぐ。
        /// </summary>
        public static float BowlProfileAt(float distanceMetres, float calderaRadiusMetres,
                                          float depthMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(calderaRadiusMetres) || calderaRadiusMetres <= 0f) return 0f;
            if (IsBad(depthMetres) || depthMetres <= 0f) return 0f;
            if (distanceMetres >= calderaRadiusMetres * RimFraction) return 0f;

            float t = distanceMetres / calderaRadiusMetres;

            // 底は平ら。**ここが「大きい火口」との違いである。**
            if (t <= FloorFraction) return -depthMetres;

            // 壁。smoothstep で底から縁へ。縁で深さも傾きも 0 になる。
            float w = (t - FloorFraction) / (RimFraction - FloorFraction);
            if (w > 1f) w = 1f;
            float k = 1f - w * w * (3f - 2f * w);
            return -depthMetres * k;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
