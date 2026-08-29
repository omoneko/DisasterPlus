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
    ///  ① 隆起   0 〜 BulgeFrames
    ///     震源に半径 BulgeStartRadius の山が立ち上がる（高さ 0 → 1）
    ///
    ///  ② 拡大   BulgeFrames 〜 SpreadFrames
    ///     山の縁が外へ広がり、天面が平らな**台地**になる
    ///
    ///  ③ 陥没   SpreadFrames 〜 CollapseFrames
    ///     台地の**中央だけ**が海面へ戻り、**ドーナツ**になる
    ///
    ///  ④ 伝播   CollapseFrames 〜 TotalFrames
    ///     ドーナツの環が<b>減衰せずに</b>外へ広がる ＝ 同心円状の水の壁
    /// </code>
    ///
    /// ── ★★ 時計は「sim フレーム」である。ゲーム内秒ではない ────────────────
    ///
    /// **2026-08-29、実機報告「津波がやはり高さと波の継続力がとても弱いです」の
    /// 原因の 1 つがここだった。** 前の版はこの時計を<b>ゲーム内秒</b>で持ち、
    /// 呼び出し側が <c>(frame - start) / FramesPerMinute × 60</c> で渡していた。
    /// ところが
    ///
    /// <code>
    ///     SimulationManager.DAYTIME_FRAMES = 65536 / 日
    ///     ＝ 45.51 フレーム / ゲーム内分
    ///     ＝ **1 sim フレームが 1.32 ゲーム内秒**
    /// </code>
    ///
    /// なので、当時の「900 ゲーム内秒」は<b>683 フレーム ≒ 11 実秒</b>にしかならず、
    /// 肝心の④（環が広がる区間、124 ゲーム内秒）は
    /// <b>94 フレーム ≒ 1.6 実秒</b>だった。**瞬きの間に終わっていた。**
    ///
    /// ★★ プレイヤーが見ているのは<b>フレーム</b>である。ゲーム内時計で演出を
    ///   組むと、1 フレームが 1.32 ゲーム内秒であることを忘れた瞬間に
    ///   <b>体感が 1/60 になる。</b>だから単位をフレームに変えて、
    ///   名前にも <c>Frames</c> と書いた（<c>Seconds</c> という名前を残すと必ず戻る）。
    ///
    /// ★ ゲーム速度を上げればフレームも速く進む —— それは正しい挙動である
    ///   （早送りすれば波も早送りになる）。
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
    /// 環の内側は<b>平常の海</b>である（③の陥没で中央は海面へ戻る）。環の外も平常。
    /// 上がっているのは環だけ。
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
        // ── 段の切れ目（**sim フレーム**。クラス doc の ★★ を読むこと）──────────
        //
        //   実秒の目安は 60 フレーム/実秒（ゲーム速度 1）で割ったもの。

        /// <summary>① 隆起が立ち上がりきるまで（≒ 4 実秒）。</summary>
        public const float BulgeFrames = 240f;

        /// <summary>② 台地に育ちきるまで（≒ 13 実秒）。</summary>
        public const float SpreadFrames = 780f;

        /// <summary>③ 中央が沈下しきって環になるまで（≒ 25 実秒）。</summary>
        public const float CollapseFrames = 1500f;

        /// <summary>
        /// ④ が終わって全部が畳まれるまで（≒ 70 実秒）。
        ///
        /// ★ 環が <see cref="ReachEdgeMetres"/> へ出るのは
        ///   <c>CollapseFrames + (ReachEdge - Plateau) / Speed</c> ＝ 3833 フレーム。
        ///   ここはそれより少しだけ後にする —— **空回りする時間を作らない。**
        /// </summary>
        public const float TotalFrames = 4200f;

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

        /// <summary>
        /// 環が外へ広がる速さ（**m / sim フレーム**）。
        ///
        /// ★ 2.4 m/フレーム ＝ ゲーム速度 1 でおよそ 144 m/実秒。
        ///   2600 m から 8200 m までを 2333 フレーム ≒ 39 実秒かけて渡る。
        /// </summary>
        public const float SpeedMetresPerFrame = 2.4f;

        /// <summary>波が届く限界（m）。ここから外へは水源も置かない。</summary>
        public const float ReachEdgeMetres = 8200f;

        /// <summary>
        /// 環に「なり切った」ときの高さ（隆起の高さに対する比）。
        ///
        /// ★ 1 を超える。ドーナツになるとき、中央の水が<b>環へ寄せられる</b>ので
        ///   環は元の隆起より高くなる —— 実際の津波の生成過程でもそうである。
        /// </summary>
        public const float RingPeakFactor = 1.15f;

        /// <summary>包絡線が落ちはじめる時刻（<see cref="TotalFrames"/> に対する比）。</summary>
        public const float FadeFromFraction = 0.92f;

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
        /// 環が <see cref="ReachEdgeMetres"/> へ出るフレーム。
        /// **これ以降は何も持ち上がらない**ので、後始末はここから始めてよい。
        /// </summary>
        public static float RingLeavesAtFrame
        {
            get
            {
                return CollapseFrames
                       + (ReachEdgeMetres - PlateauRadiusMetres) / SpeedMetresPerFrame;
            }
        }

        /// <summary>
        /// 環（水の壁）の中心が今いる半径（m）。
        /// ③ が終わるまでは台地の縁に居り、④ から外へ走り出す。
        /// </summary>
        public static float RingRadiusAt(float elapsedFrames)
        {
            if (IsBad(elapsedFrames) || elapsedFrames <= 0f) return 0f;

            if (elapsedFrames <= BulgeFrames) return BulgeStartRadiusMetres;

            if (elapsedFrames <= SpreadFrames)
            {
                // ② 台地が広がる。
                float k = (elapsedFrames - BulgeFrames) / (SpreadFrames - BulgeFrames);
                return BulgeStartRadiusMetres
                       + (PlateauRadiusMetres - BulgeStartRadiusMetres) * Smooth(k);
            }

            if (elapsedFrames <= CollapseFrames) return PlateauRadiusMetres;

            // ④ 環が外へ走る。**速さは一定**（減衰しないのと同じ理由）。
            return PlateauRadiusMetres
                   + (elapsedFrames - CollapseFrames) * SpeedMetresPerFrame;
        }

        /// <summary>
        /// 環の内側の縁（m）。**水源を並べる帯の内側**でもある。
        ///
        /// ★★ <b>ドーナツに「なりきる」まで 0 を返す</b>（<c>Hollowness &lt; 1</c>）。
        ///   ③ の途中は中央がまだ海面より上で、<b>緩やかに沈んでいる最中</b>である。
        ///   ここで内側を切り上げると、中央を担当していた水源が黙らされて
        ///   <b>沈めるはずの水が置き去りになる</b>（水源は注ぐだけでなく
        ///   目標より高い水を吸い戻す側でもある）。
        ///   帯は必ず <see cref="ShapeAt"/> が 0 でない範囲を覆うこと。
        /// </summary>
        public static float RingInnerRadiusAt(float elapsedFrames)
        {
            if (Hollowness(elapsedFrames) < 1f) return 0f;

            float inner = RingRadiusAt(elapsedFrames) - RingWidthMetres;
            return inner > 0f ? inner : 0f;
        }

        /// <summary>
        /// 震源から <paramref name="distanceMetres"/> の地点で、地震から
        /// <paramref name="elapsedFrames"/> フレーム後の<b>海面の持ち上がり</b>（m、0 以上）。
        ///
        /// 終わったあとは 0 を返す —— <b>呼び出し側はそれで水位を戻す。</b>
        /// </summary>
        public static float RiseAt(float distanceMetres, float elapsedFrames,
                                   float amplitudeMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedFrames) || elapsedFrames < 0f) return 0f;
            if (IsBad(amplitudeMetres) || amplitudeMetres <= 0f) return 0f;
            if (elapsedFrames >= TotalFrames) return 0f;
            if (distanceMetres >= ReachEdgeMetres) return 0f;

            float envelope = EnvelopeAt(elapsedFrames);
            if (envelope <= 0f) return 0f;

            float shape = ShapeAt(distanceMetres, elapsedFrames);
            if (shape <= 0f) return 0f;

            return amplitudeMetres * envelope * shape;
        }

        /// <summary>
        /// 形だけ（高さ 1 に正規化）。<b>4 つの段がここで繋がっている。</b>
        /// </summary>
        public static float ShapeAt(float distanceMetres, float elapsedFrames)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(elapsedFrames) || elapsedFrames <= 0f) return 0f;

            float ring = RingRadiusAt(elapsedFrames);
            if (ring <= 0f) return 0f;

            // ① のあいだは高さそのものが 0 から立ち上がる（「隆起が生成」）。
            float birth = elapsedFrames < BulgeFrames
                ? Smooth(elapsedFrames / BulgeFrames)
                : 1f;
            if (birth <= 0f) return 0f;

            // ── ④ の「どれだけドーナツになっているか」 ─────────────────
            //    0 = 中央まで詰まった台地 / 1 = 完全な環
            float hollow = Hollowness(elapsedFrames);

            // 環（＝縁）の高さ。ドーナツになるほど高くなる。
            float peak = 1f + (RingPeakFactor - 1f) * hollow;

            // ── 縁より外は、いつでも釣鐘で落ちる ─────────────────────
            if (distanceMetres > ring)
            {
                float over = distanceMetres - ring;
                if (over >= RingWidthMetres) return 0f;
                return birth * peak * Bell(over / RingWidthMetres);
            }

            // ── 縁より内側 ──────────────────────────────────────
            //    hollow = 0 なら平らな天面（台地）。
            //    hollow = 1 なら中央は 0（＝海面）まで落ちる。
            float centre = peak * (1f - hollow);

            float inner = ring - RingWidthMetres;
            if (inner < 0f) inner = 0f;

            if (distanceMetres >= inner)
            {
                // 内側の壁。縁（k=0）から inner（k=1）へ向かって centre まで落ちる。
                float k = (ring - distanceMetres) / (ring - inner + 1e-3f);
                return birth * (peak + (centre - peak) * Smooth(k));
            }

            return birth * centre;
        }

        /// <summary>
        /// 中央がどれだけ抜けているか <c>[0,1]</c>。
        /// ③ の段で 0 → 1 へ、**緩やかに**動く（所有者の「緩やかに沈下して」）。
        /// </summary>
        public static float Hollowness(float elapsedFrames)
        {
            if (IsBad(elapsedFrames) || elapsedFrames <= SpreadFrames) return 0f;
            if (elapsedFrames >= CollapseFrames) return 1f;

            float k = (elapsedFrames - SpreadFrames) / (CollapseFrames - SpreadFrames);
            return Smooth(k);
        }

        /// <summary>
        /// 全体の包絡線 <c>[0,1]</c>。
        ///
        /// ★★ **④ のあいだは 1 のまま**である（所有者の「減衰することなく拡散する」）。
        ///   落とすのは<b>いちばん最後だけ</b> —— それは演出ではなく、
        ///   <b>持ち上げた海を戻すための合図</b>である（戻さないとセーブに残る）。
        /// </summary>
        public static float EnvelopeAt(float elapsedFrames)
        {
            if (IsBad(elapsedFrames) || elapsedFrames < 0f) return 0f;
            if (elapsedFrames >= TotalFrames) return 0f;

            float w = elapsedFrames / TotalFrames;
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
