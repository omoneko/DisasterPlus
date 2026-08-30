using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>海溝型地震の津波の「発生源」。</b>**エンジン非依存の純関数だけ。**
    ///
    /// ── 研究（2026-08-29）──────────────────────────────────────
    ///
    /// 全文は <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>。結論:
    ///
    /// &gt; **バニラの津波は「波」ではない。**マップ外周の一区画で海面を
    /// &gt; 1.5 周期ぶん上下させるだけの<b>境界条件</b>であり、街を襲う水の壁は
    /// &gt; すべてゲーム自身の浅水ソルバ（<c>WaterSimulation.SimulateWater</c>）が
    /// &gt; その揺さぶりを内陸へ伝播させた結果である。
    ///
    /// 同じソルバには<b>マップのどこにでも置ける外力</b>がある（<c>TYPE_IMPACT</c>、
    /// IL_0845–0A49）。1 セルごとに
    ///
    /// <code>
    ///   f(d) = delta * (1 - d^2 / R^2)          d はセル単位の距離、d&gt;=R で 0
    ///   accX += f(dx,dz) - f(dx+1,dz)           ← **水面の傾き**に足される
    /// </code>
    ///
    /// つまり<b>そこに水の山があるかのようにソルバを騙す仮想の山</b>で、
    /// <b>海底が隆起したのと同じ</b> —— 津波の教科書どおりの発生源である。
    /// <c>delta &gt; 0</c> なら水は外へ、<c>delta &lt; 0</c> なら中へ流れる。
    ///
    /// ── ★★ 時計の単位は「水ステップ」。sim フレームではない ────────────────
    ///
    /// **2026-08-30、これが「津波が発生しない」の真の原因だった。**
    ///
    /// <code>
    ///   SimulateWater は最後に SetCurrentWaterFrame(start, progress, .., max, ..) を呼び、
    ///     m_waterFrameIndex = (start &amp; ~63) + (progress &lt;&lt; 6)/max = start + 64
    ///   WaterThread は m_waterFrameIndex &lt; m_simulationFrameIndex のあいだだけ回り、
    ///   SimulationStep は m_simulationFrameIndex &gt; m_waterFrameIndex + 1 で待つ。
    ///   ＝ **SimulateWater は 64 sim フレームに 1 回しか走らない。**
    /// </code>
    ///
    /// だから「1080 フレーム押す」は<b>水ステップにして 17 回</b>でしかなかった。
    /// 実機で 0.7 m しか上がらなかったのはこれである。
    ///
    /// ★★ 独立した裏取りが 2 つある:
    ///   ・オフライン再現（<c>tools/WaterSolverSim</c>）で測った波の速さ
    ///     8.22 m / 水ステップ ÷ 64 ＝ **0.128 m / sim フレーム**
    ///   ・<c>TsunamiAI.IsStillActive</c> がバニラで使う寿命定数が
    ///     **0.125 m / sim フレーム**（IL_0024）
    ///   小数第 2 位まで一致する。
    ///
    /// ★ 1 水ステップ ＝ 64 sim フレーム ＝ ゲーム速度 1 でおよそ **1.07 実秒**。
    ///
    /// ── ★★ 外力は「引き → 押し → 引き」でなければならない ────────────────
    ///
    /// **2026-08-30、オフライン再現で分かった 2 つ目の間違い。**
    /// 前の版は「①引き（40 歩）→ ②③押しっぱなし（140 歩）」だった。結果:
    ///
    /// <code>
    ///   drive 800 で 300 歩まわすと ——
    ///     震源の海面 -13.9 m（海底が露出）、環はたった 1.8 m
    ///   drive 4000 なら震源は -40.00 m ＝ **海底まで掘り切った。**
    /// </code>
    ///
    /// 押しっぱなしの外力は<b>定常的な流出</b>を作る。定常的な流出は波ではなく
    /// **穴**である。波を作るのは<b>移り変わり</b>のほうで、
    /// だからバニラの津波も 1.5 周期の<b>振動</b>なのである（引き波→押し波→引き波）。
    ///
    /// ★★ **どの形がいちばん良いかは、6 つ振ってオフライン再現で決めた**
    ///   （2026-08-30。<c>tools/WaterSolverSim</c> で 5 つのゲート
    ///    G1 隆起 / G2 穴を残さない / G3 環 / G4 到達 / G5 安全 を判定）。
    ///   勝ったのは<b>「長く引いて、短く強く押す」</b>:
    ///
    /// <code>
    ///   w = t / TotalSteps
    ///   w &lt; 0.6 : drive = -sin(pi * w / 0.6)              ①**隆起が育つ**
    ///   w &gt;= 0.6: drive = +sin(pi * (w-0.6)/0.4) * 1.5    ②台地→ドーナツ→環
    ///   そのあとは外力 0 ＝ ③ソルバだけが環を運ぶ
    /// </code>
    ///
    /// ★ 時間積分はちょうど 0（0.6×(2/pi) ＝ 0.4×1.5×(2/pi)）。**穴が残らない。**
    ///
    /// ★ 落ちた形の記録:
    ///   ・1.5 周期（引き→押し→引き）は隆起が 3.2 m までしか育たなかった
    ///   ・半径 200 セルは<b>源が桶になって共振し</b>、中心が海底まで抜けたあと
    ///     **+111 m まで跳ね上がった**（420 歩で止めていたので最初は「緑」に見えた
    ///     —— 1080 歩まで回して初めて分かった）
    /// </summary>
    public static class TsunamiSource
    {
        // ── 時計（**水ステップ**。クラス doc の ★★ を読むこと）──────────────

        /// <summary>
        /// 外力が動いている長さ（水ステップ）。300 歩 ≒ 320 実秒 ≒ 5.3 実分。
        ///
        /// ★ 目盛りはバニラ: DLC の津波の発生源は <c>m_duration 16384 / 64</c>
        ///   ＝ **256 水ステップ**（≒ 4.5 実分）。同じ桁である。
        /// </summary>
        public const float TotalSteps = 300f;

        /// <summary>
        /// 引き込みに使う割合。**残りが押し出し。**
        ///
        /// ★★ 0.6 は<b>オフライン再現の掃引で勝った値</b>（6 つの形を並列に振って
        ///   5 つのゲートで判定した。<c>docs/…/2026-08-29-tsunami-il-facts.md</c>）。
        ///   長く引いてから短く強く押す —— これがいちばん
        ///   「隆起がしっかり見えてから、環になって走り出す」形だった。
        /// </summary>
        public const float DrawInFraction = 0.6f;

        /// <summary>
        /// 押し出しの山の高さ（引き込みの山を 1 としたとき）。
        ///
        /// ★ <c>0.6 : 0.4</c> の時間配分に対して <c>1 : 1.5</c> の高さなので、
        ///   <b>時間積分がちょうど 0</b> になる（0.6×(2/π) ＝ 0.4×1.5×(2/π)）。
        ///   **これが「穴を残さない」の代数的な担保**である。
        /// </summary>
        public const float PushOvershoot = 1.5f;

        /// <summary>①（引き込み）が終わる水ステップ。診断と試験のため。</summary>
        public static float DrawInSteps { get { return TotalSteps * DrawInFraction; } }

        /// <summary>②（押し出し）がいちばん強くなる水ステップ。</summary>
        public static float PushSteps
        {
            get { return TotalSteps * (DrawInFraction + (1f - DrawInFraction) * 0.5f); } }

        // ── 寸法 ──────────────────────────────────────────────

        /// <summary>
        /// 発生源の半径（セル。1 セル 16 m）。
        ///
        /// ★ IL では <c>R = 1 + max(maxX-origX, origX-minX)</c> ——<b>X しか見ない。</b>
        ///   80 セル ＝ 1280 m。震源の「隆起」の大きさそのものである。
        ///
        /// ★★ 120 -> 80（2026-08-30、オフライン再現の掃引）。大きくすると
        ///   <b>源そのものが桶になって共振する</b> —— 半径 200 セルでは、
        ///   縁で立った波が中心へ戻ってきて<b>中心が海底まで抜けたあと
        ///   +111 m まで跳ね上がった</b>（掃引の shape 1）。
        /// </summary>
        public const int RadiusCells = 80;

        /// <summary>同上をメートルで。</summary>
        public const float RadiusMetres = RadiusCells * 16f;

        // ── 外力の大きさ ───────────────────────────────────────

        /// <summary><c>WaterWave.m_delta</c> は Int16。**ここを超えると折り返す。**</summary>
        public const int MaxDeltaUnits = 32767;

        /// <summary>
        /// 同じ場所に重ねる水波の数。
        ///
        /// ★ 外力は波ごとに加算されるので、Int16 を超える外力はこれで作る。
        ///   費用は「波の数 × bbox に入るセル数」。**上げる前に実機で測ること。**
        /// </summary>
        public const int MaxStackedWaves = 8;

        /// <summary>重ねた全部で出せる外力の上限。</summary>
        public const int MaxDriveUnits = MaxStackedWaves * MaxDeltaUnits;

        /// <summary><c>m_delta</c> の 1 m ぶん（IL: <c>depth * 65536 / 1024</c>）。</summary>
        public const int UnitsPerMetre = 64;

        /// <summary>
        /// いちばん弱い地震の外力（<c>m_delta</c> の単位、水深
        /// <see cref="ReferenceDepthMetres"/> のとき）。
        /// **<c>tools/WaterSolverSim</c> で測って決めた値**であって、推測ではない。
        /// </summary>
        public const int MinDriveUnits = 780;

        /// <summary>
        /// いちばん強い地震（強度 255 ＝ 強度解放の 25.5）の外力。同じく実測。
        ///
        /// ★★ **強度の帯が狭いのは手抜きではない。**
        ///   波の大きさを決めているのは<b>海の深さ</b>である —— ソルバの流量は
        ///   <c>v = min(v, m_height)</c> で頭打ちされるので、
        ///   <b>浅い海はどんなに強い地震でも大きな波を運べない</b>。
        ///   水深 40 m での実測（<c>tools/WaterSolverSim</c>、格子 1081、780 歩）:
        ///
        /// <code>
        ///     drive   隆起     いちばん深い中心   底に残る水   環（2km）
        ///       896   18.1 m   -29.2 m (73%)     10.8 m      5.7 m   ← 安全な作動点
        ///      1200   24.4 m   -38.8 m (97%)      1.25 m     8.2 m   ← **掘り抜き**
        ///      1500   30.3 m   -40.00 m (100%)    0 m        —       ← 海底むき出し
        /// </code>
        ///
        /// ★★ **1200 は「強い」のではなく壊れている。**（2026-08-30、最終検証）
        ///   1200 では震源の水柱が 40 m のうち 1.25 m しか残らず、
        ///   画面には<b>2 km 幅の穴が 130 実秒</b>映る。強度スライダーは
        ///   最初のクリックで 255 まで振れるので、これは隅ではなく<b>既定の最悪</b>だった。
        ///
        /// ★ だから上限は 900 ——「掘る割合 ≦ 75%」で決めた値である。
        ///   帯が狭いのは手抜きではない: <b>波の大きさを決めるのは海の深さ</b>で、
        ///   ソルバの流量は <c>v = min(v, m_height)</c> で頭打ちされる。
        /// </summary>
        public const int MaxIntensityDriveUnits = 900;

        /// <summary>
        /// 上の数字を測ったときの水深（m）。
        ///
        /// ★★ **外力は水深で割らなければならない。**（2026-08-30、判定エージェント）
        ///   ソルバの流量は <c>v = min(v, m_height)</c> で水深に頭打ちされるので、
        ///   同じ外力でも<b>浅い海ほど掘り抜けてしまう</b>。水深 10 m で
        ///   水深 40 m 用の外力を出すと、震源のセルが
        ///   <b>136 水ステップ（≒145 実秒）のあいだ海底むき出しになった</b>。
        ///   隆起の高さと掘り下げの深さはこのソルバでは同じ量なので、
        ///   浅い海では隆起そのものを小さくするしかない。
        /// </summary>
        public const float ReferenceDepthMetres = 40f;

        /// <summary>
        /// 水深の係数の下限。**0 にすると浅瀬で津波が消える。**
        ///
        /// ★ 0.15 -> 0.08（2026-08-30）。0.15 だと水深 5 m の海に
        ///   水深 6 m ぶんの外力が出て、震源が<b>海底むき出しになった</b>
        ///   （実測 -5.00 m ＝ 水柱まるごと）。素の比（depth/40）が使える
        ///   範囲を水深 3.2 m まで下げる。
        /// </summary>
        public const float MinDepthFactor = 0.08f;

        /// <summary>
        /// 同じく上限。
        ///
        /// ★★ **1.0 -> 12.5（2026-08-30、実機報告「津波が発生しない」）。**
        ///
        ///   1.0 にした根拠は「CS の地形は標高 0 以上なので、海面 40 m のマップに
        ///   40 m より深い海は作れない」だった。**この前提が間違っていた** ——
        ///   海面はマップごとに違い、実機のマップは<b>海面 207 m・水深 174 m</b>
        ///   だった。そこで出していた外力は 806、つまり
        ///   <b>水柱の 15% しか掘っていない</b>（余力を 85% 使い残していた）。
        ///   環は 2 〜 3 m にしかならず、水深 174 m の外洋では<b>見えない</b>。
        ///
        ///   <c>WaterSimulation.MAX_SEA_LEVEL</c> は 500（IL 実測）なので、
        ///   ありうる最深は 500 m。上限はそれを割った 500/40 = 12.5 とする。
        ///
        /// ★★ **応答は水深に完全に無依存である**（<c>tools/WaterSolverSim</c>、
        ///   格子 1081・1200 歩・drive 806 を水深 40 m と 174 m で回して
        ///   <b>全列・全フレームが一致</b>した）。水深が効くのは
        ///   <c>v = min(v, m_height)</c> の頭打ちだけで、そこに触れないうちは
        ///   同じ外力が同じ波を作る。だから<b>掘る割合は外力に比例し水深に反比例する</b>:
        ///
        /// <code>
        ///     掘る割合 ≒ 0.0329 * drive / depth        （水深 40・174 の実測から）
        ///     -> 70% を超えないための線は  drive ≒ 21 * depth
        /// </code>
        ///
        ///   <see cref="DriveUnitsFor"/> は <c>base * depth/40</c>（base は 780〜900）
        ///   ＝ <c>(19.5〜22.5) * depth</c> なので、**どの水深でも同じ割合**に収まる。
        ///   水深 174 m での実測（drive 3500 ＝ 強度 55 相当）:
        ///   隆起 72 m、最深 -110 m（63%）、環は 2.4 km で 23.7 m、8.2 km で 12.8 m。
        /// </summary>
        public const float MaxDepthFactor = 12.5f;

        /// <summary>
        /// 地震の強度（0〜255）と<b>震源の水深</b>から外力の大きさを出す（符号なし）。
        /// </summary>
        public static int DriveUnitsFor(byte intensity, float depthMetres)
        {
            float d = MinDriveUnits
                      + (MaxIntensityDriveUnits - MinDriveUnits) * (intensity / 255f);

            d *= DepthFactor(depthMetres);

            int units = (int)(d + 0.5f);
            if (units < 1) return 1;
            if (units > MaxDriveUnits) return MaxDriveUnits;
            return units;
        }

        /// <summary>水深による割り引き。**上のクラス doc の理由で必須。**</summary>
        public static float DepthFactor(float depthMetres)
        {
            if (IsBad(depthMetres) || depthMetres <= 0f) return MinDepthFactor;

            float f = depthMetres / ReferenceDepthMetres;
            if (f < MinDepthFactor) return MinDepthFactor;
            if (f > MaxDepthFactor) return MaxDepthFactor;
            return f;
        }

        // ── バニラの目盛り（比較のためだけに持つ）─────────────────────

        /// <summary>
        /// <c>TsunamiAI.m_height</c> のプレハブ実測値（m）。
        /// sharedassets55 の GameObject <c>Tsunami</c> から読んだ。
        /// </summary>
        public const float VanillaHeightMetres = 64f;

        /// <summary>
        /// バニラの津波が外周の海面を持ち上げる高さ（<c>m_delta</c> の単位）。
        /// <code>  round(m_height * 65536/1024 * intensity / 55)  </code>
        ///
        /// ★ これは<b>境界条件の振幅</b>であって、こちらの<b>仮想の山</b>とは
        ///   意味が違う。同じ単位なので並べて書けるだけである ——
        ///   **「バニラより小さいから弱い」とは読めない。**
        /// </summary>
        public static int VanillaDeltaUnits(byte intensity)
        {
            float d = VanillaHeightMetres * UnitsPerMetre * intensity / 55f;
            int units = (int)(d + 0.5f);
            if (units > MaxDeltaUnits) return MaxDeltaUnits;
            if (units < 0) return 0;
            return units;
        }

        // ── 外力の形 ──────────────────────────────────────────

        /// <summary>
        /// 地震から <paramref name="elapsedSteps"/> 水ステップ後に、
        /// <b>重ねた波ぜんぶで</b>出す外力（<c>m_delta</c> の単位）。
        ///
        /// <b>負が「水を中心へ集める」、正が「外へ押し出す」。</b>
        /// 終わったら 0 —— <b>呼び出し側はそれで波を解放する。</b>
        /// </summary>
        public static int DeltaAt(float elapsedSteps, int driveUnits)
        {
            if (IsBad(elapsedSteps) || elapsedSteps < 0f) return 0;
            if (elapsedSteps >= TotalSteps) return 0;
            if (driveUnits <= 0) return 0;

            float f = DriveAt(elapsedSteps);
            int units = (int)(f * driveUnits + (f >= 0f ? 0.5f : -0.5f));

            if (units > MaxDriveUnits) return MaxDriveUnits;
            if (units < -MaxDriveUnits) return -MaxDriveUnits;
            return units;
        }

        /// <summary>
        /// 外力の形だけ（頂点を 1 に正規化）。<c>[-1, 1]</c>。
        ///
        /// ★★ **時間積分がほぼ 0 でなければならない。**（クラス doc）
        ///   押しっぱなしにすると海底に穴が残るだけで、波にならない。
        /// </summary>
        public static float DriveAt(float elapsedSteps)
        {
            if (IsBad(elapsedSteps) || elapsedSteps <= 0f) return 0f;
            if (elapsedSteps >= TotalSteps) return 0f;

            double w = elapsedSteps / TotalSteps;

            // ① 長く引く（負 ＝ 水が中心へ集まる ＝ 隆起が育つ）。
            if (w < DrawInFraction)
            {
                return (float)(-Math.Sin(Math.PI * w / DrawInFraction));
            }

            // ②③ 短く強く押す（正 ＝ 外へ。隆起が台地→ドーナツ→環になる）。
            double k = (w - DrawInFraction) / (1.0 - DrawInFraction);
            return (float)(Math.Sin(Math.PI * k) * PushOvershoot);
        }

        /// <summary>
        /// 外力を切ったあと（＝④）か。ここから先は<b>ソルバだけが波を運ぶ</b>ので、
        /// 呼び出し側は水波を解放してよい。
        /// </summary>
        public static bool IsFinished(float elapsedSteps)
        {
            return !IsBad(elapsedSteps) && elapsedSteps >= TotalSteps;
        }

        /// <summary>今どの段か（**英語・診断用**）。</summary>
        public static string StageAt(float elapsedSteps)
        {
            if (IsBad(elapsedSteps) || elapsedSteps < 0f) return "not started";
            if (elapsedSteps >= TotalSteps) return "4 done - the ring is on its own now";

            float w = elapsedSteps / TotalSteps;
            if (w < DrawInFraction) return "1 the sea is drawn in and the bulge rises";
            return "2 the bulge is pushed out into a spreading ring";
        }

        // ── 重ねた波への配分 ─────────────────────────────────────

        /// <summary>
        /// 重ねた波 <paramref name="index"/> 個目（0 基点）に入れる <c>m_delta</c>。
        /// <paramref name="driveUnits"/> を <see cref="MaxDeltaUnits"/> ずつ分けて配る。
        /// </summary>
        public static int DeltaForWave(int index, int driveUnits)
        {
            if (index < 0 || index >= MaxStackedWaves) return 0;

            int magnitude = driveUnits < 0 ? -driveUnits : driveUnits;
            if (magnitude > MaxDriveUnits) magnitude = MaxDriveUnits;

            int given = index * MaxDeltaUnits;
            int left = magnitude - given;
            if (left <= 0) return 0;
            if (left > MaxDeltaUnits) left = MaxDeltaUnits;

            return driveUnits < 0 ? -left : left;
        }

        /// <summary>いま何本の波が要るか（診断用）。</summary>
        public static int WavesNeeded(int driveUnits)
        {
            int magnitude = driveUnits < 0 ? -driveUnits : driveUnits;
            if (magnitude <= 0) return 0;
            if (magnitude > MaxDriveUnits) magnitude = MaxDriveUnits;

            return (magnitude + MaxDeltaUnits - 1) / MaxDeltaUnits;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
