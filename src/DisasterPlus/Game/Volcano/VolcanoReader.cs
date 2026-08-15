using System;
using System.Reflection;
using ColossalFramework;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤が読む唯一の場所。地形 API の到達経路を解決し、
    /// <see cref="VolcanoSnapshot"/> にして publish する。
    ///
    /// **<see cref="Read"/> は sim スレッドから呼ぶこと。**
    /// <c>TerrainManager</c> / <c>SimulationManager</c> / <c>ToolManager</c> は
    /// いずれもシミュレーションが所有する。main スレッドから書き換え系を触ると、
    /// スタックトレースの出ない <c>IndexOutOfRangeException</c> ポップアップが
    /// 後になってバニラ側から出る（この MOD の try/catch では捕まえられない）。
    /// 形は②の <c>EarthquakeReader</c>・④の <see cref="TyphoonReader"/> をそのまま手本にしている。
    ///
    /// **読み取りだけはどちらのスレッドからでも安全である**（既存の
    /// <c>TerrainHeightSampler</c> の doc がそう名乗っている）。だから
    /// <see cref="ScanTerrainFacts"/> は main スレッドの <see cref="Assumptions"/> からも呼べる。
    ///
    /// <c>Singleton&lt;T&gt;.exists</c> を**必ず先に見る**。<c>Singleton&lt;T&gt;.instance</c> は
    /// <c>sInstance</c> が null のとき <c>FindObjectOfType</c> と <c>new GameObject</c> を
    /// 走らせる **main スレッド専用 API** で、sim スレッドから踏むと落ちる
    /// （<c>LongPeriodDamage.Sweep</c> / <c>TyphoonWind.Sweep</c> の同じ注記）。
    ///
    /// > **⑤は災害まわりの型に一切触らない。** 設計書 §2 が「災害スロットに
    /// > 載せない」と決めており、事実文書 §D-11 が「ND 無しではバニラの災害プレハブが
    /// > 1 つも無く、災害の検出 API は <c>m_DisasterWrapper</c> が null で NRE になる」と
    /// > 確定させている（罠 6）。担保は grep なので、**その API 名を doc にも書かない**
    /// > （<see cref="VolcanoFeature"/> のクラス doc）。
    /// </summary>
    public static class VolcanoReader
    {
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

        private static VolcanoTerrainFacts _facts;
        private static bool _factsScanned;

        /// <summary>
        /// レベルのロード／アンロードで呼ぶ。都市をまたいで地形キャッシュを持ち越さない
        /// （「全セッション状態はレベルアンロードでリセットする」がこの MOD の規則）。
        /// <c>RawHeights</c> の配列は都市ごとに作り直されるので、**長さの実測を
        /// 持ち越すと 2 つ目の都市で前の都市の事実を名乗ることになる。**
        /// </summary>
        public static void Reset()
        {
            _facts = new VolcanoTerrainFacts();
            _factsScanned = false;
        }

        /// <summary>**sim スレッド専用。**</summary>
        public static VolcanoSnapshot Read()
        {
            try
            {
                if (!SimulationManager.exists) return VolcanoSnapshot.Invalid();

                uint frame = SimulationManager.instance.m_currentFrameIndex;

                // ★ 位相と調査結果は sim スレッドの VolcanoState から直接読む。
                //   この Read はポーズガードより上で走るので、載るのは
                //   **この tick で VolcanoState.Tick が走る前の状態**である
                //   （VolcanoSnapshot.Phase の doc）。
                // 準備の実績も sim スレッドの static から直接読む（同じスレッド）。
                return new VolcanoSnapshot(true, ResolveTerrainFacts(), frame, ReadGameMode(),
                                           VolcanoState.Phase, VolcanoState.Footprint,
                                           VolcanoState.ProgressUnit, VolcanoState.LastRefusal,
                                           VolcanoState.SettingsChanged,
                                           VolcanoClearing.ClearedRadiusMetres,
                                           VolcanoClearing.Complete,
                                           VolcanoClearing.TotalBuildingsDestroyed,
                                           VolcanoClearing.TotalSegmentsDestroyed,
                                           VolcanoClearing.LastBuildingsRefused,
                                           VolcanoClearing.LastCapped,
                                           VolcanoClearing.RoadPathAvailable,
                                           VolcanoUplift.SummitMetres,
                                           VolcanoUplift.ActiveRadiusMetres,
                                           VolcanoUplift.Complete,
                                           VolcanoUplift.CraterCarved,
                                           VolcanoUplift.TileCount,
                                           VolcanoUplift.TileCursor,
                                           VolcanoEruption.Active,
                                           VolcanoEruption.IntensityUnit,
                                           VolcanoEruption.SummitWorld,
                                           VolcanoLava.FlowCount,
                                           VolcanoLava.AliveCount,
                                           VolcanoLava.LongestMetres,
                                           VolcanoLava.BuildingsIgnited,
                                           VolcanoLava.TreesIgnited,
                                           VolcanoLava.TreesAvailable,
                                           // ★ publish 後に書き換えられない配列である
                                           //   （VolcanoLava は前進のたびに丸ごと差し替える）。
                                           //   コピーを取らないのはそのためで、
                                           //   main スレッドが参照を持ったままでも安全。
                                           VolcanoLava.TrailPoints,
                                           VolcanoLava.TrailPointCounts,
                                           VolcanoLava.CoolUnit);
            }
            catch (Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("volcano read failed", e);
                }
                else
                {
                    Log.Diag("VolcRead", "volcano read failed: " + e.GetType().Name);
                }
                return VolcanoSnapshot.Invalid();
            }
        }

        /// <summary>
        /// ゲームモードか（マップエディタなら false）。
        ///
        /// <c>m_blockHeights</c> の追随速度が変わる（ゲームで上へ 2 m、エディタで 8 m、§A-2）。
        /// **読めなければ true（ゲームモード）を返す** —— 遅いほうを名乗るのが安全側で、
        /// 「もう建てられます」と早まって言わない。
        ///
        /// ★ **計画 §2.3 の記述を 1 箇所訂正する。** 計画は
        /// <c>ToolController.m_mode</c> と書いているが、<c>m_mode</c> は
        /// <c>ToolController</c> の **public インスタンスフィールド**（型は
        /// <c>ItemClass.Availability</c>）であって static ではない。
        /// 到達経路は <c>ToolManager.instance.m_properties.m_mode</c> である
        /// （本 MOD 側でリフレクションにより確認済み）。
        /// </summary>
        private static bool ReadGameMode()
        {
            try
            {
                if (!Singleton<ToolManager>.exists) return true;

                var properties = Singleton<ToolManager>.instance.m_properties;
                if (properties == null) return true;

                return (properties.m_mode & ItemClass.Availability.Game) != ItemClass.Availability.None;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 地形の事実をキャッシュ越しに返す。**sim スレッド専用**（<c>_factsScanned</c> を書く）。
        ///
        /// ④の <c>ResolvePrefabFacts</c> と違って**間引きつきの再走査をしない。**
        /// あちらが見ているのはプレハブで、レベルロード直後にはまだ揃っていない
        /// ことがあった。こちらが見ているのは <c>TerrainManager.Awake</c> が確保した
        /// 配列とアセンブリのメソッド表で、**レベルがロードされた時点で確定している**。
        /// 毎 tick 走査しないのは純粋にコストの都合である。
        /// </summary>
        private static VolcanoTerrainFacts ResolveTerrainFacts()
        {
            if (_factsScanned) return _facts;

            _factsScanned = true;
            _facts = ScanTerrainFacts();

            if (!_facts.Usable)
            {
                // Warn はスロットルされないので Diag に落とす。
                Log.Diag("VolcTerrain",
                    "the terrain write path is not usable: RawHeights=" + _facts.RawArrayLength
                    + " updateArea=" + (_facts.UpdateAreaResolved ? "ok" : "MISSING")
                    + "; no volcano will be built");
            }
            return _facts;
        }

        /// <summary>
        /// 地形 API を走査するだけの純粋関数。キャッシュを一切触らないので、
        /// **どのスレッドから呼んでもこのクラスの状態を壊さない**。
        /// <see cref="Assumptions"/>（main スレッド）はこちらを使うこと
        /// —— <see cref="ResolveTerrainFacts"/> を呼ぶと、sim スレッドが回している
        /// キャッシュを main スレッドから巻き戻すことになる
        /// （<c>FireWhirlSpawner.HasTornadoPrefab</c> で同じ欠陥を直した経緯がある）。
        ///
        /// **項目ごとに別々の try で囲む。** 片方の失敗でもう片方まで諦めると、
        /// 「溶岩が流れないだけ」の環境で山まで止まる。
        ///
        /// メソッドは <c>GetMethod</c> で**引数の型まで指定**して見る。名前だけの
        /// <c>GetMethod</c> はオーバーロードで <c>AmbiguousMatchException</c> を投げるうえ、
        /// シグネチャ変更を見逃す（②が確立した形）。
        /// <c>SampleDetailHeight</c> は同名 4 本のオーバーロードがあるので特にそうである。
        /// </summary>
        public static VolcanoTerrainFacts ScanTerrainFacts()
        {
            bool heightsResolved = false;
            int rawLength = 0;

            try
            {
                // ★ exists を先に見る（クラス doc）。
                if (Singleton<TerrainManager>.exists)
                {
                    // 読み取りなのでどちらのスレッドからでも安全。
                    // RawHeights は public プロパティで、型は ushort[]
                    //（コンパイル時に検証される。§C-8 / §B-6）。
                    ushort[] raw = Singleton<TerrainManager>.instance.RawHeights;
                    if (raw != null)
                    {
                        heightsResolved = true;
                        rawLength = raw.Length;
                    }
                }
            }
            catch
            {
                heightsResolved = false;
                rawLength = 0;
            }

            bool updateAreaResolved = HasMethod(typeof(TerrainModify), "UpdateArea", true,
                new Type[]
                {
                    typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(bool), typeof(bool), typeof(bool)
                });

            bool craterResolved = HasMethod(typeof(DisasterHelpers), "MakeCrater", true,
                new Type[]
                {
                    typeof(UnityEngine.Vector2), typeof(float), typeof(float), typeof(bool)
                });

            bool burnGroundResolved = HasMethod(typeof(DisasterHelpers), "BurnGround", true,
                new Type[]
                {
                    typeof(UnityEngine.Vector2), typeof(float), typeof(float)
                });

            // ★ 3 引数版（out float slopeX, out float slopeZ）。1 引数版では勾配が取れず、
            //   溶岩は下り方向を見つけられない（§B-6）。out は MakeByRefType で指定する。
            bool slopeSampleResolved = HasMethod(typeof(TerrainManager), "SampleDetailHeight", false,
                new Type[]
                {
                    typeof(UnityEngine.Vector3),
                    typeof(float).MakeByRefType(), typeof(float).MakeByRefType()
                });

            bool dlc;
            try
            {
                dlc = ModCompat.NaturalDisastersOwned;
            }
            catch
            {
                dlc = false;
            }

            return new VolcanoTerrainFacts(heightsResolved, rawLength, updateAreaResolved,
                                           craterResolved, burnGroundResolved,
                                           slopeSampleResolved, dlc);
        }

        /// <summary>
        /// 引数の型まで指定した <c>GetMethod</c>。見つからない・例外が出たときは false。
        /// **「解決できなかった」を「例外」にしない** —— <see cref="ScanTerrainFacts"/> は
        /// <see cref="Assumptions"/> の <c>Check</c> の外側からも呼ばれうる。
        /// </summary>
        private static bool HasMethod(Type declaringType, string name, bool isStatic,
                                      Type[] parameterTypes)
        {
            try
            {
                var flags = BindingFlags.Public
                            | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
                return declaringType.GetMethod(name, flags, null, parameterTypes, null) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
