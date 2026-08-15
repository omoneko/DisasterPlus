using ColossalFramework;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風が天候を駆動する。<b>sim スレッド専用。</b>
    ///
    /// 小さいが間違え方が独特で、**どの間違いも例外を出さない**。
    ///
    /// ── 1. 毎 tick 書く ────────────────────────────────────
    ///
    /// <c>WeatherManager.SimulationStepImpl</c> は <c>m_targetRain</c> を
    /// <c>m_currentRain</c> と比べ、**等しいときにだけ** <c>Randomizer.Int32(20000) == 0</c>
    /// で勝手に別の値へ振り直す（IL 事実文書 §A-4、本タスクで IL_0222 を再確認）。
    /// つまり 1 回書いて放置すると、<c>current</c> が <c>target</c> に追いついた瞬間から
    /// 1/20000/step で天候を奪われる。**毎 tick 書いていれば奪われない。**
    /// 霧も雲も同型で、遷移レートは雨・霧 <c>0.0002/step</c>、雲 <c>0.0008/step</c>。
    ///
    /// ── 2. <c>m_forceWeatherOn</c> を書かないと、天候 OFF の環境で全部 0 になる ──
    ///
    /// 同メソッドは <c>w = m_enableWeather ? 1 : 0</c> を作り、
    /// <c>m_forceWeatherOn != 0</c> なら <c>w = Max(w, m_forceWeatherOn)</c> にする。
    /// <c>w &lt; 1</c> の枝（IL_053D）は <c>m_targetRain / Fog / Cloud</c> を 0 にし、
    /// <c>current</c> も <c>Min(current, w)</c> で潰す。**プレイヤーが天候を切っていると
    /// 台風は晴天の下を進む。** <c>m_forceWeatherOn</c> は <c>0.001/step</c> で減衰するので
    /// 毎 tick 書き直す。値 2f はバニラの嵐・竜巻が書いているのと同じである（§A-1）。
    ///
    /// ── 3. バニラの嵐と 256 フレームに 1 度だけ食い違う（対策は不要） ─────────
    ///
    /// ④の宿主は <c>SelfTrigger</c> 付きの <c>ThunderStormAI</c> なので、**バニラ自身も**
    /// Active 分岐で <c>m_targetRain = 1; m_targetCloud = 1; m_targetFog = 0;
    /// m_forceWeatherOn = 2</c> を書く（§A-1）。ただしそれが走るのは
    /// **256 sim フレームに 1 回**（§E-2）で、④は毎 tick 書く。
    /// <c>m_currentRain</c> は <c>0.0002/step</c> でしか動かないので、
    /// **1 tick ぶんの target の食い違いは画面に出ない。対策は不要である。**
    /// Harmony パッチで抑えに行かないこと。
    ///
    /// 本タスクで走査したところ、同じ性質の書き手がもう 1 つある:
    /// <c>ForestFireAI.SimulationStep</c> は Active（<c>m_flags &amp; 8</c>）の間
    /// <c>m_targetRain = 0f</c> を書く（IL_001F。事実文書に記載が無かったので追記する）。
    /// ④の落雷が森林火災を起こせば同居しうるが、こちらも 256 フレームに 1 回なので
    /// 結論は同じ。
    ///
    /// ── 4. 風向は台風に追いつかない（<c>m_windDirection</c> を直接書かない） ──────
    ///
    /// <c>m_directionSpeed</c> は <c>+0.001/step</c> ずつしか上がらず、上限は残角×0.001
    /// （§A-4）。**台風の急旋回は表現できない。** <c>m_windDirection</c> を直接書けば
    /// 可能だが、書き手はバニラの <c>SimulationStepImpl</c> と <c>Data.Deserialize</c> の
    /// 2 つだけで、毎フレーム補間しているものを外から差し替えると木と道路の揺れが飛ぶ。
    /// **直接書かない。** 代わりに T5 のパネルが「風向はゆっくりしか変わりません
    /// （ゲームの制約）」を 1 行出す。
    ///
    /// 角度の対応は推測ではなく実測してある。<c>WeatherManager.EndRenderingImpl</c> は
    /// <c>rad = m_windDirection * 0.01745329</c> から
    /// <c>_WindDirection = (sin rad, 0, cos rad, ...)</c> を作る（IL_0000–0050）ので、
    /// <c>m_windDirection</c> は **0 度 = +Z、90 度 = +X の方位角**である。
    /// 一方④の進行方位 φ は <c>(cos φ, sin φ)</c> を (X, Z) とする数学系なので、
    /// 変換は <c>θ = 90 - φ[deg]</c> になる（<see cref="WindDegreesOf"/>）。
    ///
    /// ── 5. 雨量と雲量の式は④が発明したものである ────────────────────
    ///
    /// <code>
    /// near  = 1 - clamp01(|centre| / galeRadius)     // 強風域に入るほど 1 へ
    /// rain  = clamp01(0.35 + 0.65 * near)
    /// cloud = clamp01(0.55 + 0.45 * near)
    /// </code>
    /// バニラに参照すべき制約は無い（設計書 §1.2）。距離は**マップ原点＝都市の中心**から
    /// 台風の中心（クランプ前）までで測る。
    ///
    /// ── 6. ★ 雨量 0.8 超がバニラの雷雨を生むことについての判断 ──────────────
    ///
    /// **④は意図して雨量を 0.8 より上へ持っていく。** 台風の最盛期に土砂降りに
    /// ならないほうが嘘であり、しかも §A-5 のとおり**風速を上げるフィールドは
    /// 存在しない**ので、④が「強さ」を天候として出せる手段は雨量と雲量しか無い。
    /// その代償を隠さずここに書く。
    ///
    /// <c>WeatherManager.SimulationStepImpl</c> 末尾（IL_09D3–0A30）:
    /// <c>m_currentRain &gt; 0.8f &amp;&amp; m_lightningQueue.m_size == 0</c> なら
    /// <c>QueueLightningStrike(uint)</c> を呼ぶ。**本タスクでその 1 引数版の IL を
    /// 全部読み、事実文書 §A-3 の記述をさらに絞り込んだ**:
    ///
    /// <code>
    /// IL_00CA-0125  災害バッファを走査し、m_flags &amp; 8 (Active) かつ
    ///               Info == ThunderStormAI プレハブのものを探す
    /// IL_012A       ★ 見つかったら brtrue で **CreateDisaster を飛ばす**
    /// IL_0131-01B1  見つからなかったときだけ CreateDisaster（戻り値検査あり）→
    ///               m_intensity = 10 → m_targetPosition = マップ一様乱数点 →
    ///               StartNow → ActivateNow
    /// </code>
    ///
    /// したがって:
    ///
    /// 1. **④の台風が Active の間、ゲームは新しい雷雨災害を作らない。** 既にある
    ///    ④の嵐を見つけて再利用し、落雷 1 発をそのグループに足すだけである。
    ///    計画 §3.1 が心配していた「④の雨がゲームに嵐を作らせる自己増殖」は、
    ///    Active の間は**起きない**（案 (b) を却下した結論そのものは変わらない ——
    ///    却下の第 2 の理由「プレイヤーから原因が MOD だと分からない」は残る）。
    /// 2. 新しい雷雨災害が生まれうるのは、雨量が既に 0.8 超なのに④の嵐がまだ
    ///    **Emerging**（Active ではない）である窓と、④が終わったあと
    ///    <c>m_currentRain</c> が <c>0.0002/step</c> で 0.8 を下り切るまでの窓だけ。
    ///    どちらも <c>CreateDisaster</c> の戻り値をゲーム自身が見ているので
    ///    スロットは壊れない。**これは受け入れる** —— 台風の前後に雷雨が出るのは
    ///    現象として正しく、抑えるにはバニラへパッチを当てるしかない。
    /// 3. 落着点は台風の中心ではなく**マップ一様の乱数点**である
    ///    （1 引数版 IL_000F–004D、<c>Randomizer.Int32(-8640, 8640)</c>）。
    ///    ④の落雷（T6）とは分布が違うので、遠方に落ちる雷は④のものではない。
    /// 4. **T6（落雷）への申し送り。** 環境落雷の条件は
    ///    <c>m_lightningQueue.m_size == 0</c> である。T6 が常に 1 発以上キューへ
    ///    積んでいれば環境落雷は**完全に止まる**ので、20 発の上限に対して
    ///    環境落雷ぶんの取り分を見積もる必要は無い。逆に T6 が発数を 0 に絞る
    ///    位相（眼の中など）を作ると、そこだけ環境落雷が復活する。
    ///
    /// ── 7. 戻し方（<see cref="Release"/>） ───────────────────────────
    ///
    /// ④が上書きするのは 4 つで、戻り方は 2 種類ある:
    ///
    /// | 上書きするもの | 戻し方 |
    /// |---|---|
    /// | <c>m_targetRain</c> / <c>m_targetCloud</c> | **明示的に 0 を書く。** |
    /// | <c>m_targetFog</c> | 書くのをやめる（④が入れる値は 0 なので既に無害） |
    /// | <c>m_forceWeatherOn</c> | 書くのをやめる。<c>0.001/step</c> で自然に切れる |
    /// | <c>m_targetDirection</c> | 書くのをやめる。到達した瞬間にバニラが再抽選する（§A-4） |
    ///
    /// <c>ThunderStormAI.DeactivateDisaster</c> は <c>SelfTrigger</c> 付きなら
    /// <c>m_targetRain = 0; m_targetCloud = 0</c> を書く（§A-1、本タスクで IL 再確認）ので
    /// 本来は「書くのをやめる」だけでよい。**だがそれに頼れない** ——
    /// <c>DisasterAI.DeactivateNow</c> は <c>m_flags &amp; Active(8)</c> が無ければ
    /// 何もしないので（本タスクで IL 実測）、Emerging 中に止めた台風では空振りする。
    /// だから <see cref="Release"/> でも同じ 2 つを 0 にする。二重に書いても害は無い。
    ///
    /// <c>m_forceWeatherOn</c> に 0 を書かないのは意図である。バニラの
    /// <c>DeactivateDisaster</c> も触っていないし、天候 OFF のプレイヤーの環境で
    /// ここに 0 を書くと「台風が去って雨が引いていく」ではなく
    /// 「台風が去った瞬間に雨が消える」になる。
    ///
    /// **残る 1 つの穴（T8 の担当）。** <c>m_targetRain</c> はセーブに焼き付く
    /// （<c>WeatherManager+Data</c>）。台風の最中にセーブして終了すると、
    /// 次のロードで <c>m_targetRain</c> が高いまま復元される。時間はかかるが
    /// バニラの再抽選で必ず戻るので固まりはしない。保存時に戻す経路は
    /// <c>DisasterPlusSerialization</c> を触る T8 と同じ場所なので、そちらに任せる。
    /// </summary>
    public static class TyphoonWeather
    {
        /// <summary>
        /// <c>m_forceWeatherOn</c> に書く値。バニラの嵐・竜巻と同じ 2f（§A-1）。
        /// <c>0.001/step</c> 減衰なので 2000 step ぶんの猶予がある。
        /// </summary>
        private const float ForceWeatherOn = 2f;

        private const float RainBase = 0.35f;
        private const float RainRange = 0.65f;
        private const float CloudBase = 0.55f;
        private const float CloudRange = 0.45f;

        /// <summary>台風に霧は出さない（バニラの嵐と同じ。§A-1）。</summary>
        private const float TargetFog = 0f;

        private static bool _driving;
        private static float _lastRain;
        private static float _lastCloud;
        private static float _lastFog;
        private static float _lastDirectionDegrees;
        private static bool _weatherDisabledByPlayer;
        private static bool _errorLogged;

        /// <summary>④が今この tick で天候を書いているか。</summary>
        public static bool Driving { get { return _driving; } }

        /// <summary>直近に書いた <c>m_targetRain</c>。**これは目標値で、実測の降雨量ではない。**</summary>
        public static float LastRain { get { return _lastRain; } }

        public static float LastCloud { get { return _lastCloud; } }

        public static float LastFog { get { return _lastFog; } }

        public static float LastDirectionDegrees { get { return _lastDirectionDegrees; } }

        /// <summary>
        /// プレイヤーが設定で天候を切っているか（<c>m_enableWeather == false</c>）。
        /// **不具合ではなく正当な設定。** ④はその環境でも
        /// <c>m_forceWeatherOn</c> で嵐を見せるが、黙ってやらずに診断へ note を出す。
        /// </summary>
        public static bool WeatherDisabledByPlayer { get { return _weatherDisabledByPlayer; } }

        /// <summary>
        /// sim スレッド。<c>TyphoonFeature.OnSimulationTick</c> のポーズガードより下、
        /// <c>TyphoonController.Tick</c> の**直後**に、台風が動いているときだけ呼ぶ。
        ///
        /// <paramref name="deltaMinutes"/> は使わない。ここが書くのは
        /// **今の幾何から決まる目標値**であって時間で積算する量ではなく、
        /// 目標へ寄せる速度はゲーム側が <c>0.0002/step</c>（雲は <c>0.0008</c>）で
        /// 決めている（§A-4）。引数に残してあるのは、他の要素（T6〜T10）と
        /// 呼び出しの形をそろえるためである。
        ///
        /// <paramref name="snapshot"/> に載っている台風の状態は**前 tick のもの**なので
        /// 使わない（<see cref="TyphoonSnapshot"/> の T3 節の注記）。座標も半径も
        /// <see cref="TyphoonController"/> の static から同じスレッドで直接読む。
        /// </summary>
        public static void Drive(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon weather driving failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWeather",
                             "typhoon weather driving failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step()
        {
            // ★ exists を先に見る。Singleton<T>.instance は sInstance が null のとき
            //    FindObjectOfType と new GameObject を走らせる main スレッド専用 API。
            if (!Singleton<WeatherManager>.exists)
            {
                _driving = false;
                return;
            }

            var w = Singleton<WeatherManager>.instance;

            float near = NearnessOf(TyphoonController.Centre, TyphoonController.GaleRadius);
            float rain = Clamp01(RainBase + RainRange * near);
            float cloud = Clamp01(CloudBase + CloudRange * near);
            float direction = WindDegreesOf(TyphoonController.HeadingRadians);

            // ★ 毎 tick 書く（クラス doc 1.）。
            w.m_targetRain = rain;
            w.m_targetCloud = cloud;
            w.m_targetFog = TargetFog;

            // ★ これが無いと天候 OFF の環境で全部 0 に潰される（クラス doc 2.）。
            w.m_forceWeatherOn = ForceWeatherOn;

            // 風向は到達した瞬間に再抽選されるので、これも毎 tick 書き続ける（§A-4）。
            w.m_targetDirection = direction;

            _driving = true;
            _lastRain = rain;
            _lastCloud = cloud;
            _lastFog = TargetFog;
            _lastDirectionDegrees = direction;
            _weatherDisabledByPlayer = !w.m_enableWeather;
        }

        /// <summary>
        /// 都市（マップ原点）から見た台風の近さ [0, 1]。強風域の縁で 0、中心で 1。
        /// 半径が読めていなければ 0（＝いちばん弱い雨。推測した半径で強めない）。
        /// </summary>
        private static float NearnessOf(Vec3 centre, float galeRadius)
        {
            if (!(galeRadius > 0f)) return 0f;   // NaN もここで落ちる

            float distance = (float)System.Math.Sqrt(centre.X * centre.X + centre.Z * centre.Z);
            if (float.IsNaN(distance)) return 0f;

            float near = 1f - distance / galeRadius;
            return Clamp01(near);
        }

        /// <summary>
        /// ④の進行方位（rad、<c>(cos φ, sin φ)</c> を (X, Z) とする数学系）を
        /// <c>m_targetDirection</c> の方位角（度、0 = +Z / 90 = +X）へ。
        /// 対応はクラス doc 4. のとおり <c>EndRenderingImpl</c> の IL から取ってある。
        /// </summary>
        private static float WindDegreesOf(float headingRadians)
        {
            if (float.IsNaN(headingRadians)) return 0f;

            float degrees = 90f - headingRadians * 57.29578f;
            degrees = degrees % 360f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// ④が握っていた天候を手放す。戻し方の内訳はクラス doc 7. の表。
        ///
        /// 呼ばれるのは 2 箇所で、**台風を手放すあらゆる経路がこの 2 つのどちらかを通る**:
        ///
        /// - <c>TyphoonController.Forget</c> —— 通常の終了（<c>Stop</c>）だけでなく、
        ///   **災害スロットを奪われたとき**（<c>LoseSlot</c>）も通る。復元を
        ///   <c>Stop</c> 側に置くと、スロットを奪われた瞬間に天候を握ったまま
        ///   台風だけが消える
        /// - <see cref="Reset"/> —— レベルアンロード
        ///
        /// 冪等である（<c>_driving</c> が false なら即 return）ので重ねて呼んでよい。
        ///
        /// 駆動していなければ何もしない —— 台風を 1 度も起こしていない都市で
        /// プレイヤーの雨を勝手に 0 にしない。
        /// </summary>
        public static void Release()
        {
            if (!_driving) return;
            _driving = false;

            try
            {
                if (Singleton<WeatherManager>.exists)
                {
                    var w = Singleton<WeatherManager>.instance;
                    w.m_targetRain = 0f;
                    w.m_targetCloud = 0f;
                    // ★ m_forceWeatherOn と m_targetDirection には書かない（クラス doc 7.）。
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon weather release failed", e);
                }
            }

            _lastRain = 0f;
            _lastCloud = 0f;
            _lastFog = 0f;
            _lastDirectionDegrees = 0f;
        }

        /// <summary>
        /// **レベルアンロードで必ず呼ぶ。** アンロード中は sim スレッドが既に
        /// 停止しているので、ここから <c>WeatherManager</c> を直接触ってよい
        /// （<c>DisasterPlusLoading.OnLevelUnloading</c> の同じ注記）。
        /// </summary>
        public static void Reset()
        {
            Release();
            _weatherDisabledByPlayer = false;
            // ★ _errorLogged は戻さない（ゲームのビルドに対する事実であって
            //    都市ごとの状態ではない。TyphoonReader と同じ扱い）。
        }
    }
}
