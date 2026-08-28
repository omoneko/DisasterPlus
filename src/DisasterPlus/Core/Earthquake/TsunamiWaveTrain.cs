using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>海溝型地震の津波。</b>**エンジン非依存の純関数だけ。**
    ///
    /// ── 所有者の指示（2026-08-29）─────────────────────────────────
    ///
    /// &gt; 私が考えているのは、地震→すぐに震源地に海面の巨大な隆起が生成→
    /// &gt; 隆起が台地状に拡大→ある程度大きくなったら中央は緩やかに沈下して
    /// &gt; 元の海面に（ドーナツ状）→同心円状の水の壁が、減衰することなく拡散する
    /// &gt; という流れです。現在のような海面全体の隆起は不要です。
    ///
    /// **その 4 段をそのまま式にした。** 段を跨いでも形が飛ばないよう、
    /// 全部を 1 本の関数（<see cref="RiseAt"/>）で連続に繋いである。
    ///
    /// <code>
    ///  ① 隆起   0 〜 BulgeSeconds
    ///     震源に半径 BulgeStartRadius の山が立ち上がる（高さ 1.0）
    ///
    ///  ② 拡大   BulgeSeconds 〜 SpreadSeconds
    ///     山の縁が外へ広がり、天面が平らな**台地**になる
    ///
    ///  ③ 陥没   SpreadSeconds 〜 CollapseSeconds
    ///     台地の**中央だけ**が海面へ戻り、**ドーナツ**になる
    ///
    ///  ④ 伝播   CollapseSeconds 〜 TotalSeconds
    ///     ドーナツの環が<b>減衰せずに</b>外へ広がる ＝ 同心円状の水の壁
    /// </code>
    ///
    /// ── ★★ 「減衰することなく」をどう守るか ────────────────────────────
    ///
    /// 波は<b>高さを保ったまま</b>広がる。実際の津波は外洋では
    /// ほとんど減衰しない（エネルギーは広がるが、深い海では波高が落ちにくい）ので、
    /// これは物理的にも正しい。
    ///
    /// ★ ただし<b>最後には必ず 0 へ戻す</b>（<see cref="EnvelopeAt"/>）。
    ///   戻さないと呼び出し側が水位を下げる合図を受け取れず、
    ///   <b>持ち上げた海がセーブに残る</b>。減衰しないのは「広がっている間」だけである。
    ///
    /// ── ★★ 「海面全体の隆起は不要」────────────────────────────────
    ///
    /// 1 つ前の版は wake（前線の内側に残る水位）を敷いていた。
    /// **それを捨てた。** ③ の陥没で中央は海面へ戻るので、
    /// <b>環の内側は平常の海</b>である。環の外も平常。上がっているのは環だけ。
    ///
    /// ── なぜ DLC の津波を使わないのか（IL 実測）──────────────────────────
    ///
    /// <c>WaterSimulation.SimulateWater</c> は波を型で振り分ける（IL_0344–03D0）:
    /// <c>m_type == 1</c>（TSUNAMI）は <c>m_tsunamiWaves</c> へ入り、それを読む
    /// 唯一の場所（IL_16C7–16F9）は<b>マップ外周リングを回るループの中</b>である。
    /// つまり <c>TYPE_TSUNAMI</c> の波は<b>どこに置いても外周からしか効かない。</b>
    /// 「震源中心の同心円」は原理的に作れない。
    /// </summary>
    public static class TsunamiWaveTrain
    {
        // ── 段の切れ目（秒。ゲーム内時間）──────────────────────────────

        /// <summary>① 隆起が立ち上がりきるまで。**「すぐに」なので短い。**</summary>
        public const float BulgeSeconds = 12f;

        /// <summary>② 台地に育ちきるまで。</summary>
        public const float SpreadSeconds = 40f;

        /// <summary>③ 中央が沈下しきって環になるまで。</summary>
        public const float CollapseSeconds = 78f;

        /// <summary>④ 環が広がり終わるまで（＝全体の終わり）。</summary>
        public const float TotalSeconds = 900f;

        // ── 形 ────────────────────────────────────────────────

        /// <summary>① で立ち上がる隆起の半径（m）。</summary>
        public const float BulgeStartRadiusMetres = 900f;

        /// <summary>② で台地が広がりきる半径（m）。</summary>
        public const float PlateauRadiusMetres = 2600f;

        /// <summary>
        /// ④ の環の厚み（m）。**水の壁の厚みそのもの**である。
        /// 薄いと海岸を素通りし、厚いと「うねり」に見える。
        /// </summary>
        public const float RingWidthMetres = 900f;

        /// <summary>環が外へ広がる速さ（m/秒）。</summary>
        public const float SpeedMetresPerSecond = 45f;

        /// <summary>波が届く限界（m）。ここから外へは水源も置かない。</summary>
        public const float ReachEdgeMetres = 8200f;

        /// <summary>
        /// 環に「なり切った」ときの高さ（隆起の高さに対する比）。
        ///
        /// ★ 1 を超える。ドーナツになるとき、中央の水が<b>環へ寄せられる</b>ので
        ///   環は元の隆起より高くなる —— 実際の津波の生成過程でもそうである。
        /// </summary>
        public const float RingPeakFactor = 1.15f;

        /// <summary>包絡線が落ちはじめる時刻（<see cref="TotalSeconds"/> に対する比）。</summary>
        public const float FadeFromFraction = 0.86f;

        // ── 高さ ──────────────────────────────────────────────

        /// <summary>いちばん高い波の高さ（m）の上限。</summary>
        public const float MaxAmplitudeMetres = 22f;

        /// <summary>同じく下限（これ未満は「津波」と呼べない）。</summary>
        public const float MinAmplitudeMetres = 5f;

        /// <summary>地震の強度（0〜255）から、いちばん高い波の高さ（m）を出す。</summary>
        public static float AmplitudeOf(byte intensity)
        {
            float a = MaxAmplitudeMetres * (intensity / 255f);
            if (a < MinAmplitudeMetres) return MinAmplitudeMetres;
            return a;
        }

        /// <summary>
        /// 環（水の壁）の中心が今いる半径（m）。
        /// ③ が終わるまでは台地の縁に居り、④ から外へ走り出す。
        /// </summary>
        public static float RingRadiusAt(float elapsedSeconds)
        {
            if (IsBad(elapsedSeconds) || elapsedSeconds <= 0f) return 0f;

            if (elapsedSeconds <= BulgeSeconds) return BulgeStartRadiusMetres;

            if (elapsedSeconds <= SpreadSeconds)
            {
                // ② 台地が広がる。
                float k = (elapsedSeconds - BulgeSeconds) / (SpreadSeconds - BulgeSeconds);
                return BulgeStartRadiusMetres
                       + (PlateauRadiusMetres - BulgeStartRadiusMetres) * Smooth(k);
            }

            if (elapsedSeconds <= CollapseSeconds) return PlateauRadiusMetres;

            // ④ 環が外へ走る。**速さは一定**（減衰しないのと同じ理由）。
            return PlateauRadiusMetres
                   + (elapsedSeconds - CollapseSeconds) * SpeedMetresPerSecond;
        }

        /// <summary>
        /// 震源から <paramref name="distanceMetres"/> の地点で、地震から
        /// <paramref name="elapsedSeconds"/> 秒後の<b>海面の持ち上がり</b>（m、0 以上）。
        ///
        /// 終わったあとは 0 を返す —— <b>呼び出し側はそれで水位を戻す。</b>
        /// </summary>
        public static float RiseAt(float distanceMetres, float elapsedSeconds,
                                   float amplitudeMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedSeconds) || elapsedSeconds < 0f) return 0f;
            if (IsBad(amplitudeMetres) || amplitudeMetres <= 0f) return 0f;
            if (elapsedSeconds >= TotalSeconds) return 0f;
            if (distanceMetres >= ReachEdgeMetres) return 0f;

            float envelope = EnvelopeAt(elapsedSeconds);
            if (envelope <= 0f) return 0f;

            float shape = ShapeAt(distanceMetres, elapsedSeconds);
            if (shape <= 0f) return 0f;

            return amplitudeMetres * envelope * shape;
        }

        /// <summary>
        /// 形だけ（高さ 1 に正規化）。<b>4 つの段がここで繋がっている。</b>
        /// </summary>
        public static float ShapeAt(float distanceMetres, float elapsedSeconds)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedSeconds) || elapsedSeconds <= 0f) return 0f;

            float ring = RingRadiusAt(elapsedSeconds);
            if (ring <= 0f) return 0f;

            // ── ④ の「どれだけドーナツになっているか」 ─────────────────
            //    0 = 中央まで詰まった台地 / 1 = 完全な環
            float hollow = Hollowness(elapsedSeconds);

            // 環（＝縁）の高さ。ドーナツになるほど高くなる。
            float peak = 1f + (RingPeakFactor - 1f) * hollow;

            // ── 縁より外は、いつでも釣鐘で落ちる ─────────────────────
            if (distanceMetres > ring)
            {
                float over = distanceMetres - ring;
                if (over >= RingWidthMetres) return 0f;
                return peak * Bell(over / RingWidthMetres);
            }

            // ── 縁より内側 ──────────────────────────────────────
            //    hollow = 0 なら平らな天面（台地）。
            //    hollow = 1 なら中央は 0（＝海面）まで落ちる。
            float inner = ring - RingWidthMetres;
            if (inner < 0f) inner = 0f;

            if (distanceMetres >= inner)
            {
                // 内側の壁。縁から inner へ向かって、hollow のぶんだけ落ちる。
                float k = (ring - distanceMetres) / (ring - inner + 1e-3f);
                float floorLevel = 1f - hollow;      // 中央の高さ
                return peak + (floorLevel * peak - peak) * Smooth(k) * hollow
                       + (1f - hollow) * 0f;
            }

            // 中央部。台地のうちは 1、ドーナツになりきると 0。
            return peak * (1f - hollow);
        }

        /// <summary>
        /// 中央がどれだけ抜けているか <c>[0,1]</c>。
        /// ③ の段で 0 → 1 へ、**緩やかに**動く（所有者の「緩やかに沈下して」）。
        /// </summary>
        public static float Hollowness(float elapsedSeconds)
        {
            if (IsBad(elapsedSeconds) || elapsedSeconds <= SpreadSeconds) return 0f;
            if (elapsedSeconds >= CollapseSeconds) return 1f;

            float k = (elapsedSeconds - SpreadSeconds) / (CollapseSeconds - SpreadSeconds);
            return Smooth(k);
        }

        /// <summary>
        /// 全体の包絡線 <c>[0,1]</c>。
        ///
        /// ★★ **④ のあいだは 1 のまま**である（所有者の「減衰することなく拡散する」）。
        ///   落とすのは<b>いちばん最後だけ</b> —— それは演出ではなく、
        ///   <b>持ち上げた海を戻すための合図</b>である（戻さないとセーブに残る）。
        /// </summary>
        public static float EnvelopeAt(float elapsedSeconds)
        {
            if (IsBad(elapsedSeconds) || elapsedSeconds < 0f) return 0f;
            if (elapsedSeconds >= TotalSeconds) return 0f;

            float w = elapsedSeconds / TotalSeconds;
            if (w <= FadeFromFraction) return 1f;

            return (1f - w) / (1f - FadeFromFraction);
        }

        /// <summary>釣鐘の外側半分。0 で 1、1 で 0。両端で傾きも 0。</summary>
        private static float Bell(float k)
        {
            if (k <= 0f) return 1f;
            if (k >= 1f) return 0f;
            return 0.5f * (1f + (float)Math.Cos(Math.PI * k));
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
