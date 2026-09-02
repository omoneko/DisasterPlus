using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>海溝型地震で、震央から遠くても火災と倒壊が起きる。</b>**sim スレッド専用。**
    ///
    /// ── 所有者の指示（2026-09-02）────────────────────────────────
    ///
    /// &gt; 海溝型地震の、地震による被害が少ないです。震源から離れていても
    /// &gt; 一定確率で火災や倒壊が起きるようにしてください（地震の規模に合わせて）
    ///
    /// 確率のモデルと「なぜ海溝型だけ被害が少ないのか」の全文は
    /// <see cref="DistantDamage"/> のクラス doc にある。ここは<b>それを建物に配る係</b>で、
    /// 走査の骨格は <see cref="LongPeriodDamage"/> をそのまま写している。
    ///
    /// ── ★★ なぜ海溝型<b>だけ</b>か ─────────────────────────────
    ///
    /// 断層型の震央は<b>プレイヤーが指した場所</b>なので、バニラの円盤は街の上に
    /// 落ちる。困っていないものを変えない —— 全ての地震に足すと、
    /// <b>依頼されていない断層型の難度まで黙って上がる</b>。
    ///
    /// 見分けは <c>TrenchQuakeSlot.IsTrenchQuake</c>（災害 ID と乱数種の組）だけを
    /// 使う。**震源が海の上かどうかでは判定しない** —— プレイヤーがバニラの
    /// 災害パネルから海に置いた地震は断層型のつもりで置いたものである
    /// （<c>TrenchQuakeSlot</c> のクラス doc が確立した規律）。
    ///
    /// ── 倒壊と火災の順序 ─────────────────────────────────────
    ///
    /// 先に倒壊を引き、外れた建物だけが出火を引く。逆にはできない ——
    /// <c>CommonBuildingAI.BurnBuilding</c> は <c>Collapsed</c> を見て断る（IL_0019）ので、
    /// 倒れた建物は燃えない。**2 つの抽選は別の鍵で引く**
    /// （<see cref="DistantDamage.FireSalt"/>）。同じ鍵だと
    /// 「倒壊しなかった建物ほど燃えにくい」という相関が付いてしまう。
    ///
    /// ── 抽選は (地震, 建物) だけで決まる ──────────────────────────
    ///
    /// ★★ **フレームも走査回数も混ぜない**（<see cref="LongPeriodDamage"/> と同じ規律）。
    ///   混ぜると同じ建物が走査のたびに抽選し直され、<b>地震が長引くほど際限なく
    ///   壊れる</b>。混ぜなければ、被害の総量は<b>走査が何回走っても変わらない</b>ので、
    ///   <see cref="DistantDamage"/> の doc の目安表がそのまま実機の期待値になる。
    ///
    /// ── 足すだけ。抑えない ───────────────────────────────────
    ///
    /// バニラの破壊にはパッチも介入もしない。<c>DisasterHelpers</c> も経由しない
    /// （<see cref="LongPeriodDamage"/> と同じ理由で、NDR のパッチ面を迂回する）。
    ///
    /// ★★ <b><c>Building.m_fireIntensity</c> は絶対に直接書かない。</b>
    ///   書くと誰も消さない永久の幽霊火災になり、**バニラの建物配列に入るので
    ///   セーブに焼き付き、MOD を外しても残る**。本プロジェクトは一度これを
    ///   出荷している。火勢の面倒はバニラ側（<c>BurnBuilding</c>）が見る。
    ///
    /// ── 1 tick あたりの仕事量の上限 ──────────────────────────────
    ///
    /// 到達距離は最大 21,300 m ＝ 建物グリッド（270×270、1 セル 64 m）の全域に
    /// なりうるので、上限に達したら打ち切り、次の走査は続きから再開する。
    /// **順序は震央から外側へ**（<see cref="OutwardCellOrder"/>）—— 行優先だと
    /// 最初に見るのが矩形の角＝いちばん確率の低い場所になる。
    /// </summary>
    public static class TrenchQuakeDistantDamage
    {
        /// <summary>
        /// 走査の間隔（フレーム相当のゲーム内時間）。
        ///
        /// ★ <see cref="LongPeriodDamage"/> の 256 より短くしてある。あちらは
        ///   地震のあいだ<b>繰り返し</b>効くが、こちらは<b>1 周で終わる</b>
        ///   （<see cref="_circuitDone"/>）ので、<b>その 1 周が本震のあいだに
        ///   終わらないと、遠方が一度も抽選されない</b>。
        ///   1 周は上限に当たって 4〜5 回に分かれるので、64 なら
        ///   ゲーム内 6 分ほどで配り終える。
        /// </summary>
        private const int IntervalFrames = 64;

        /// <summary>
        /// 1 回の走査で見るグリッドセルの上限。グリッド全体は 270x270 ＝ 72,900。
        /// </summary>
        private const int MaxCellsPerPass = 65536;

        /// <summary>
        /// 1 回の走査で調べる建物の上限。
        ///
        /// ★ <see cref="LongPeriodDamage"/> の 2,048 より大きい。**1 周を本震の
        ///   あいだに終わらせる必要がある**（<see cref="IntervalFrames"/>）ためで、
        ///   1 棟あたりの費用は距離とハッシュ 1 回だけ（抽選に当たった棟だけが
        ///   仮想呼び出しへ進む）なので、8,192 でも安い。
        ///
        ///   参考: バニラ自身の全体円盤は最大 7,172 m のグリッド走査を
        ///   <b>1 シミュレーションステップごとに、上限なしで</b>行う（§A-3）。
        /// </summary>
        private const int MaxBuildingsPerPass = 8192;

        /// <summary>建物グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>
        /// 1 セルの連結リストを辿る回数の上限（壊れた保存データ対策）。
        /// バニラの <c>DisasterHelpers.DestroyBuildings</c> の内側ループと同じ
        /// 49152 ＝ 建物バッファの大きさ。
        /// </summary>
        private const int GridChainGuard = 49152;

        /// <summary>
        /// 候補にするフラグ条件。<see cref="LongPeriodDamage"/> と同じ。
        /// <c>Collapsed</c> を弾かないと、倒した跡地の瓦礫が毎回 refused に積まれて
        /// 診断の数字が読めなくなる。
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        private static float _minutesSincePass;
        private static ushort _quakeId;

        /// <summary>
        /// 追っている地震の <c>StartFrame</c>。**災害 ID だけでは足りない。**
        ///
        /// ★★ <c>DisasterManager.CreateDisaster</c> は空いた枠を<b>先頭一致で
        ///   使い回す</b>（<c>TrenchQuakeSlot</c> のクラス doc が IL で確定させた）。
        ///   海溝型が終わって次の海溝型が<b>同じ番号を取る</b>ことは普通に起きる。
        ///   ID だけで見ていると <see cref="_circuitDone"/> が立ったままになり、
        ///   <b>2 回目の海溝型地震が黙って無傷になる</b>。
        ///   開始フレームまで見れば別人だと分かる。
        /// </summary>
        private static uint _quakeStartFrame;
        private static int _cursorOrdinal;
        private static bool _errorLogged;

        /// <summary>
        /// この地震ぶんの配布が 1 周し終えたか。**終わったら二度と走らない。**
        ///
        /// ★★ これが無いと 2 つの壊れ方をする。（2026-09-02、実装中に気付いた）
        ///
        ///   1. <b>消し止めた火事が何度でも再着火する。</b>抽選は (地震, 建物) で
        ///      決まるので、一度「出火」と出た建物は<b>毎周また出火</b>する。
        ///      消防が消した先から再び燃え、地震が終わるまで終わらない。
        ///   2. バニラが断った建物（公園・瓦礫・水没中）を毎周試し続け、
        ///      診断の refused が地震の長さに比例して膨らむ。
        ///
        ///   被害は<b>揺れが外へ伝わるのに合わせて一度だけ</b>配る。
        ///   それが <see cref="DistantDamage"/> の doc の目安表の前提でもある。
        /// </summary>
        private static bool _circuitDone;

        // ── 診断カウンタ（全て sim スレッドからのみ読み書きする）──────────────
        private static int _passes;
        private static int _lastScanned;
        private static int _lastCollapsed;
        private static int _lastIgnited;
        private static int _lastRefused;
        private static bool _lastCapped;
        private static int _totalCollapsed;
        private static int _totalIgnited;

        /// <summary>これまでに走った走査の回数（セッション累計）。</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>直近 1 回で調べた建物数（候補マスクを通り、到達範囲内にあったもの）。</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>直近 1 回で実際に倒壊した棟数。</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>直近 1 回で実際に着火した棟数。</summary>
        public static int LastIgnited { get { return _lastIgnited; } }

        /// <summary>
        /// 直近 1 回で選ばれたのにバニラが断った棟数。瓦礫・公園・水没中など。
        /// **「機能が死んでいる」と「対象が居ない」を見分ける唯一の数字。**
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>直近 1 回が上限で打ち切られたか。</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>セッション累計の倒壊棟数。</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>セッション累計の着火棟数。</summary>
        public static int TotalIgnited { get { return _totalIgnited; } }

        /// <summary>レベルアンロード時。**セッション状態を 1 つも持ち越さない。**</summary>
        public static void Reset()
        {
            _minutesSincePass = 0f;
            _quakeId = 0;
            _quakeStartFrame = 0u;
            _cursorOrdinal = 0;
            _circuitDone = false;
            _errorLogged = false;

            _passes = 0;
            _lastScanned = 0;
            _lastCollapsed = 0;
            _lastIgnited = 0;
            _lastRefused = 0;
            _lastCapped = false;
            _totalCollapsed = 0;
            _totalIgnited = 0;
        }

        /// <summary>
        /// sim スレッド。**必ず <c>EarthquakeFeature.OnSimulationTick</c> の
        /// ポーズガードより下から呼ぶこと**（ポーズ中に建物が倒れる）。
        /// </summary>
        public static void Apply(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            if (snapshot == null || !snapshot.Valid) return;

            try
            {
                Step(snapshot, deltaMinutes);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("trench-quake distant damage failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake,
                             "EqDistant", "trench distant damage failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(EarthquakeSnapshot snapshot, float deltaMinutes)
        {
            // ★ 間隔の累積は**対象の地震より先に**進める（LongPeriodDamage の I3）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSincePass += deltaMinutes;
            if (interval > 0f && _minutesSincePass > interval) _minutesSincePass = interval;

            EarthquakeReading quake = FindTrenchQuake(snapshot);
            if (quake == null)
            {
                if (_quakeId != 0) Forget();
                return;
            }

            if (quake.DisasterId != _quakeId || quake.StartFrame != _quakeStartFrame)
            {
                // ★ 地震が変わった tick は走らせない（SeismographRecorder.Rescan が
                //    同じ tick で全スロット走査を行うので、そこへ重ねない）。
                //    **累積は巻き戻さない。**
                Forget();
                _quakeId = quake.DisasterId;
                _quakeStartFrame = quake.StartFrame;
                return;
            }

            // ★★ この地震ぶんは配り終えている（_circuitDone の doc）。
            //    累積だけ進めて、走査には入らない。
            if (_circuitDone) return;

            if (framesPerMinute <= 0f) return;
            if (_minutesSincePass < interval) return;

            // 余りを繰り越さない（LongPeriodDamage / FireWhirlDamage と同じ）。
            _minutesSincePass = 0f;

            int strength = ModSettings.EarthquakeTrenchDamageStrength.value;
            if (strength <= 0) return;

            Sweep(quake, strength);
        }

        /// <summary>
        /// いま被害を配るべき海溝型地震。無ければ null。
        ///
        /// ★★ <c>QuakeSelection.SelectDamaging</c> は使わない。あれは「いちばん強い
        ///   地震 1 個」を選ぶので、<b>断層型が同時に走っているとそちらが選ばれ、
        ///   海溝型の遠地被害が黙って止まる</b>。ここが探しているのは
        ///   「強い地震」ではなく「海溝型の地震」である。
        ///
        /// ★ <c>Active</c> だけを対象にする。バニラの破壊も <c>SimulationStep</c> の
        ///   Active 分岐にしか無い（§A-3）ので、本震前に建物を潰すと
        ///   <b>揺れる前に倒れる</b>ことになる。
        ///
        /// ★★ <b><c>Located</c> は見ない。絶対に足さないこと。</b>
        ///   （2026-09-02、Codex レビューが実装中の版から掘り出した。書いた本人は
        ///     「壊れた読み取りを弾く保険」のつもりで置いていた。）
        ///
        ///   <c>Located</c>（<c>DisasterPhases.Located</c> = 4096）が立つのは
        ///   <b>震央に地震計の観測範囲があるとき</b>だけである（§A-2 / §C-2）。
        ///   あれは<b>ハザードマップに塗ってよいか</b>の旗であって被害の条件ではない ——
        ///   バニラの <c>DestroyBuildings</c> も <c>QuakeSelection.SelectDamaging</c> も
        ///   1 度も見ていない。
        ///
        ///   そして<b>海溝型の震央は数 km 沖の海の上</b>である。**そこに地震計を
        ///   建てる人はいない。** つまりこの旗を条件にすると、
        ///   <b>この機能はほとんどの都市で 1 度も走らない</b> ——
        ///   例外も警告も出ないまま、依頼された被害がまるごと消える。
        ///
        ///   震央の座標は <c>m_targetPosition</c> から来ており（<c>EarthquakeReader</c>）、
        ///   地震計の有無とは無関係に入っている。**見なくても何も困らない。**
        /// </summary>
        private static EarthquakeReading FindTrenchQuake(EarthquakeSnapshot snapshot)
        {
            var quakes = snapshot.Quakes;
            if (quakes == null) return null;

            for (int i = 0; i < quakes.Count; i++)
            {
                EarthquakeReading q = quakes[i];
                if (q == null) continue;
                if (q.Phase != EarthquakePhase.Active) continue;
                if (!TrenchQuakeSlot.IsTrenchQuake(q.DisasterId)) continue;
                return q;
            }

            return null;
        }

        /// <summary>
        /// 監視をやめる。**間隔の累積は触らない**（あれは地震ではなく時間の状態）。
        /// カウンタは診断のために残す。
        /// </summary>
        private static void Forget()
        {
            _quakeId = 0;
            _quakeStartFrame = 0u;
            _cursorOrdinal = 0;
            _circuitDone = false;
        }

        private static void Sweep(EarthquakeReading quake, int strength)
        {
            // ★ Singleton<T>.instance は sInstance が null のとき main スレッド専用の
            //    経路を走らせるので、exists で先に確認する。
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            float reach = DistantDamage.ReachMetres(quake.Intensity);
            if (!(reach > 0f)) return;

            var epicentre = quake.Epicentre.ToVec2();

            // 建物グリッドは 1 セル 64m、270x270（バニラの DestroyBuildings と同じ
            // セル 64・オフセット 135・[0,269]）。
            int minX = Clamp((int)((epicentre.X - reach) / 64f + 135f));
            int maxX = Clamp((int)((epicentre.X + reach) / 64f + 135f));
            int minZ = Clamp((int)((epicentre.Z - reach) / 64f + 135f));
            int maxZ = Clamp((int)((epicentre.Z + reach) / 64f + 135f));

            int cellCount = (maxX - minX + 1) * (maxZ - minZ + 1);
            if (cellCount <= 0) return;

            // ★ 海溝型の震央は<b>マップの外にもなる</b>（沖の海）。同じクランプを
            //   掛けるので、リングの中心は必ずグリッドの中に落ちる。
            int centreX = Clamp((int)(epicentre.X / 64f + 135f));
            int centreZ = Clamp((int)(epicentre.Z / 64f + 135f));
            int ordinalCount = OutwardCellOrder.OrdinalCount(
                OutwardCellOrder.RingRadiusFor(centreX, centreZ, minX, maxX, minZ, maxZ));

            var group = GroupOf(quake.DisasterId);

            int scanned = 0, collapsed = 0, ignited = 0, refused = 0, cells = 0;
            bool capped = false;

            int ordinal = _cursorOrdinal;
            if (ordinal < 0 || ordinal >= ordinalCount) ordinal = 0;
            int startOrdinal = ordinal;

            while (ordinal < ordinalCount)
            {
                if (cells >= MaxCellsPerPass || scanned >= MaxBuildingsPerPass)
                {
                    capped = true;
                    break;
                }

                int dx, dz;
                bool ok = OutwardCellOrder.Offset(ordinal, out dx, out dz);
                ordinal++;
                if (!ok) continue;

                int x = centreX + dx;
                int z = centreZ + dz;

                // ★ 矩形の外は**数えずに飛ばす**（数えると上限が目減りする）。
                if (x < minX || x > maxX || z < minZ || z > maxZ) continue;

                cells++;

                int index = z * GridSide + x;
                if (index < 0 || index >= grid.Length) continue;

                ushort id = grid[index];
                int guard = 0;

                while (id != 0 && id < buildings.Length)
                {
                    // ★ 次の ID は**行動する前に**控える。倒壊で建物を解放する
                    //    サードパーティの AI が居ると、このセルの残りが黙って飛ぶ。
                    ushort next = buildings[id].m_nextGridBuilding;

                    if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                    {
                        var p = buildings[id].m_position;
                        float d = Distance(epicentre, p.x, p.z);
                        if (d < reach)
                        {
                            scanned++;
                            Hit(buildings, id, group, quake, d, strength,
                                ref collapsed, ref ignited, ref refused);
                        }
                    }

                    id = next;

                    if (++guard > GridChainGuard) break;
                }
            }

            // ★ 1 周し終えたら、この地震ぶんはそこで終わり（_circuitDone の doc）。
            //   打ち切りなら続きから。
            bool finished = ordinal >= ordinalCount;
            _cursorOrdinal = finished ? 0 : ordinal;
            if (finished) _circuitDone = true;
            _passes++;
            _lastScanned = scanned;
            _lastCollapsed = collapsed;
            _lastIgnited = ignited;
            _lastRefused = refused;
            _lastCapped = capped;
            _totalCollapsed += collapsed;
            _totalIgnited += ignited;

            // ★ 0 のときも 1 行出す。「機能が死んでいる」と「範囲に建物が無い」が
            //   ログ上で区別できなくなる（③で実際に起きた形）。
            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Earthquake, "trenchDistant",
                "pass#" + _passes + " quake#" + quake.DisasterId
                + " intensity=" + quake.Intensity
                + " strength=" + strength
                + " reach=" + reach.ToString("F0")
                + " cells=" + cells + "/" + cellCount
                + " ringOrder=" + startOrdinal + ".." + (ordinal - 1)
                + " next=" + _cursorOrdinal
                + " scanned=" + scanned
                + " collapsed=" + collapsed + " ignited=" + ignited
                + " refused=" + refused
                + (capped ? " (capped; resumes next pass)"
                          : " (circuit complete; this quake is done)"));
        }

        /// <summary>
        /// 1 棟ぶんの判定と実行。
        ///
        /// **倒壊が先、外れたら出火**（クラス doc）。倒れた建物には
        /// <c>BurnBuilding</c> がどのみち断りを返す。
        /// </summary>
        private static void Hit(Building[] buildings, ushort id,
                                InstanceManager.Group group, EarthquakeReading quake,
                                float distance, int strength,
                                ref int collapsed, ref int ignited, ref int refused)
        {
            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return;

            var ai = info.m_buildingAI;

            float collapseChance =
                DistantDamage.CollapseChance(distance, quake.Intensity, strength);

            if (collapseChance > 0f
                && DeterministicRandom.Unit(quake.DisasterId, id) < collapseChance)
            {
                // demolish: false（瓦礫を残す）、burnAmount: 0（揺れで潰れるのであって
                // 焼損ではない）。m_fireIntensity には触れない。
                //
                // ★ dry-run が false でも本番は呼ぶ —— PowerPoleAI.CollapseBuilding は
                //   `if (testOnly) return false;` の直後に本物の倒壊を行う（IL 実測）。
                bool accepted = ai.CollapseBuilding(id, ref buildings[id], group,
                                                    true, false, 0);
                if (!accepted) refused++;
                if (ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0))
                {
                    collapsed++;
                }

                return;
            }

            // 判定より前に燃えている建物には何もしない（FireWhirlDamage と同じ）。
            if (buildings[id].m_fireIntensity != 0) return;

            float fireChance = DistantDamage.FireChance(distance, quake.Intensity, strength);
            if (fireChance <= 0f) return;

            // ★ 倒壊と**別の鍵**で引く（DistantDamage.FireSalt の doc）。
            uint fireKey = quake.DisasterId ^ DistantDamage.FireSalt;
            if (DeterministicRandom.Unit(fireKey, id) >= fireChance) return;

            bool burnable = ai.BurnBuilding(id, ref buildings[id], group, true);
            if (ai.BurnBuilding(id, ref buildings[id], group, false)) ignited++;
            else if (!burnable) refused++;
        }

        /// <summary>
        /// 災害グループ。渡すとバニラ側の集計（災害ごとの被害棟数）が正しく積まれる。
        /// <c>InstanceManager</c> がまだ居なければ null（バニラ自身も null を渡す
        /// 経路を持つ。集計が積まれないだけで倒壊・着火は走る）。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            if (!Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        private static int Clamp(int cell)
        {
            if (cell < 0) return 0;
            return cell > GridSide - 1 ? GridSide - 1 : cell;
        }

        private static float Distance(Vec2 epicentre, float x, float z)
        {
            float dx = x - epicentre.X;
            float dz = z - epicentre.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
