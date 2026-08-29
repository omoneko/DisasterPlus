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
    /// ★★ ここも同じ形にした。時間積分がほぼ 0 なので<b>穴が残らない</b>:
    ///
    /// <code>
    ///   drive(t) = -envelope(t) * sin(2*pi * 1.5 * t/T)
    ///   envelope(t) = (1 - cos(2*pi * t/T)) / 2       // 両端 0、真ん中 1
    ///
    ///     t/T = 0   〜 1/3 : 負 ＝ 水が中心へ → ①**隆起**
    ///     t/T = 1/3 〜 2/3 : 正 ＝ 外へ押し出す → ②台地 ③ドーナツ（いちばん強い）
    ///     t/T = 2/3 〜 1   : 負 ＝ 引き戻す → **掘った穴を埋める**
    ///   そのあとは外力 0 ＝ ④ソルバだけが環を運ぶ
    /// </code>
    /// </summary>
    public static class TsunamiSource
    {
        // ── 時計（**水ステップ**。クラス doc の ★★ を読むこと）──────────────

        /// <summary>
        /// 外力が動いている長さ（水ステップ）。180 歩 ≒ 192 実秒 ≒ 3.2 実分。
        ///
        /// ★ 目盛りはバニラ: DLC の津波の発生源は <c>m_duration 16384 / 64</c>
        ///   ＝ **256 水ステップ**（≒ 4.5 実分）。こちらはその 7 割。
        /// </summary>
        public const float TotalSteps = 180f;

        /// <summary>
        /// 外力が回る周期の数。**バニラと同じ 1.5。**
        /// 「引き → 押し → 引き」の 3 つの半周期になる。
        /// </summary>
        public const float Cycles = 1.5f;

        /// <summary>①（引き込み）が終わる水ステップ。診断と試験のため。</summary>
        public static float DrawInSteps { get { return TotalSteps / (Cycles * 2f); } }

        /// <summary>②③（押し出し）が終わる水ステップ。</summary>
        public static float PushSteps { get { return TotalSteps * 2f / (Cycles * 2f); } }

        // ── 寸法 ──────────────────────────────────────────────

        /// <summary>
        /// 発生源の半径（セル。1 セル 16 m）。
        ///
        /// ★ IL では <c>R = 1 + max(maxX-origX, origX-minX)</c> ——<b>X しか見ない。</b>
        ///   120 セル ＝ 1920 m。震源の「隆起」の大きさそのものである。
        /// </summary>
        public const int RadiusCells = 120;

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
        /// いちばん弱い地震の外力（<c>m_delta</c> の単位）。
        /// **<c>tools/WaterSolverSim</c> で測って決めた値**であって、推測ではない。
        /// </summary>
        public const int MinDriveUnits = 260;

        /// <summary>
        /// いちばん強い地震（強度 255、＝強度解放の 25.5）の外力。
        /// 同じく <c>tools/WaterSolverSim</c> の実測から決めた。
        /// </summary>
        public const int MaxIntensityDriveUnits = 2600;

        /// <summary>地震の強度（0〜255）から外力の大きさを出す（符号なし）。</summary>
        public static int DriveUnitsFor(byte intensity)
        {
            float d = MinDriveUnits
                      + (MaxIntensityDriveUnits - MinDriveUnits) * (intensity / 255f);

            int units = (int)(d + 0.5f);
            if (units < MinDriveUnits) return MinDriveUnits;
            if (units > MaxDriveUnits) return MaxDriveUnits;
            return units;
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

            // 両端 0、真ん中 1 の包絡。**段差を作らない**（ソルバは傾きの差を積む）。
            double envelope = (1.0 - Math.Cos(2.0 * Math.PI * w)) * 0.5;

            // 1.5 周期。最初の半周期を「引き」にしたいので符号を反転する。
            double swing = -Math.Sin(2.0 * Math.PI * Cycles * w);

            return (float)(envelope * swing);
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
            if (w < 1f / 3f) return "1 the sea is drawn in over the epicentre";
            if (w < 2f / 3f) return "2 the bulge is pushed out into a ring";
            return "3 the sea is drawn back in so no crater is left";
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
