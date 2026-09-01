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
        /// ★★ <b>2026-08-22、持ち主の指摘「進行をもっとゆっくりに」で半分にした。</b>
        ///   マップの一辺（17280 m）＝「持続時間いっぱいでマップを 1 回横切る」から、
        ///   <b>マップの半辺（8640 m）＝「持続時間いっぱいでマップを半分だけ渡る」</b>へ。
        ///   実測値（<c>m_activeDuration</c> = 8192）での速度は
        ///   <c>8640 / 8192 = 1.055 m/frame</c>（以前は 2.109 m/frame）で、
        ///   この速さでマップの一辺を渡り切るには <b>16384 フレーム
        ///   ＝ ゲーム内 360 分（6 時間）＝ 台風の寿命のちょうど 2 倍</b>かかる。
        ///
        /// ★★ <b>「もっとゆっくり」を寿命の延長では実現していない。その理由。</b>
        ///   <c>ThunderStormAI.IsStillActive</c> は
        ///   <c>currentFrame - m_activationFrame &lt; m_activeDuration</c> なので
        ///   （IL 事実文書 §A-1）、宿主の嵐は <b>8192 フレームより長生きできない</b>。
        ///   延ばす手は 2 つあり、どちらも高い:
        ///
        ///   1. <c>m_activationFrame</c> を毎 tick 前へ押す。すると
        ///      <c>GetFireSpreadProbability</c> の <c>1500 / (8 + (num &gt;&gt; 10))</c> の
        ///      <c>num</c> が小さいままになり、**延焼確率が最大に張り付く**
        ///      （§A-5。しかもその式はバニラの中にある）。
        ///   2. 宿主より④が長生きする。すると <c>WeatherManager</c> が
        ///      「Active な雷雨が居ない」と判断して<b>自前の雷雨災害を作り始める</b>
        ///      （§A-3。④の落雷の予算が宿主のぶんを空けて抑え込んでいる釣り合いが崩れる）。
        ///
        ///   どちらも「ゆっくり動く」より高くつく。**経路を短くするのが正解である。**
        ///
        ///   なお、経路が短くなったので<b>台風は普通マップの外へは出ない</b> ——
        ///   終わり方は「持続時間を使い切った」のほうが通常になる。終了経路は
        ///   どちらも <c>TyphoonController.Stop</c> ＝ <c>Forget</c> を通る（設計書 §4.2）。
        /// </summary>
        /// <remarks>
        /// ★★ <b>2026-09-02、マップ半辺 → 12,000 m へ伸ばした。</b>
        ///   所有者の依頼「クリックしてその場に急に発生するのではなく、マップ端で
        ///   発生して徐々にクリックした地点に近づき、その後進路を維持して立ち去る」。
        ///
        ///   進入ぶんの道のりが要る。半辺（8,640 m）のままだと、クリック地点まで
        ///   来るのに寿命のほとんどを使ってしまい、<b>最盛期を沖で迎えて
        ///   衰えながら上陸する</b>ことになる（強度の包絡線は寿命の真ん中が頂点で、
        ///   これは宿主の落雷ランプに合わせてあるので動かせない）。
        ///
        /// ★★ <b>要る道のりは「一辺」であって半辺ではない。</b>
        ///   接近に使えるのは寿命の半分（<see cref="ApproachFraction"/>。強度の頂点が
        ///   そこだから）なので、<b>半分の時間でマップ半辺を戻れる速さ</b>が要る。
        ///   マップ中央を指されたとき、端までがちょうど半辺（8,640 m）である。
        ///
        ///   <c>速さ × 寿命/2 ≥ 8,640</c> ⇔ <c>速さ ≥ 2.109</c>
        ///   ⇔ <c>経路長 ≥ 17,280 ＝ マップの一辺</c>。
        ///   12,000 m で試して落ちた（中央を指すと 6,000 m しか戻れず、
        ///   マップの中から湧いてしまう）ので、一辺まで戻した。
        ///
        /// ★ これは 2026-08-22 に「もっとゆっくり」で半分にする前の速さである。
        ///   <b>ただし当時とは意味が違う</b> —— あのときは<b>クリック地点から</b>
        ///   走り出して足早にマップを出ていた。いまは半分を接近に使うので、
        ///   街の上に居る時間はむしろ長い。**速く見えるようなら、
        ///   ここではなく <see cref="ApproachFraction"/> を下げて調整すること。**
        /// </remarks>
        public const float NominalPathLength = MapHalfExtent * 2f;

        /// <summary>
        /// ④の寿命が宿主の嵐の <c>m_activeDuration</c> の何倍か。
        ///
        /// ── ★★ なぜ倍にできるのか（2026-08-22、実機報告）────────────────────
        ///
        /// &gt; 台風についてはエフェクトがすぐに消えてしまいます。
        /// &gt; 台風がゆっくりと移動する様子を再現してください。
        ///
        /// 以前の doc は「宿主の嵐は 8192 フレームより長生きできない」と書いていた。
        /// **それは正しいが、条件を読み違えていた。** IL を読み直すと:
        ///
        /// <code>
        /// ThunderStormAI.IsStillActive :
        ///     (currentFrame - m_activationFrame) &lt; m_activeDuration
        /// ThunderStormAI.IsStillEmerging :
        ///     m_activationFrame == 0        → true（恒久）
        ///     currentFrame &lt; m_activationFrame → true
        ///     それ以外                       → false
        /// </code>
        ///
        /// 死ぬのは<b>活性化フレームからの経過</b>が上限に届いたときであって、
        /// 開始からの経過ではない。だから <c>m_activationFrame</c> を
        /// <b>「今」へ進め直せば、残り時間はそのつど満タンに戻る</b>
        /// （<c>TyphoonSlot.KeepAlive</c>）。
        ///
        /// ★ 「今」ちょうどに置くこと。
        ///   - <c>IsStillActive</c> … <c>0 &lt; 8192</c> で true
        ///   - <c>IsStillEmerging</c> … <c>now &lt; now</c> は false（Emerging に戻らない）
        ///   - <c>GetFireSpreadProbability</c> の <c>1500 / (8 + (num &gt;&gt; 10))</c> …
        ///     <c>num = 0</c> なので割る数は 8。**0 除算にならない**
        ///     （負へ引き戻すと 0 除算になる。実機で 1 度出した）
        ///
        /// 4 倍は「速度 1 でおよそ 9 分」——マップの半分を渡るのにそれだけかける。
        /// 実測の <c>m_activeDuration</c> = 8192 に対して 32768 フレームで、
        /// 速度は <c>8640 / 32768 = 0.264 m/frame</c>（以前の 4 分の 1）である。
        /// </summary>
        public const uint LifetimeMultiplier = 4u;

        /// <summary>
        /// ④の寿命（フレーム）。<paramref name="activeDuration"/> は宿主の嵐の
        /// <c>m_activeDuration</c>。**0 なら 0**（呼び出し側は台風を起こさない）。
        /// </summary>
        public static uint LifetimeFramesFor(uint activeDuration)
        {
            if (activeDuration == 0u) return 0u;

            // 桁あふれを塞ぐ（.cgs もプレハブも手で変えられうる）。
            if (activeDuration > uint.MaxValue / LifetimeMultiplier) return activeDuration;
            return activeDuration * LifetimeMultiplier;
        }

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
            return HeadingAt(new Vec2(0f, 0f), seed, elapsedFrames, speed);
        }

        /// <summary>
        /// 同上。**出発点を見て、マップを横切る向きを選ぶ。**
        /// <see cref="BearingFrom"/> のクラス doc に理由がある。
        /// </summary>
        public static float HeadingAt(Vec2 origin, uint seed, uint elapsedFrames, float speed)
        {
            return HeadingAt(origin, seed, elapsedFrames, speed, 0u);
        }

        /// <summary>
        /// 同上。<paramref name="approachFrames"/> だけ時計を戻す
        /// （<see cref="CentreAt(Vec2, uint, uint, float, uint)"/> と揃えること ——
        /// ずらし忘れると<b>進んでいる向きと表示の向きが食い違う</b>）。
        /// </summary>
        public static float HeadingAt(Vec2 origin, uint seed, uint elapsedFrames, float speed,
                                      uint approachFrames)
        {
            float theta = BearingFrom(origin, seed);
            if (speed <= 0f) return theta;

            theta += CurvatureOf(seed) * ((float)elapsedFrames - approachFrames);

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
            return CentreAt(origin, seed, elapsedFrames, speed, 0u);
        }

        /// <summary>
        /// 同上。<paramref name="approachFrames"/> だけ<b>時計を戻して</b>評価する。
        ///
        /// ★★ これが「マップ端から来る」の実体である（2026-09-02）。
        ///   <paramref name="origin"/> は<b>出発点ではなく到達点</b>になり、
        ///   <c>t = approachFrames</c> でちょうどそこを通る。
        ///   <c>t = 0</c> では円弧を後ろへ辿った先 ——ふつうはマップの外—— に居る。
        ///
        /// ★ 経路そのものは変えていない。同じ円弧の<b>どこを t = 0 と呼ぶか</b>を
        ///   ずらしただけなので、「経路は elapsedFrames の閉じた関数」という
        ///   このクラスの約束（クラス doc）は保たれる。
        /// </summary>
        public static Vec2 CentreAt(Vec2 origin, uint seed, uint elapsedFrames, float speed,
                                    uint approachFrames)
        {
            return ArcPosition(origin, BearingFrom(origin, seed), CurvatureOf(seed),
                               (float)elapsedFrames - approachFrames, speed);
        }

        /// <summary>
        /// 寿命のうち<b>接近に使う割合</b>。
        ///
        /// ★★ 0.5 にしてある。<see cref="IntensityAt"/> の包絡線は<b>寿命の真ん中が
        ///   頂点</b>（宿主の落雷ランプと同じ台形）なので、
        ///   <b>クリック地点に着いた瞬間が最盛期</b>になる。
        ///
        /// ★★ <see cref="RampFraction"/> が 0.25 なので、強度の台形は
        ///   <b>寿命の 25% から 75% までが平ら</b>である。その間に着けば満額なので、
        ///   ちょうど 0.5 である必要は無い。
        ///
        ///   だから接近の長さは<b>「端まで戻れた時点」で決める</b>のであって、
        ///   割合で決め打ちしない。割合は<b>その探索の上限</b>である。
        ///
        ///   0.5 / 0.6 で決め打ちして 2 度落ちた —— 円弧の弦は弧より短いので、
        ///   マップ中央を指されると戻り切れない
        ///   （実測: 0.5 で (8154, 2857)、0.6 で (-7848, 6774)。どちらもまだ中）。
        ///
        /// ★ 上限を 0.75 にしてあるのは、強度の台形がそこまで平らだからである。
        /// </summary>
        public const float ApproachFraction = 0.75f;

        /// <summary>
        /// 接近に<b>最低でも</b>使う割合。
        ///
        /// ★★ 端のすぐそばを指されると数百フレームで着いてしまい、
        ///   <b>まだ強度が立ち上がりきっていない</b>（台形は 25% で満額になる）。
        ///   それでは「弱い台風が通り過ぎた」で終わる。
        ///   端より手前から始めることになるが、そこはマップの外なので誰も見ていない。
        /// </summary>
        public const float MinApproachFraction = 0.25f;

        /// <summary>
        /// 進入点を探すときの刻み（フレーム）。粗くてよい ——
        /// マップの外に出たことさえ分かればいい。
        /// </summary>
        private const int ApproachProbeFrames = 64;

        /// <summary>
        /// <b>クリック地点に着くまでのフレーム数。</b>
        ///
        /// 台風の経路は「クリック地点を <c>t = これ</c> で通る円弧」である。
        /// <see cref="CentreAt"/> はこの値だけ時計を戻して評価するので、
        /// <c>t = 0</c> では<b>マップの外（か、端の近く）</b>に居る。
        ///
        /// ★ 求め方は<b>後ろ向きに辿るだけ</b>。円弧は閉形式なので負の時間を
        ///   そのまま入れられる（<see cref="ArcPosition"/> の <c>sinc</c> は偶関数）。
        ///   マップの外へ出た時点で止め、<see cref="ApproachFraction"/> で頭打ちにする。
        ///
        /// ★★ 頭打ちに当たる＝<b>クリック地点が端から遠すぎて、寿命の半分では
        ///   端まで戻れない</b>場合である。そのときは端ではなくマップの中から
        ///   湧くことになるが、**それでも「向こうから来て通り過ぎる」形は保つ**。
        ///   ここで寿命を延ばす手は採らない（<see cref="NominalPathLength"/> の
        ///   doc にある通り、宿主より長生きすると別の壊れ方をする）。
        /// </summary>
        public static uint ApproachFramesFor(Vec2 origin, uint seed, float speed,
                                             uint totalFrames)
        {
            if (speed <= 0f || totalFrames == 0u) return 0u;

            int cap = (int)(totalFrames * ApproachFraction);
            if (cap <= 0) return 0u;

            float theta = BearingFrom(origin, seed);
            float curvature = CurvatureOf(seed);

            int floor = (int)(totalFrames * MinApproachFraction);

            for (int back = ApproachProbeFrames; back <= cap; back += ApproachProbeFrames)
            {
                Vec2 at = ArcPosition(origin, theta, curvature, -back, speed);
                if (IsInsideMap(at)) continue;

                // ★ 端を出た。ただし早すぎる到達は避ける（MinApproachFraction）。
                return (uint)(back < floor ? floor : back);
            }

            return (uint)cap;
        }

        /// <summary>
        /// 進入方位の広がり（ラジアン、片側）。0 にすると必ず中心をまっすぐ通る。
        /// </summary>
        public const float BearingSpreadRadians = 0.62f;

        /// <summary>
        /// 出発点がここより中心に近ければ、向きは種だけで決める（m）。
        /// 中心そのものを指されたら「中心へ向かう向き」が定義できない。
        /// </summary>
        public const float CentreDeadZoneMetres = 900f;

        /// <summary>
        /// <paramref name="origin"/> から出発する台風の進入方位（rad）。
        ///
        /// ── ★★ なぜ種だけで決めてはいけないのか（2026-08-22、実機報告）──────────
        ///
        /// &gt; 台風の雲のエフェクトは一瞬だけ現れて消えてしまいます
        /// &gt; …可能な限り上空を巨大な台風雲がゆっくりと回転して通過しながら…
        ///
        /// 以前はここが <see cref="BearingOf"/>（種だけ）だった。方位が
        /// <b>出発点と無関係</b>なので、マップの端の近くを指して外向きの目が出ると、
        /// 台風は数百 m でマップを出て <c>TyphoonController.Stop()</c> に掛かる ——
        /// <b>雲が一瞬出て消える</b>のはこれである。バニラの雷雨（宿主）は
        /// 別の寿命で動いているので、そちらだけが残って「ただの雷雨」になる。
        ///
        /// いまは<b>中心へ向かう向きを基準に、種で ±<see cref="BearingSpreadRadians"/>
        /// だけ振る</b>。どこを指しても台風はマップを横切るので、
        /// 街の上を通過する時間がいちばん長くなる。
        ///
        /// ★ 中心のすぐ近く（<see cref="CentreDeadZoneMetres"/> の内側）を指された
        ///   ときは「中心へ向かう向き」が定義できないので、種だけで決める ——
        ///   そこから出るなら**どちらへ行ってもマップを横切る**ので、それでよい。
        /// </summary>
        public static float BearingFrom(Vec2 origin, uint seed)
        {
            float distance = (float)Math.Sqrt(origin.X * origin.X + origin.Z * origin.Z);
            if (distance < CentreDeadZoneMetres) return BearingOf(seed);

            // 中心（0,0）へ向かう向き。
            float toCentre = (float)Math.Atan2(-origin.Z, -origin.X);

            // 種で ±spread だけ振る。**同じ地点・同じ種なら同じ経路**である。
            float offset = (DeterministicRandom.Unit(seed, BearingSpreadSalt) * 2f - 1f)
                           * BearingSpreadRadians;

            const float twoPi = 6.28318531f;
            float theta = toCentre + offset;
            theta = (float)(theta - Math.Floor(theta / twoPi) * twoPi);
            if (theta < 0f) theta = 0f;
            if (theta >= twoPi) theta = 0f;
            return theta;
        }

        /// <summary>方位の振れ幅を引くときの塩。**ほかと重ねないこと。**</summary>
        private const uint BearingSpreadSalt = 0x42454152u;

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
