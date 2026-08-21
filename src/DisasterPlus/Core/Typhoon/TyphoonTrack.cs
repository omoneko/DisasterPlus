using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// 台風の位相。<c>Idle</c> は「持続時間が読めていないので位相を名乗れない」。
    /// **0 フレーム目を <c>Idle</c> にしない** —— それは「まだ接近していない」ではなく
    /// 「まだ何も分かっていない」の意味で使う（<see cref="TyphoonTrack.PhaseAt"/>）。
    /// </summary>
    public enum TyphoonPhase
    {
        Idle,
        Approaching,
        Peak,
        Passing,
        Gone,
    }

    /// <summary>
    /// 台風の経路。**ここにある数字は全て本 MOD が発明したものである。**
    /// バニラに「移動する台風」は存在せず、参照すべき制約も無い（IL 事実文書 §E-1）。
    /// 唯一の例外は <see cref="IntensityAt"/> の包絡線で、これは
    /// ThunderStormAI.SimulationStep の落雷本数ランプ
    /// c = min(100, (f-act)&gt;&gt;3, (act+dur-f)&gt;&gt;3) と**同じ台形**にしてある（§A-1）。
    /// 別の形にすると、④が「最盛期」と表示している瞬間にバニラの落雷が
    /// 減っている、という食い違いが起きる。
    ///
    /// **経路は elapsedFrames の閉じた関数である。** 積算にすると経路が
    /// 呼び出し履歴に依存し、ゲーム速度・ポーズ・ロード直後で同じセーブが
    /// 違う経路を描くうえ、ユニットテストで固定できなくなる。
    /// 曲率を種から 1 個引いて円弧にすれば閉形式が存在する（<see cref="ArcPosition"/>）。
    ///
    /// **速度だけは④の自由ではない。** ThunderStormAI.IsStillActive は
    /// currentFrame - m_activationFrame &lt; m_activeDuration なので（§A-1）、
    /// 台風は m_activeDuration より長生きできない。<see cref="SpeedFor"/> が
    /// 経路長をその持続時間で割る。**m_activeDuration が読めなければ 0 を返し、
    /// 呼び出し側は台風を 1 個も起こさない**（設計書 §6）。
    ///
    /// 乱数は <see cref="DisasterPlus.Core.Common.DeterministicRandom"/> だけを使う。
    /// VanillaRandomizer は**使わない** —— ここで決めるのはバニラが引く値ではなく、
    /// ④が発明した判断である（両ファイルの doc にある判別の規則）。
    ///
    /// 唯一の履歴依存は上陸減衰（<see cref="DecayAfter"/>）で、これは経路ではなく
    /// 「これまでどれだけ陸の上に居たか」そのものなので、呼び出し側が float 1 個を
    /// 持ち越す。1 ステップぶんだけをここで純関数として持つ。
    /// </summary>
    public static class TyphoonTrack
    {
        /// <summary>マップ半辺（m）。CS のマップは 1080 セル × 16 m = 17280 m 四方。</summary>
        public const float MapHalfExtent = 8640f;

        /// <summary>
        /// 想定する総経路長（m）。<see cref="SpeedFor"/> がこれを
        /// <c>m_activeDuration</c> で割って速度にする。
        ///
        /// ★ **マップの一辺そのものにしてある**（発明した数ではない）。台風は
        ///   プレイヤーが指した地点から発生するので（<see cref="CentreAt"/>）、
        ///   「持続時間いっぱいでマップを 1 回横切る」が自然な尺度である。
        ///   以前はマップ外の進入点から入ってくる設計で、進入距離ぶんを足した
        ///   24000 m を使っていた。**その進入距離はもう存在しない。**
        /// </summary>
        public const float NominalPathLength = MapHalfExtent * 2f;

        public const float MinSpeedMetresPerFrame = 0.25f;
        public const float MaxSpeedMetresPerFrame = 6f;

        /// <summary>
        /// 1 フレームあたりの旋回量の上限（rad）。これでも 20000 フレームで 0.4 rad
        /// 曲がる。実在の台風の急旋回はバニラの風向が追随できないので（§A-4）、
        /// ここを大きくしても画面上は嘘になるだけである。
        /// </summary>
        public const float MaxCurvatureRadPerFrame = 0.00002f;

        public const float LandDecayPerMinute = 0.9f;
        public const float SeaRecoveryPerMinute = 0.2f;
        public const float MaxDecay = 255f;

        /// <summary>強度包絡線の立ち上がり／立ち下がりが持続時間に占める割合。§A-1 の
        /// <c>&gt;&gt;3</c> の台形を比で書き直したもの。</summary>
        public const float RampFraction = 0.25f;

        /// <summary>
        /// 包絡線の下限（最盛期に対する比）。**生きている台風を強度 0 にしないため**にある。
        ///
        /// 台形の素の形は 0 フレーム目でちょうど 0 を返す。ところがその値は
        /// <c>DisasterData.m_intensity</c> にそのまま書かれ、ログにも出る ——
        /// 実機の初回テストで <c>intensity=0</c> と出たのはこれである。
        /// 強度 0 の災害はバニラ側の全部（落雷本数のランプ、ハザードマップの半径）が
        /// 0 になるので、**居るのに何も起きない台風**になり、しかも
        /// 「値が読めなかった」との区別が付かない。
        ///
        /// バニラの台形とわずかにずれるが、ずれるのは立ち上がり／立ち下がりの
        /// 端の数百フレームだけである。**0 と紛らわしいことのほうが高くつく。**
        /// </summary>
        public const float MinRampFraction = 0.08f;

        /// <summary>
        /// 生きている台風が名乗る最小の強度。丸めで 0 に落ちるのを防ぐだけの値で、
        /// <b>上陸で減衰しきった台風は今までどおり 0 を返す</b>（そちらは
        /// 「弱くなった」という事実であって、丸め誤差ではない）。
        /// </summary>
        public const byte MinLiveIntensity = 1;

        /// <summary>
        /// これ未満の曲率は直線として扱う（rad/frame）。
        ///
        /// 円弧の閉形式は v/κ を含むので κ = 0 で 0/0 になる。実際に使う曲率は
        /// <see cref="MaxCurvatureRadPerFrame"/>（2e-5）までなので、この閾値の
        /// 3 桁下に落ちる経路は「ほぼ直進」であり、20000 フレーム走らせても
        /// 直線との差は数 m にしかならない。
        /// </summary>
        private const float CurvatureEpsilon = 1e-8f;

        // マジックナンバーの由来: いずれも ASCII 4 文字。DeterministicRandom.Unit の
        // 第 2 引数（用途タグ）で、同じ種から独立な値を引くためだけにある。
        private const uint SaltBearing = 0x54595048u;    // "TYPH"
        private const uint SaltCurvature = 0x43555256u;  // "CURV"

        /// <summary>進行方位（rad、[0, 2π)）。種だけで決まる。</summary>
        public static float BearingOf(uint seed)
        {
            return DeterministicRandom.Unit(seed, SaltBearing) * 6.28318531f;
        }

        /// <summary>
        /// 経路の曲率（rad/frame、[-Max, +Max]）。符号が旋回の向き。
        /// </summary>
        public static float CurvatureOf(uint seed)
        {
            return (DeterministicRandom.Unit(seed, SaltCurvature) * 2f - 1f)
                   * MaxCurvatureRadPerFrame;
        }

        /// <summary>
        /// 円弧上の位置。**public なのはテストが κ→0 の連続性を直接固定するため**（計画 §1.1）。
        ///
        /// 計画が書いている閉形式は
        /// <code>
        /// X = entryX + (v/κ)(sin(θ0+κt) − sin θ0)
        /// Z = entryZ + (v/κ)(cos θ0 − cos(θ0+κt))
        /// </code>
        /// で、ここでは**代数的に等しい半角形**
        /// <code>
        /// X = entryX + v·t·cos(θ0 + κt/2)·sinc(κt/2)
        /// Z = entryZ + v·t·sin(θ0 + κt/2)·sinc(κt/2)      sinc(x) = sin(x)/x
        /// </code>
        /// を使う。sin の差を取る形は κ が小さいほど桁落ちが激しく（κ = 1e-7、
        /// v/κ = 1.2e7 で 1 m 近い誤差になる）、しかも**その誤差は経路が
        /// もっともらしいまま静かに増える**。半角形は差を取らないので κ の大小に
        /// 依らず安定で、κ→0 で連続に直線の式へ落ちる。
        /// </summary>
        public static Vec2 ArcPosition(Vec2 entry, float theta0, float curvature,
                                       float frames, float speed)
        {
            float distance = speed * frames;

            if (curvature > -CurvatureEpsilon && curvature < CurvatureEpsilon)
            {
                return new Vec2(entry.X + distance * (float)Math.Cos(theta0),
                                entry.Z + distance * (float)Math.Sin(theta0));
            }

            double half = curvature * frames * 0.5f;
            double sinc = half == 0.0 ? 1.0 : Math.Sin(half) / half;
            double mid = theta0 + half;

            return new Vec2(entry.X + (float)(distance * Math.Cos(mid) * sinc),
                            entry.Z + (float)(distance * Math.Sin(mid) * sinc));
        }

        /// <summary>
        /// 進行方位（rad、[0, 2π)）。θ(t) = θ0 + κ·t。
        ///
        /// 速度 0 は「<c>m_activeDuration</c> が読めなかった」の意味なので
        /// （<see cref="SpeedFor"/>）、台風は 1 m も動いていない。旋回もしていないので
        /// 進入方位をそのまま返す。
        /// </summary>
        public static float HeadingAt(uint seed, uint elapsedFrames, float speed)
        {
            float theta = BearingOf(seed);
            if (speed <= 0f) return theta;

            theta += CurvatureOf(seed) * elapsedFrames;

            // [0, 2π) に畳む。表示（度）と m_angle の両方でそのまま使えるようにする。
            const float twoPi = 6.28318531f;
            theta = (float)(theta - Math.Floor(theta / twoPi) * twoPi);
            if (theta < 0f) theta = 0f;
            if (theta >= twoPi) theta = 0f;
            return theta;
        }

        /// <summary>
        /// 台風の中心（真の位置。マップ外にもなる）。
        ///
        /// ★★ <paramref name="origin"/> は**プレイヤーが指した地点**である。
        ///
        /// 以前は進入点を種から引き、必ずマップの外から入ってくるようにしていた。
        /// いまは④のタイルがバニラの災害ボタンと同じように配置カーソルを構え、
        /// クリックした地点から台風が発生する（設計書 §4.1）。**種が決めるのは
        /// 進行方位と曲率だけ**で、出発点は決めない。
        ///
        /// 種を残しているのは、同じ災害 ID・同じ地点なら同じ経路を描くため
        /// （§4.1「同じセーブで再現できること」）。
        /// </summary>
        public static Vec2 CentreAt(Vec2 origin, uint seed, uint elapsedFrames, float speed)
        {
            return ArcPosition(origin, BearingOf(seed), CurvatureOf(seed),
                               elapsedFrames, speed);
        }

        /// <summary>マップの矩形の中か。**終了判定はクランプ前の中心で行うこと。**</summary>
        public static bool IsInsideMap(Vec2 centre)
        {
            return centre.X >= -MapHalfExtent && centre.X <= MapHalfExtent
                && centre.Z >= -MapHalfExtent && centre.Z <= MapHalfExtent;
        }

        /// <summary>
        /// 進行速度（m/frame）。
        ///
        /// ★ <paramref name="activeDurationFrames"/> が 0（＝プレハブが読めなかった）なら
        /// **0 を返す**。呼び出し側はこれを「不明」として扱い、台風を 1 個も起こさない。
        /// これが設計書 §6 の「読めなければ推測せず何もしない」を構造で保証している
        /// 唯一の場所である。**この <c>return 0f</c> を「安全な既定値」に書き換えてはいけない。**
        /// </summary>
        public static float SpeedFor(uint activeDurationFrames)
        {
            if (activeDurationFrames == 0u) return 0f;

            float speed = NominalPathLength / activeDurationFrames;
            if (speed < MinSpeedMetresPerFrame) return MinSpeedMetresPerFrame;
            if (speed > MaxSpeedMetresPerFrame) return MaxSpeedMetresPerFrame;
            return speed;
        }

        /// <summary>
        /// 上陸減衰の 1 ステップ。陸の上なら溜まり、海の上なら（ゆっくり）抜ける。
        /// 負にも <see cref="MaxDecay"/> 超にもならない。
        /// <paramref name="deltaMinutes"/> が 0 以下なら何も進めない。
        /// </summary>
        public static float DecayAfter(float decay, bool overLand, float deltaMinutes)
        {
            if (float.IsNaN(decay)) decay = 0f;
            if (deltaMinutes <= 0f || float.IsNaN(deltaMinutes)) return decay;

            if (overLand)
            {
                decay += LandDecayPerMinute * deltaMinutes;
                if (decay > MaxDecay) decay = MaxDecay;
                return decay;
            }

            decay -= SeaRecoveryPerMinute * deltaMinutes;
            if (decay < 0f) decay = 0f;
            return decay;
        }

        /// <summary>
        /// 今の強度。**バニラの落雷本数ランプと同じ台形**（§A-1、クラス doc）に
        /// 上陸減衰を引いたもの。
        ///
        /// ★ 台形は <see cref="MinRampFraction"/> で下から支えてある。
        ///   **生きている台風は 0 を名乗らない**（その doc）。0 を返すのは
        ///   「持続時間が読めなかった」か「減衰しきった」ときだけである。
        ///
        /// ★ <c>(byte)</c> へのキャストの**前に**必ずクランプする。C# の float→byte は
        /// 範囲外で未定義に近い値を返す（負値が 255 付近に化ける）ので、
        /// 減衰しきった台風が最強になる、という最悪の壊れ方をする。
        /// </summary>
        public static byte IntensityAt(byte peak, uint elapsedFrames, uint activeDurationFrames,
                                       float decay)
        {
            if (activeDurationFrames == 0u) return 0;

            float duration = activeDurationFrames;
            float elapsed = elapsedFrames;
            float rampFrames = duration * RampFraction;

            float ramp = 1f;
            if (rampFrames > 0f)
            {
                float rise = elapsed / rampFrames;
                float fall = (duration - elapsed) / rampFrames;
                if (rise < ramp) ramp = rise;
                if (fall < ramp) ramp = fall;
            }
            // ★ 台形の端を 0 のままにしない（MinRampFraction の doc）。
            if (ramp < MinRampFraction) ramp = MinRampFraction;
            if (ramp > 1f) ramp = 1f;

            float value = peak * ramp - decay;
            if (float.IsNaN(value) || value <= 0f) return 0;
            if (value >= 255f) return 255;

            // ★ 丸めで 0 に落ちた「生きている台風」を 0 と名乗らせない。
            //   減衰しきった側（value <= 0）は上で 0 を返しており、ここには来ない。
            byte rounded = (byte)value;
            return rounded == 0 ? MinLiveIntensity : rounded;
        }

        /// <summary>
        /// 位相。<paramref name="activeDurationFrames"/> が 0 なら
        /// <see cref="TyphoonPhase.Idle"/>（持続時間が読めていないので位相を名乗らない）。
        /// </summary>
        public static TyphoonPhase PhaseAt(uint elapsedFrames, uint activeDurationFrames)
        {
            if (activeDurationFrames == 0u) return TyphoonPhase.Idle;

            float f = (float)elapsedFrames / activeDurationFrames;
            if (f >= 1f) return TyphoonPhase.Gone;
            if (f < 0.3f) return TyphoonPhase.Approaching;
            if (f < 0.7f) return TyphoonPhase.Peak;
            return TyphoonPhase.Passing;
        }
    }
}
