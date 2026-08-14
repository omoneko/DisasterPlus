using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 進行中の地震・地震計カバレッジ・sim スレッドの時刻を読む。
    ///
    /// **sim スレッドから呼ぶこと。** <c>DisasterManager</c> /
    /// <c>ImmaterialResourceManager</c> / <c>SimulationManager</c> はいずれも
    /// シミュレーションが所有する。main スレッドから直接触ると、スタックトレースの
    /// 出ない <c>IndexOutOfRangeException</c> ポップアップが後になってバニラ側から出る
    /// （この MOD の try/catch では捕まえられない）。走査の形は①の
    /// <c>WeatherReader.CountLocatedStorms</c> をそのまま手本にしている。
    ///
    /// **時刻の読み方（§F-1 の罠）。** <c>SimulationManager.m_currentDayTimeHour</c> は
    /// **メインスレッドが** <c>m_referenceFrameIndex</c>（描画補間側）から書くフィールドで、
    /// sim スレッドから読むとスレッド境界を跨ぐ上に、そもそも別の量を読むことになる。
    /// sim スレッドの正解は <c>m_dayTimeFrame * DAYTIME_FRAME_TO_HOUR</c> の 1 つだけ。
    /// このファイルに <c>m_currentDayTimeHour</c> が現れたら、それは欠陥である。
    /// </summary>
    public static class EarthquakeReader
    {
        /// <summary>
        /// 直近のプレハブ走査が失敗してから何回呼ばれたか。<see cref="Read"/> は毎 sim tick
        /// 呼ばれるので、失敗を毎回リトライすると全 prefab 走査が毎 tick 走る。
        /// <c>FireWhirlSpawner._missCallCount</c> と同じ間引き。
        /// </summary>
        private static int _missCallCount;

        /// <summary>失敗キャッシュを効かせる呼び出し回数。0 にはしない（＝毎回リトライになる）。</summary>
        private const int MissRetryCalls = 64;

        private static EarthquakePrefabFacts _prefab;
        private static bool _prefabSearched;

        /// <summary>
        /// <see cref="Read"/> 内の想定外例外を <c>Log.Error</c> で鳴らしたか。
        ///
        /// <c>Log.Warn</c> / <c>Log.Error</c> はスロットルされない。Read() は sim tick ごと
        /// （通常速度でおよそ 50 回/秒）に呼ばれるので、恒常的に投げる状態になると
        /// 毎秒 50 行を output_log.txt に書き続けてログを使い物にならなくする。
        /// 1 回目だけ確実に目立たせ、以後は <c>Log.Diag</c> のキー単位スロットル
        /// （512 sim フレームに 1 回）へ落とす（<c>WeatherReader._readErrorLogged</c> /
        /// <c>HazardMapReader._sampleErrorLogged</c> が確立した形）。
        ///
        /// **レベルアンロードでリセットしない。** 「投げる」はこの DLL が参照している
        /// ゲームのビルドに対する事実であって、都市ごとの状態ではない。
        /// </summary>
        private static bool _readErrorLogged;

        /// <summary>
        /// レベルのロード／アンロードで呼ぶ。都市をまたいでプレハブキャッシュを持ち越さない。
        /// （2 つ目の都市が DLC 構成の違う環境で開かれる可能性は低いが、
        ///  「全セッション状態はレベルアンロードでリセットする」がこの MOD の規則）。
        /// </summary>
        public static void Reset()
        {
            _prefab = new EarthquakePrefabFacts();
            _prefabSearched = false;
            _missCallCount = 0;
        }

        /// <summary>**sim スレッド専用。**</summary>
        public static EarthquakeSnapshot Read()
        {
            try
            {
                if (!Singleton<DisasterManager>.exists) return EarthquakeSnapshot.Invalid();
                if (!SimulationManager.exists) return EarthquakeSnapshot.Invalid();

                var prefab = ResolvePrefabFacts();
                var quakes = CollectQuakes(Singleton<DisasterManager>.instance, prefab);

                var sim = SimulationManager.instance;

                // ★ m_currentDayTimeHour は読まない（§F-1）。これが sim スレッドの唯一の正解。
                float hour = sim.m_dayTimeFrame * SimulationManager.DAYTIME_FRAME_TO_HOUR;

                // 日夜サイクル OFF だと hour は永久に 12.0 に固定される。ここでは黙って通し、
                // その事実をスナップショットに載せて表示側に判断させる
                // （EarthquakeSnapshot.DayNightEnabled の doc 参照）。
                bool dayNight = sim.m_enableDayNight;

                // ★ カーソルは 1 回だけ取る。建物の走査（地震があるときだけ）と
                //    カバレッジの読み取り（地震の有無に関わらず）で共有する。
                //    2 回 TakeCursor すると、その間に main スレッドが publish し直した
                //    別の座標について 2 つの値を作ることになる。
                Vec3 cursor;
                bool haveCursor = EarthquakeHub.TakeCursor(out cursor);

                ushort cursorQuakeId;
                BuildingProbeOutcome cursorProbe;
                float cursorHeight;
                var cursorBuilding = ProbeCursorBuilding(quakes, haveCursor, cursor,
                                                         out cursorQuakeId, out cursorProbe,
                                                         out cursorHeight);

                // カーソル地点のカバレッジは**地震が 1 個も無くても読む**。
                // 「ここに地震計は届いているか」は都市の性質であって、
                // 今地震が起きているかとは関係が無い（設計書 §3.4）。
                int cursorCoverage = 0;
                bool cursorCoverageValid = haveCursor
                    && TryReadCoverage(new Vector3(cursor.X, cursor.Y, cursor.Z),
                                       out cursorCoverage);

                // 波形は**前の tick までに貯まったもの**である。今 tick ぶんの
                // サンプリングは EarthquakeFeature がポーズガードより下で行うので、
                // ここで読めるのは常に 1 tick 前までの状態になる（BuildingProbe の
                // カーソル追従と同じ性質の、設計上の遅延）。
                //
                // ★ RecordingQuakeId も同じ 1 tick ぶん古い。新しい地震が選ばれる
                //    tick では、ここで載る WaveformQuakeId は**前の地震の ID**
                //    （あるいは 0）になる。Sample() がまだ走っていないためで、
                //    次の tick で自動的に揃う。Traces は同じ時点の値なので、
                //    ID と中身が食い違うことはない（両方が 1 tick 古い）。
                var traces = SeismographRecorder.Snapshot();

                // ★ 第 2 層（津波連鎖）の状態も sim スレッドのここで読む。TsunamiChain は
                //    同じスレッドの持ち物なので、これは単なるローカルな読み出しである
                //    （main スレッドへ渡す唯一の経路をスナップショットに一本化している）。
                //    Read() は TsunamiChain.Tick() より前に走るので、載るのは最大
                //    1 tick 前の状態になる（EarthquakeSnapshot.TsunamiState の doc）。
                return new EarthquakeSnapshot(quakes, prefab, sim.m_currentFrameIndex,
                                              hour, dayNight, cursorBuilding, cursorQuakeId,
                                              cursorProbe, cursorHeight,
                                              cursorCoverage, cursorCoverageValid,
                                              traces, SeismographRecorder.RecordingQuakeId,
                                              TsunamiChain.State, TsunamiChain.DueFrame,
                                              TsunamiChain.QuakeId, true);
            }
            catch (System.Exception e)
            {
                if (!_readErrorLogged)
                {
                    _readErrorLogged = true;
                    Log.Error("earthquake read failed", e);
                }
                else
                {
                    Log.Diag("EqRead", "earthquake read failed: " + e.GetType().Name);
                }
                return EarthquakeSnapshot.Invalid();
            }
        }

        /// <summary>
        /// 生きている地震を全部拾う。**sim スレッド専用。**
        ///
        /// <c>DisasterData.Info</c> は <c>PrefabCollection&lt;DisasterInfo&gt;.GetPrefab(m_infoIndex)</c>
        /// を呼ぶだけで境界検査をしない（IL 実測: <c>get_Info</c> は 4 命令）。壊れたセーブや
        /// MOD 由来の不正な <c>m_infoIndex</c> で投げうるので、**要素ごとに** try/catch で
        /// 囲んで 1 件の失敗が集計全体を落とさないようにする。
        /// </summary>
        private static IList<EarthquakeReading> CollectQuakes(DisasterManager d,
                                                             EarthquakePrefabFacts prefab)
        {
            var list = d.m_disasters;
            if (list == null) return EarthquakeSnapshot.EmptyQuakeList;

            var buffer = list.m_buffer;
            if (buffer == null) return EarthquakeSnapshot.EmptyQuakeList;

            // m_size を信用しきらない。バニラは m_size までしか回さないが、こちらは
            // 配列長でも頭を押さえておく（sim スレッドで IndexOutOfRange を出すと
            // スタックトレース無しのポップアップになる）。
            int size = list.m_size;
            if (size > buffer.Length) size = buffer.Length;

            // 地震はふつう 0 個なので、見つかるまでリストを確保しない。
            List<EarthquakeReading> found = null;

            for (int i = 0; i < size; i++)
            {
                int flags = (int)buffer[i].m_flags;
                if (!DisasterPhases.IsAlive(flags)) continue;

                DisasterAI ai;
                try
                {
                    var info = buffer[i].Info;
                    // UnityEngine.Object の == オーバーロードで破棄済み(fake-null)も弾く。
                    if (info == null) continue;
                    ai = info.m_disasterAI;
                }
                catch
                {
                    continue;
                }

                // ai の静的型は DisasterAI なのでこの is はダウンキャスト検査であり、
                // 「常に false」で CS0184（このプロジェクトではエラー扱い）にはならない。
                if (!(ai is EarthquakeAI)) continue;

                if (found == null) found = new List<EarthquakeReading>();
                found.Add(BuildReading((ushort)i, ref buffer[i], flags, prefab));
            }

            // 読み取り専用に包んでから渡す。スナップショットは不変であることが
            // スレッド境界の前提なので、「変更しない約束」ではなく型で保証する。
            return found == null ? EarthquakeSnapshot.EmptyQuakeList : found.AsReadOnly();
        }

        private static EarthquakeReading BuildReading(ushort id, ref DisasterData data, int flags,
                                                      EarthquakePrefabFacts prefab)
        {
            var pos = data.m_targetPosition;
            byte intensity = data.m_intensity;

            int coverage;
            bool coverageKnown = TryReadCoverage(pos, out coverage);

            // §A-3 の L / W。プレハブが解決できていなければ 0 を入れ、
            // 受け取った側（Task 4 の FaultBand）が「不明」として扱う。
            float scale = 0.5f + intensity * 0.005f;
            float crackLength = prefab.Resolved ? prefab.CrackLength * scale : 0f;
            float crackWidth = prefab.Resolved ? prefab.CrackWidth * scale : 0f;

            return new EarthquakeReading(
                id,
                new Vec3(pos.x, pos.y, pos.z),
                data.m_angle,
                intensity,
                DisasterPhases.PhaseOf(flags),
                DisasterPhases.IsLocated(flags),
                data.m_startFrame,
                data.m_activationFrame,
                // ★ 0 は「今」ではなく「未定」。SelfTrigger(64) が立っていない地震は
                //    ここが 0 のまま Emerging で永久に固まる（§A-1）。
                data.m_activationFrame != 0u,
                coverage,
                coverageKnown,
                crackLength,
                crackWidth);
        }

        /// <summary>
        /// カーソル直下の建物の余裕度。**sim スレッド専用**（建物バッファに触る）。
        ///
        /// 対象にするのは**破壊判定がこれから走る、あるいは今走っている地震 1 個だけ**で、
        /// 全地震ぶんは走査しない（毎 sim tick のコストを地震の数に比例させない）。
        ///
        /// Clearing / Finished を外しているのは性能のためではなく**正確さのため**である。
        /// 全体円盤の <c>DestroyBuildings</c> は <c>EarthquakeAI.SimulationStep</c> の
        /// **Active 分岐にしか無い**（§A-3）。収束済みの地震について「倒壊します」と
        /// 出すのは、もう起きないことを起きると言うことになる。
        ///
        /// 選定順は <see cref="QuakeSelection.SelectDamaging"/> に任せる（順位付けを
        /// 複数箇所に写さないため。あちらのクラス doc に経緯がある）。
        /// どの地震を選んだかは <see cref="EarthquakeSnapshot.CursorQuakeId"/> で名乗る。
        /// </summary>
        private static BuildingMargin ProbeCursorBuilding(IList<EarthquakeReading> quakes,
                                                          bool haveCursor, Vec3 cursor,
                                                          out ushort cursorQuakeId,
                                                          out BuildingProbeOutcome outcome,
                                                          out float heightMetres)
        {
            cursorQuakeId = 0;
            outcome = BuildingProbeOutcome.NotProbed;
            heightMetres = 0f;
            if (quakes.Count == 0) return BuildingMargin.None();

            // main スレッドが「今カーソルはここ」と言っていないなら何も調べない
            // （パネルが閉じている、マウスが UI の上にある、地形を外している）。
            if (!haveCursor) return BuildingMargin.None();

            var target = QuakeSelection.SelectDamaging(quakes);
            if (target == null) return BuildingMargin.None();

            // ★ 建物が見つかる前にここで立てる。CursorQuakeId != 0 は
            //    「この地震について、この座標を実際に調べた」の印であって
            //    「建物があった」の印ではない。区別しないと、収束中の地震しか
            //    無いとき（破壊判定はもう走らない）に、表示側が建物の上で
            //    「カーソルの下に建物がありません」という誤った説明を出す。
            cursorQuakeId = target.DisasterId;

            var band = new FaultBand(target.Epicentre.ToVec2(), target.AngleRadians,
                                     target.CrackLength, target.CrackWidth);

            // ★ 破壊コードが他 MOD に置き換えられていれば、余裕度は結論を出さない
            //    （§E-2。BuildingMargin.Evaluate の damageModelReplaced）。
            //    ModCompat.NdrPresent は起動時に 1 回だけ評価してキャッシュされる
            //    ので、ここが毎 tick 走っても PluginManager は舐め直されない。
            return BuildingProbe.ProbeAt(cursor, target, band, ModCompat.NdrPresent,
                                         out outcome, out heightMetres);
        }

        /// <summary>
        /// 指定地点の地震計カバレッジ。**クランプしない**（生値を持ち、
        /// <c>Min(cov, 100)</c> は表示側の <see cref="WarningLeadTime"/> が行う）。
        ///
        /// 呼び出し箇所は 2 つあり、**意味がまったく違う**:
        ///   - 震央（<c>m_targetPosition</c>）… バニラが警報リードタイムと
        ///     <c>located</c> の判定に実際に使う 1 点（§A-2）。
        ///   - カーソル地点 … 「今この場所に地震計は届いているか」を確かめるためだけの値。
        ///     **ここからリードタイムを出してはいけない。**
        ///
        /// 読めなかったときに 0 を返して true にしてはいけない。カバレッジ 0 は
        /// 「地震計が無い＝ハザードマップが空なのは正常」という**意味のある実測値**で、
        /// 読み取り失敗と同じ値になってしまう。
        /// </summary>
        private static bool TryReadCoverage(Vector3 position, out int coverage)
        {
            coverage = 0;
            try
            {
                if (!Singleton<ImmaterialResourceManager>.exists) return false;
                Singleton<ImmaterialResourceManager>.instance.CheckLocalResource(
                    ImmaterialResourceManager.Resource.EarthquakeCoverage, position, out coverage);
                return true;
            }
            catch
            {
                coverage = 0;
                return false;
            }
        }

        /// <summary>
        /// プレハブ 4 値をキャッシュ越しに返す。**sim スレッド専用**
        /// （<c>_prefabSearched</c> / <c>_missCallCount</c> を書き換える）。
        /// </summary>
        private static EarthquakePrefabFacts ResolvePrefabFacts()
        {
            if (_prefabSearched && _prefab.Resolved) return _prefab;

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

            if (!_prefab.Resolved)
            {
                // Warn はスロットルされないので Diag に落とす。DLC 非所持環境では
                // これが恒常的な正常状態になる。
                Log.Diag("EqPrefab",
                    "no EarthquakeAI DisasterInfo found; Natural Disasters DLC required for earthquakes");
            }
            return _prefab;
        }

        /// <summary>
        /// プレハブを走査するだけの純粋関数。キャッシュもミス回数も一切触らないので、
        /// **どのスレッドから呼んでもこのクラスの状態を壊さない**。
        /// <c>Assumptions.Run()</c>（main スレッド）はこちらを使うこと
        /// —— <see cref="ResolvePrefabFacts"/> を呼ぶと、sim スレッドが回している
        /// キャッシュを main スレッドから巻き戻すことになる
        /// （<c>FireWhirlSpawner.HasTornadoPrefab</c> で同じ欠陥を直した経緯がある）。
        ///
        /// <c>DisasterManager.FindDisasterInfo&lt;T&gt;()</c> は public static generic で、
        /// <c>PrefabCollection&lt;DisasterInfo&gt;</c> を走査して <c>m_disasterAI is T</c> の
        /// 最初のプレハブを返すだけ（IL 事実文書 §B-5）。DLC 判定は中に無く、
        /// **DLC が無ければプレハブ自体が存在せず null が返る**のが権威。
        /// </summary>
        public static EarthquakePrefabFacts ScanPrefabFacts()
        {
            try
            {
                var info = DisasterManager.FindDisasterInfo<EarthquakeAI>();
                if (info == null) return new EarthquakePrefabFacts();

                var ai = info.m_disasterAI as EarthquakeAI;
                if (ai == null) return new EarthquakePrefabFacts();

                return new EarthquakePrefabFacts(
                    ai.m_crackLength, ai.m_crackWidth, ai.m_emergingDuration, ai.m_activeDuration);
            }
            catch
            {
                return new EarthquakePrefabFacts();
            }
        }
    }
}
