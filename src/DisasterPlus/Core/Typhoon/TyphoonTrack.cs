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

        /// <summary>進入点を進行方向の逆へどれだけ下げるか（m）。<see cref="EntryOf"/>。</summary>
        public const float EntryDistance = 12000f;

        /// <summary>直進経路がマップ中心から外れてよい横方向の最大量（m）。</summary>
        public const float LateralMax = 4000f;

        /// <summary>
        /// 想定する総経路長（m）。<see cref="SpeedFor"/> がこれを
        /// <c>m_activeDuration</c> で割って速度にする。進入点までの距離
        /// （最大 <see cref="EntryDistance"/> ＋ 横ずれぶん）の 2 倍弱にしてある。
        /// </summary>
        public const float NominalPathLength = 24000f;

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
        /// 進入点をここまで外へ押し出す半径（m）。
        ///
        /// マップの**角**までの距離は <see cref="MapHalfExtent"/> × √2 ≒ 12219 m で、
        /// <see cref="EntryDistance"/>（12000）より長い。つまり進入方位が 45 度付近だと
        /// 「12000 m 手前」がまだマップの中に入る。半径だけを外へ押し出せば、
        /// |entry| ≧ 12400 &gt; 8640√2 から max(|X|,|Z|) &gt; 8640 が必ず言えるので、
        /// 進入点は常にマップの外になる（<see cref="IsInsideMap"/> の否定）。
        ///
        /// 押し出しは半径方向なので進行方位 θ0 を変えず、横ずれの絶対量も
        /// <see cref="LateralMax"/> を超えない（押し出しが効くのは |lateral| が
        /// 小さいときだけで、そのとき横ずれ自身も小さい）。
        /// </summary>
        private const float OutsideRadius = 12400f;

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
        private const uint SaltLateral = 0x4C415452u;    // "LATR"

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
        /// 進入点。進行方向 θ0 の**逆**へ <see cref="EntryDistance"/> 進み、
        /// θ0 に直交する向きへ [-<see cref="LateralMax"/>, +LateralMax] ずらす。
        /// こうすると直進経路は必ずマップ中心から LateralMax 以内を通る
        /// （LateralMax 4000 &lt; MapHalfExtent 8640）＝「掠めもせずに通り過ぎる」
        /// 経路が出ない。
        ///
        /// 最後に <see cref="OutsideRadius"/> まで押し出す（その doc を参照）。
        /// </summary>
        public static Vec2 EntryOf(uint seed)
        {
            float theta = BearingOf(seed);
            float dirX = (float)Math.Cos(theta);
            float dirZ = (float)Math.Sin(theta);

            float lateral = (DeterministicRandom.Unit(seed, SaltLateral) * 2f - 1f) * LateralMax;

            // θ0 に直交する単位ベクトル。
            float x = -dirX * EntryDistance - dirZ * lateral;
            float z = -dirZ * EntryDistance + dirX * lateral;

            float r = (float)Math.Sqrt(x * x + z * z);
            if (r < OutsideRadius)
            {
                // r は EntryDistance 以上なので 0 除算にならない。
                float k = OutsideRadius / r;
                x *= k;
                z *= k;
            }
            return new Vec2(x, z);
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

        /// <summary>台風の中心（真の位置。マップ外にもなる）。</summary>
        public static Vec2 CentreAt(uint seed, uint elapsedFrames, float speed)
        {
            return ArcPosition(EntryOf(seed), BearingOf(seed), CurvatureOf(seed),
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
            if (ramp < 0f) ramp = 0f;
            if (ramp > 1f) ramp = 1f;

            float value = peak * ramp - decay;
            if (float.IsNaN(value) || value <= 0f) return 0;
            if (value >= 255f) return 255;
            return (byte)value;
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
