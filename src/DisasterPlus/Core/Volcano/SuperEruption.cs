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
        /// カルデラの床が、元の地面より<b>山の高さの何倍ぶん下</b>に来るか。
        ///
        /// ── ★★ 1.35 は大きすぎた（2026-08-22、所有者の問い）──────────────
        ///
        /// &gt; カルデラ内部の標高が必ず海抜より低くなる理由は何ですか？
        ///
        /// <b>この係数がそのまま原因である。</b> 1.35・上限 900 m だったので:
        ///
        /// <code>
        ///   山 1000 m → 深さ 1350 m → 上限で 900 m
        ///   地面 120 m − 900 m = **−780 m**
        ///   ゲームの海面は 40 m（WaterSimulation.DEFAULT_SEA_LEVEL、IL 実測）
        ///   → 海面より 820 m 下。**どこに置いても必ず水没する。**
        /// </code>
        ///
        /// 実際のカルデラの床は<b>まわりの地面より数百 m 下</b>までである。
        /// 落ちるのは<b>山体</b>であって、まわりの大地ごと 900 m 沈むわけではない
        /// （イエローストーンの床はまわりの台地とほぼ同じ高さで、穴ですらない）。
        ///
        /// 0.35・上限 400 m なら、<b>高い土地では乾いたカルデラ、海に近い土地では
        /// 水没したカルデラ</b>になる —— サントリーニやクラカタウのように
        /// 水没するのは正しい姿だが、<b>「必ず」水没するのは間違い</b>だった。
        /// </summary>
        public const float CalderaDepthFactor = 0.35f;

        /// <summary>
        /// カルデラの底が全体に占める割合（ここまでは平ら）。残りが壁である。
        ///
        /// ★★ <b>0.55 では壁が緩すぎた</b>（2026-08-22、断面を描いて気づいた）。
        ///   半径 5563 m・深さ 900 m のとき、壁が水平に 2500 m もかかって
        ///   <b>約 20 度</b>——「カルデラ」ではなく「浅い盆地」に見える。
        ///   実際のカルデラは環状断層でほぼ垂直に落ち、崩れた岩屑が積もって
        ///   30〜45 度の急崖になる。0.78 なら水平 1220 m で 900 m 落ちて
        ///   <b>約 36 度</b>で、その帯に入る。
        /// </summary>
        public const float FloorFraction = 0.78f;

        /// <summary>壁の外側で地形に戻りきる割合。ここから外は 1 m も動かない。</summary>
        public const float RimFraction = 1.0f;

        /// <summary>カルデラの深さの下限（m）。浅いと「大きい火口」に見える。</summary>
        public const float MinDepthMetres = 120f;

        /// <summary>カルデラの深さの上限（m）。</summary>
        public const float MaxDepthMetres = 400f;

        /// <summary>
        /// カルデラの床のでこぼこの大きさ（深さに対する比）。
        ///
        /// ★★ **床は平らではない。**（2026-08-22、所有者の指摘
        ///   「カルデラ内部が平地になるのはおかしい」）落ちた屋根は 1 枚の板のまま
        ///   無傷で着地するのではなく、**割れて崩れた岩塊の山**（崩壊角礫岩）になる。
        ///   そのうえに火砕流が溜まる。
        /// </summary>
        public const float FloorRoughFraction = 0.22f;

        /// <summary>床のでこぼこの間隔（m）。岩塊 1 つぶんの大きさである。</summary>
        public const float RoughWavelengthMetres = 340f;

        /// <summary>細かいほうのでこぼこの間隔（m）。</summary>
        public const float FineRoughWavelengthMetres = 110f;

        /// <summary>
        /// 中央火口丘（resurgent dome）の半径（カルデラ半径に対する比）。
        ///
        /// ★ 実在の大カルデラ（イエローストーン・トバ・阿蘇）には、陥没のあとに
        ///   下からまた押し上げられた<b>中央の高まり</b>が必ずある。
        ///   これが無いと「まっさらな鉢」に見える。
        /// </summary>
        public const float ResurgentRadiusFraction = 0.34f;

        /// <summary>中央火口丘の高さ（深さに対する比）。**床より上、縁より下。**</summary>
        public const float ResurgentHeightFraction = 0.42f;

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

        /// <summary>
        /// カルデラの床の形（m、**0 か負**）。1 セルぶん。
        /// <see cref="BowlProfileAt"/> の滑らかな鉢に、<b>中央火口丘</b>と
        /// <b>崩れた岩塊のでこぼこ</b>を足したものである。
        ///
        /// ── なぜ鉢だけではだめなのか（2026-08-22、所有者の指摘）─────────────
        ///
        /// &gt; カルデラ内部が平地になるのはおかしい（元の地形や火山の山体の残骸も
        /// &gt; 加味してリアルに寄せてください）
        ///
        /// そのとおりで、<see cref="BowlProfileAt"/> は<b>まっ平らな床</b>を返す。
        /// 実際には落ちた屋根が割れて岩塊の山になり（崩壊角礫岩）、
        /// そのうえに火砕流が溜まり、やがて中央が再び押し上げられる。
        ///
        /// ★ <b>元の地形</b>のほうは呼び出し側が受け持つ ——
        ///   <c>VolcanoUplift.ProfileFor</c> が、山体の外では
        ///   <b>そのセルの本物の地面</b>を基準に落とす（中心 1 点の高さで
        ///   塗り潰さない）。ここが返すのは「その基準からどれだけ下か」である。
        /// </summary>
        /// <param name="dx">中心からの距離（m、X 方向）。でこぼこの位相に使う。</param>
        /// <param name="dz">同上（Z 方向）。</param>
        /// <param name="seed">この火山の種。同じ場所なら同じ床になる。</param>
        public static float CalderaFloorOffsetAt(float dx, float dz,
                                                 float calderaRadiusMetres, float depthMetres,
                                                 uint seed)
        {
            if (IsBad(dx) || IsBad(dz)) return 0f;

            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float bowl = BowlProfileAt(distance, calderaRadiusMetres, depthMetres);
            if (!(bowl < 0f)) return 0f;

            // ★ でこぼこも中央火口丘も、**縁へ向かって消す**。消さないと、
            //   カルデラの外の平地に岩塊がぽつぽつ残る。
            float rim = calderaRadiusMetres > 0f ? distance / calderaRadiusMetres : 1f;
            if (rim > 1f) rim = 1f;
            float inside = 1f - rim * rim;

            // ── 崩れた岩塊（2 つの間隔を重ねる）────────────────────────
            float rough =
                VolcanoRelief.ValueNoise(dx / RoughWavelengthMetres,
                                         dz / RoughWavelengthMetres, seed) * 0.7f
                + VolcanoRelief.ValueNoise(dx / FineRoughWavelengthMetres,
                                           dz / FineRoughWavelengthMetres, seed + 7717u) * 0.3f;
            // ★★ <c>ValueNoise</c> は<b>すでに [-1,1]</b> である（あちらの doc）。
            //    ここで (n*2-1) と書いていたとき、実際の範囲は [-3,1] になり、
            //    床が深さの 1.4 倍まで抜けた（-505 m / 深さ 350 m）。
            //    **[0,1] を [-1,1] へ直す型の書き癖をそのまま持ち込まないこと。**
            rough *= FloorRoughFraction * depthMetres * inside;

            // ── 中央火口丘 ────────────────────────────────────────
            float dome = 0f;
            float domeRadius = calderaRadiusMetres * ResurgentRadiusFraction;
            if (domeRadius > 0f && distance < domeRadius)
            {
                float k = distance / domeRadius;
                // 余弦の山。縁で高さも傾きも 0 になる（＝継ぎ目が出ない）。
                dome = depthMetres * ResurgentHeightFraction
                       * 0.5f * (1f + (float)Math.Cos(Math.PI * k));
            }

            float offset = bowl + rough + dome;

            // ★★ **床は元の地面より上には来ない。** 上がると、陥没したはずの
            //   カルデラの中に元の高さの島が残る。
            if (offset > 0f) return 0f;
            return offset;
        }

        /// <summary>
        /// **山体が落ち込む量**（m、**0 か負**）。1 セルぶん。
        ///
        /// ── なぜ「引き算」ではないのか（2026-08-22、所有者の指摘）───────────
        ///
        /// &gt; カルデラ形成時は、山体が大きく落ち込んで大爆発するんじゃないでしょうか…？
        ///
        /// はじめ⑤は<b>今の地面から深さぶんを引いて</b>いた。円錐が +1000 m、深さが
        /// 900 m だったので、<b>山頂に 100 m の切り株が残り</b>、そのまわりだけ
        /// 900 m 掘れた —— 「山が落ちた」ではなく「山のまわりに溝を掘った」絵である。
        ///
        /// 実際のカルデラは<b>屋根が 1 枚の板として落ちる</b>ので、床は
        /// <b>元の地面より下の 1 つの高さで平ら</b>になり、山体は跡形も無くなる。
        /// だから目標は絶対の高さ（<c>元の地面 + bowl</c>）で置き、
        /// 動かす量はそこまでの差分にする。
        ///
        /// <code>
        ///   山頂  base=+1000  目標 = 0 - 900 = -900   → -1900 落ちる
        ///   中腹  base= +400  目標 = 0 - 900 = -900   → -1300 落ちる
        ///   縁    base=    0  目標 = 0 -   0 =    0   →     0（動かない）
        /// </code>
        ///
        /// ★★ <b>陥没は地面を上げない。</b> <paramref name="groundMetres"/> は火山の
        ///   中心の地面の高さ 1 点なので、傾いた土地では外縁で目標が今の地面より
        ///   高くなりうる。そこを持ち上げると<b>落ちるはずの縁が盛り上がる</b>ので、
        ///   正の差分は 0 に切る。
        /// </summary>
        /// <param name="bowlMetres">
        /// <see cref="BowlProfileAt"/> の値（0 か負）。窪地の形そのもの。
        /// </param>
        /// <param name="baseMetres">このセルの**今の**地面の高さ（m。円錐を含む）。</param>
        /// <param name="groundMetres">火山を置く前の地面の高さ（m）。</param>
        public static float FounderDropAt(float bowlMetres, float baseMetres, float groundMetres)
        {
            if (IsBad(bowlMetres) || IsBad(baseMetres) || IsBad(groundMetres)) return 0f;

            float drop = (groundMetres + bowlMetres) - baseMetres;
            return drop < 0f ? drop : 0f;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
