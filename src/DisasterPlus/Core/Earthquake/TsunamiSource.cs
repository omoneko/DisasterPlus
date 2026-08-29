using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>海溝型地震の津波の「発生源」。</b>**エンジン非依存の純関数だけ。**
    ///
    /// ── 何をどう作り直したか（2026-08-29）───────────────────────────
    ///
    /// 所有者:「natural DisasterDLC の津波のメカニズムを研究して、
    ///        私が求めているものを一から作り直してください。」
    ///
    /// 研究の全文は <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>。
    /// 結論だけ書くと:
    ///
    /// > **バニラの津波は「波」ではない。**マップ外周の一区画で海面を
    /// > 1.5 周期ぶん上下させるだけの<b>境界条件</b>であり、街を襲う水の壁は
    /// > すべてゲーム自身の浅水ソルバ（<c>WaterSimulation.SimulateWater</c>）が
    /// > その揺さぶりを内陸へ伝播させた結果である。発生源はたった
    /// > <b>256 フレーム ≒ 4.3 実秒</b>しかない。
    ///
    /// ★★ **だから旧実装は原理から間違っていた。**
    ///   <c>TsunamiSurge</c> は水源 240 個で海面を「塗り」、<c>SplashWater</c> を
    ///   連射して壁を「描いて」いた。**ソルバの中を一度も通っていない。**
    ///   だから壁にならず、減衰し、水源の届く範囲でぱたりと止まった。
    ///
    /// ── 使う口: <c>TYPE_IMPACT</c> の水波 ──────────────────────────
    ///
    /// ソルバには<b>マップのどこにでも置ける外力</b>がある（IL_0845–0A49）。
    /// 1 セルごとに
    ///
    /// <code>
    ///   f(d) = delta * (1 - d^2 / R^2)          d はセル単位の距離、d>=R で 0
    ///   accX += f(dx,dz) - f(dx+1,dz)           ← **水面の傾き**に足される
    /// </code>
    ///
    /// つまり<b>そこに水の山があるかのようにソルバを騙す仮想の山</b>である。
    /// 体積は足さない ——<b>海底が隆起したのと同じ</b>で、これは津波の
    /// 教科書どおりの発生源そのものである。
    ///
    /// <list type="bullet">
    /// <item><c>delta &gt; 0</c> ＝ 仮想の山 → 水は<b>外へ</b>流れる</item>
    /// <item><c>delta &lt; 0</c> ＝ 仮想の窪み → 水は<b>中へ</b>流れる</item>
    /// </list>
    ///
    /// ── 所有者の 4 段が、そのまま外力の符号になる ──────────────────────
    ///
    /// <code>
    ///  ① 隆起    delta &lt; 0   水が中心へ集まり、震源の海面が盛り上がる
    ///  ② 台地    delta &gt; 0   盛り上がりが外へ押し出されて広がる
    ///  ③ ドーナツ  delta &gt; 0   中心の水が出ていくので中央は海面へ戻る
    ///  ④ 伝播    delta = 0   外力を切る。あとは重力波として同心円状に走り続ける
    /// </code>
    ///
    /// ★★ **④ は「作らない」。** ソルバが勝手にやる —— バニラの水の壁と
    ///   同じ経路である。ここが作り直しの肝で、前の版はここを自分で描こうとして
    ///   失敗していた。
    /// </summary>
    public static class TsunamiSource
    {
        // ── 段の切れ目（**sim フレーム**。1 水ステップ ≒ 1 sim フレーム、IL 実測）──
        //
        //   実秒の目安は 60 フレーム/実秒（ゲーム速度 1）で割ったもの。

        /// <summary>① 水を中心へ集めて隆起を立てる（≒ 5 実秒）。</summary>
        public const float DrawInFrames = 300f;

        /// <summary>①→② の切り替えにかける時間（≒ 1.7 実秒）。**段差を作らない。**</summary>
        public const float TurnFrames = 100f;

        /// <summary>② ③ 外へ押し出す（ここまでで ≒ 15 実秒）。</summary>
        public const float PushFrames = 900f;

        /// <summary>外力を 0 へ抜くまで（≒ 18 実秒）。**ここから先はソルバに任せる。**</summary>
        public const float TotalFrames = 1080f;

        /// <summary>
        /// 発生源の半径（セル。1 セル 16 m）。
        ///
        /// ★ IL では <c>R = 1 + max(maxX-origX, origX-minX)</c> ——<b>X しか見ない。</b>
        ///   120 セル ＝ 1920 m。震源の「隆起」の大きさそのものである。
        /// </summary>
        public const int RadiusCells = 120;

        /// <summary>同上をメートルで。</summary>
        public const float RadiusMetres = RadiusCells * 16f;

        /// <summary>① で中心へ引く強さ（押し出す強さに対する比）。</summary>
        public const float DrawInFactor = 0.45f;

        /// <summary><c>WaterWave.m_delta</c> は Int16。**ここを超えると折り返す。**</summary>
        public const int MaxDeltaUnits = 32767;

        /// <summary><c>m_delta</c> の 1 m ぶん（IL: <c>depth * 65536 / 1024</c>）。</summary>
        public const int UnitsPerMetre = 64;

        // ── バニラの目盛り ────────────────────────────────────────

        /// <summary>
        /// <c>TsunamiAI.m_height</c> のプレハブ実測値（m）。
        /// sharedassets55 の GameObject <c>Tsunami</c> から読んだ。
        /// </summary>
        public const float VanillaHeightMetres = 64f;

        /// <summary>
        /// バニラの津波が外周の海面を持ち上げる高さ（<c>m_delta</c> の単位）。
        ///
        /// <code>  round(m_height * 65536/1024 * intensity / 55)  </code>
        ///
        /// ★★ **これを目盛りに使う。** 自分で決めた数字ではなく
        ///   <b>DLC が実際に海へ与えている外力の大きさ</b>なので、
        ///   「強すぎ／弱すぎ」を議論できる唯一の基準である。
        ///   intensity 100 で 7447（＝116 m）。
        /// </summary>
        public static int VanillaDeltaUnits(byte intensity)
        {
            float d = VanillaHeightMetres * UnitsPerMetre * intensity / 55f;
            int units = (int)(d + 0.5f);
            if (units > MaxDeltaUnits) return MaxDeltaUnits;
            if (units < 0) return 0;
            return units;
        }

        /// <summary>
        /// 押し出しの強さ（バニラの外力に対する比）。
        ///
        /// ★ 1.0 ＝「DLC が外周に与えているのと同じ大きさの外力を、震源に与える」。
        ///   **実機を見て動かすのはここ 1 か所**である。
        /// </summary>
        public const float PushFactor = 1.0f;

        /// <summary>この地震が出せる外力の頂点（<c>m_delta</c> の単位）。</summary>
        public static int PeakDeltaUnits(byte intensity)
        {
            float d = VanillaDeltaUnits(intensity) * PushFactor;
            int units = (int)(d + 0.5f);
            if (units > MaxDeltaUnits) return MaxDeltaUnits;
            if (units < 0) return 0;
            return units;
        }

        /// <summary>
        /// 地震から <paramref name="elapsedFrames"/> フレーム後に
        /// <c>WaterWave.m_delta</c> へ入れる値。
        ///
        /// <b>負が「水を中心へ集める」、正が「外へ押し出す」。</b>
        /// 終わったら 0 —— <b>呼び出し側はそれで波を解放する。</b>
        /// </summary>
        public static int DeltaAt(float elapsedFrames, int peakUnits)
        {
            if (IsBad(elapsedFrames) || elapsedFrames < 0f) return 0;
            if (elapsedFrames >= TotalFrames) return 0;
            if (peakUnits <= 0) return 0;

            float f = DriveAt(elapsedFrames);
            int units = (int)(f * peakUnits + (f >= 0f ? 0.5f : -0.5f));

            if (units > MaxDeltaUnits) return MaxDeltaUnits;
            if (units < -MaxDeltaUnits) return -MaxDeltaUnits;
            return units;
        }

        /// <summary>
        /// 外力の形だけ（頂点を 1 に正規化）。<c>[-DrawInFactor, 1]</c>。
        /// **段差を作らないこと** —— ソルバは傾きの差分を積むので、
        /// 段差はそのまま不自然な衝撃になる。
        /// </summary>
        public static float DriveAt(float elapsedFrames)
        {
            if (IsBad(elapsedFrames) || elapsedFrames < 0f) return 0f;
            if (elapsedFrames >= TotalFrames) return 0f;

            // ── ① 引き込み。0 から −DrawInFactor へ入り、そのまま保つ ────────
            if (elapsedFrames < DrawInFrames)
            {
                // 立ち上がりの 1/3 で入りきる（すぐ効かせたい）。
                float k = elapsedFrames / (DrawInFrames / 3f);
                return -DrawInFactor * Smooth(k);
            }

            // ── ①→② 反転。ここが「隆起が押し出されはじめる」瞬間 ──────────
            if (elapsedFrames < DrawInFrames + TurnFrames)
            {
                float k = (elapsedFrames - DrawInFrames) / TurnFrames;
                return -DrawInFactor + (1f + DrawInFactor) * Smooth(k);
            }

            // ── ② ③ 押し出し。頂点で保つ ────────────────────────────
            if (elapsedFrames < PushFrames) return 1f;

            // ── 外力を抜く。**切らずに抜く** ────────────────────────
            float w = (elapsedFrames - PushFrames) / (TotalFrames - PushFrames);
            return 1f - Smooth(w);
        }

        /// <summary>
        /// 外力を切ったあと（＝④）か。ここから先は<b>ソルバだけが波を運ぶ</b>ので、
        /// 呼び出し側は水波を解放してよい。
        /// </summary>
        public static bool IsFinished(float elapsedFrames)
        {
            return !IsBad(elapsedFrames) && elapsedFrames >= TotalFrames;
        }

        /// <summary>今どの段か（**英語・診断用**）。</summary>
        public static string StageAt(float elapsedFrames)
        {
            if (IsBad(elapsedFrames) || elapsedFrames < 0f) return "not started";
            if (elapsedFrames < DrawInFrames) return "1 the sea is drawn in over the epicentre";
            if (elapsedFrames < DrawInFrames + TurnFrames) return "2 the bulge starts to spread";
            if (elapsedFrames < PushFrames) return "3 the centre hollows out into a ring";
            if (elapsedFrames < TotalFrames) return "4 the drive is fading; the solver takes over";
            return "5 done - the ring is on its own now";
        }

        /// <summary>smoothstep。折れ目を作らない。</summary>
        private static float Smooth(float k)
        {
            if (k <= 0f) return 0f;
            if (k >= 1f) return 1f;
            return k * k * (3f - 2f * k);
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
