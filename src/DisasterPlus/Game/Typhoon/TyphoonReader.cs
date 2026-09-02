using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ④が読む唯一の場所。プレハブ 6 値（<see cref="TyphoonPrefabFacts"/>）と
    /// <c>WeatherManager</c> の実測値を集めて <see cref="TyphoonSnapshot"/> にする。
    ///
    /// **sim スレッドから呼ぶこと。** <c>DisasterManager</c> / <c>WeatherManager</c> /
    /// <c>SimulationManager</c> はいずれもシミュレーションが所有する。main スレッドから
    /// 直接触ると、スタックトレースの出ない <c>IndexOutOfRangeException</c> ポップアップが
    /// 後になってバニラ側から出る（この MOD の try/catch では捕まえられない）。
    /// 形は②の <see cref="EarthquakeReader"/> をそのまま手本にしている。
    ///
    /// <c>Singleton&lt;T&gt;.exists</c> を**必ず先に見る**。<c>Singleton&lt;T&gt;.instance</c> は
    /// <c>sInstance</c> が null のとき <c>FindObjectOfType</c> と <c>new GameObject</c> を
    /// 走らせる **main スレッド専用 API** で、sim スレッドから踏むと落ちる
    /// （<c>LongPeriodDamage.Sweep</c> / <c>TsunamiChain</c> の同じ注記）。
    ///
    /// ★ **設計書の記述を 1 箇所だけ訂正する。**
    /// 設計書 §6 と付録は「<c>VortexAI</c> の <c>m_maxSpeed</c>」と書いているが、
    /// <c>VortexAI</c> に <c>m_maxSpeed</c> というフィールドは**存在しない**。
    /// IL 事実文書 §B-1 の <c>IL_00AF maxSpeed = m_info.m_maxSpeed</c> が読んでいるのは
    /// <c>VehicleAI.m_info</c>、すなわち **<c>VehicleInfo.m_maxSpeed</c>** である。
    /// 本 MOD 側でもリフレクションで再確認済み（<c>VortexAI</c> の宣言フィールドは
    /// <c>m_destructionRadiusMin</c> / <c>m_destructionRadiusMax</c> /
    /// <c>m_upgradeRadiusMin</c> / <c>m_upgradeRadiusMax</c> / <c>m_debrisCount</c> の 5 つだけ）。
    /// 到達経路は <c>TornadoAI.m_vortexInfo</c>（<c>VehicleInfo</c>）<c>.m_maxSpeed</c>。
    /// **<c>VortexAI</c> に <c>m_maxSpeed</c> を探しに行かないこと** —— 見つからず、
    /// 推測で別のフィールドを掴むことになる。
    /// </summary>
    public static class TyphoonReader
    {
        /// <summary>
        /// 直近のプレハブ走査が失敗してから何回呼ばれたか。<see cref="Read"/> は毎 sim tick
        /// 呼ばれるので、失敗を毎回リトライすると全 prefab 走査が毎 tick 走る。
        /// <c>EarthquakeReader._missCallCount</c> と同じ間引き。
        /// </summary>
        private static int _missCallCount;

        /// <summary>失敗キャッシュを効かせる呼び出し回数。0 にはしない（＝毎回リトライになる）。</summary>
        private const int MissRetryCalls = 64;

        private static TyphoonPrefabFacts _prefab;
        private static bool _prefabSearched;

        /// <summary>
        /// <see cref="Read"/> 内の想定外例外を <c>Log.Error</c> で鳴らしたか。
        ///
        /// <c>Log.Warn</c> / <c>Log.Error</c> はスロットルされない。<see cref="Read"/> は
        /// sim tick ごと（通常速度でおよそ 50 回/秒）に呼ばれるので、恒常的に投げる状態に
        /// なると毎秒 50 行を output_log.txt に書き続けてログを使い物にならなくする。
        /// 1 回目だけ確実に目立たせ、以後は <c>Log.Diag</c> のキー単位スロットル
        /// （512 sim フレームに 1 回）へ落とす。
        ///
        /// **レベルアンロードでリセットしない。** 「投げる」はこの DLL が参照している
        /// ゲームのビルドに対する事実であって、都市ごとの状態ではない。
        /// </summary>
        private static bool _readErrorLogged;

        /// <summary>
        /// レベルのロード／アンロードで呼ぶ。都市をまたいでプレハブキャッシュを持ち越さない
        /// （「全セッション状態はレベルアンロードでリセットする」がこの MOD の規則）。
        /// </summary>
        public static void Reset()
        {
            _prefab = new TyphoonPrefabFacts();
            _prefabSearched = false;
            _missCallCount = 0;
        }

        /// <summary>**sim スレッド専用。**</summary>
        public static TyphoonSnapshot Read()
        {
            try
            {
                if (!SimulationManager.exists) return TyphoonSnapshot.Invalid();

                var prefab = ResolvePrefabFacts();
                uint frame = SimulationManager.instance.m_currentFrameIndex;

                float rain = 0f, cloud = 0f, fog = 0f, windDirection = 0f;
                bool weatherEnabled = false;
                bool weatherReadable = ReadWeather(out rain, out cloud, out fog,
                                                   out windDirection, out weatherEnabled);

                // ★ TyphoonController は sim スレッドの static で、この Read() と
                //    同じスレッドから読んでいる（TyphoonFeature.OnSimulationTick）。
                //    載るのは **TyphoonController.Tick が走る前**＝前 tick の状態である
                //    （TyphoonSnapshot の T3 節の注記）。
                return new TyphoonSnapshot(true, prefab, frame,
                                           rain, cloud, fog, windDirection,
                                           weatherEnabled, weatherReadable,
                                           TyphoonController.Active,
                                           TyphoonController.DisasterId,
                                           TyphoonController.Centre,
                                           TyphoonController.HeadingRadians,
                                           TyphoonController.Intensity,
                                           TyphoonController.StormRadius,
                                           TyphoonController.GaleRadius,
                                           TyphoonController.TrackPlan,
                                           TyphoonController.Phase,
                                           TyphoonController.ElapsedFrames,
                                           TyphoonController.TotalFrames,
                                           TyphoonController.OverLand,
                                           TyphoonController.LandfallKnown,
                                           TyphoonController.MinutesToLandfall,
                                           TyphoonController.LastRefusal,
                                           TyphoonWeather.Driving,
                                           TyphoonWeather.LastRain,
                                           TyphoonWeather.LastCloud,
                                           TyphoonWeather.LastDirectionDegrees,
                                           TyphoonLightning.InFlight,
                                           TyphoonLightning.TotalQueued,
                                           TyphoonLightning.TotalRejected,
                                           TyphoonLightning.LastVanillaReserve,
                                           TyphoonWind.Passes,
                                           TyphoonWind.LastCollapsed,
                                           TyphoonWind.TotalCollapsed,
                                           TyphoonWind.LastScanned,
                                           TyphoonWind.LastRefused,
                                           TyphoonWind.LastCapped,
                                           TyphoonWind.LastUnknownHeight,
                                           TyphoonFlood.State,
                                           TyphoonFlood.NaturalSourceCount,
                                           TyphoonFlood.TouchedCount,
                                           TyphoonFlood.LastPeakRiseMetres,
                                           TyphoonGust.LastActive,
                                           TyphoonGust.LastCollapsed,
                                           TyphoonGust.TotalCollapsed,
                                           TyphoonGust.LastRefused);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("typhoon read failed", e);
                }
                else
                {
                    Log.Diag("TyRead", "typhoon read failed: " + e.GetType().Name);
                }
                return TyphoonSnapshot.Invalid();
            }
        }

        /// <summary>
        /// 天候の実測値。**④で <c>[measured]</c> を名乗ってよい唯一の出所**（設計書 §7-1）。
        ///
        /// 読めなければ false を返し、呼び出し側は数値を出さない。
        /// **0 と「読めなかった」を混ぜない**（①②が繰り返し確立した規律）。
        /// </summary>
        private static bool ReadWeather(out float rain, out float cloud, out float fog,
                                        out float windDirection, out bool weatherEnabled)
        {
            rain = 0f;
            cloud = 0f;
            fog = 0f;
            windDirection = 0f;
            weatherEnabled = false;

            try
            {
                // ★ exists を先に見る（クラス doc）。
                if (!Singleton<WeatherManager>.exists) return false;

                var w = Singleton<WeatherManager>.instance;
                rain = w.m_currentRain;
                cloud = w.m_currentCloud;
                fog = w.m_currentFog;
                windDirection = w.m_windDirection;
                weatherEnabled = w.m_enableWeather;
                return true;
            }
            catch
            {
                rain = 0f;
                cloud = 0f;
                fog = 0f;
                windDirection = 0f;
                weatherEnabled = false;
                return false;
            }
        }

        /// <summary>
        /// プレハブ 6 値をキャッシュ越しに返す。**sim スレッド専用**
        /// （<c>_prefabSearched</c> / <c>_missCallCount</c> を書き換える）。
        ///
        /// 解決するまでは間引きつきで再走査する。DLC 非所持環境では
        /// 永久に解決しないので、そこでは 64 呼び出しに 1 回の走査で落ち着く。
        /// </summary>
        private static TyphoonPrefabFacts ResolvePrefabFacts()
        {
            if (_prefabSearched && _prefab.StormResolved) return _prefab;

            // 直前の走査が失敗している場合は、レベルロード直後で prefab がまだ
            // 揃っていないだけの可能性がある。「二度と探さない」にはせず、
            // かといって毎 tick 全 prefab を舐めもしない。呼び出し回数で間引く。
            if (_prefabSearched)
            {
                _missCallCount++;
                if (_missCallCount < MissRetryCalls) return _prefab;
            }
            _missCallCount = 0;

            _prefabSearched = true;
            _prefab = ScanPrefabFacts();

            if (!_prefab.StormResolved)
            {
                // Warn はスロットルされないので Diag に落とす。DLC 非所持環境では
                // これが恒常的な正常状態になる。
                Log.Diag("TyPrefab",
                    "no ThunderStormAI DisasterInfo found; Natural Disasters DLC required for typhoons");
            }
            return _prefab;
        }

        /// <summary>
        /// プレハブを走査するだけの純粋関数。キャッシュもミス回数も一切触らないので、
        /// **どのスレッドから呼んでもこのクラスの状態を壊さない**。
        /// <see cref="Assumptions"/>（main スレッド）はこちらを使うこと
        /// —— <see cref="ResolvePrefabFacts"/> を呼ぶと、sim スレッドが回している
        /// キャッシュを main スレッドから巻き戻すことになる
        /// （<c>FireWhirlSpawner.HasTornadoPrefab</c> で同じ欠陥を直した経緯がある）。
        ///
        /// <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> は public static generic で、
        /// <c>PrefabCollection&lt;DisasterInfo&gt;</c> を走査して <c>m_disasterAI is T</c> の
        /// 最初のプレハブを返すだけ。DLC 判定は中に無く、**DLC が無ければプレハブ自体が
        /// 存在せず null が返る**のが権威。
        ///
        /// </summary>
        public static TyphoonPrefabFacts ScanPrefabFacts()
        {
            bool stormResolved = false;
            float stormRadius = 0f;
            uint emerging = 0u;
            uint active = 0u;

            try
            {
                var info = DisasterManager.FindDisasterInfo<ThunderStormAI>();
                var ai = info == null ? null : info.m_disasterAI as ThunderStormAI;
                if (ai != null)
                {
                    stormResolved = true;
                    stormRadius = ai.m_radius;
                    emerging = ai.m_emergingDuration;
                    active = ai.m_activeDuration;
                }
            }
            catch
            {
                stormResolved = false;
                stormRadius = 0f;
                emerging = 0u;
                active = 0u;
            }

            // ★ **竜巻プレハブはもう読まない。** 随伴竜巻が退役し、竜巻並みの被害は
            //   TyphoonGust が自前で出すようになったので、VortexAI の破壊半径も
            //   VehicleInfo.m_maxSpeed も使う場所が 1 つも無い。読める値だからといって
            //   診断に並べ続けると、次の担当者が「これは効いている」と読む。

            return new TyphoonPrefabFacts(stormResolved, stormRadius, emerging, active);
        }
    }
}
