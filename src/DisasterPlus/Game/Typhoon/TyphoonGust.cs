using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **竜巻を出さずに竜巻並みの被害だけを起こす**局所被害域（パッチ）。
    /// <b>sim スレッド専用。</b>既定 ON。
    ///
    /// ── 持ち主の指示 ──────────────────────────────────────
    ///
    /// > 竜巻を発生させずに竜巻の被害だけを複数発生させてください
    ///
    /// <b>災害の実体（<c>TornadoAI</c>）も渦の車両も漏斗のメッシュも 1 つも作らない。</b>
    /// 作るのは「台風の下のあちこちで、短いあいだ、狭い範囲だけが竜巻並みに壊れる」
    /// という現象だけである。置き方と寿命は <see cref="GustPatchPlan"/>、
    /// 壊れ方は <see cref="GustDamageModel"/>（どちらも Core、テストつき）。
    ///
    /// ── 退役した随伴竜巻との違い ──────────────────────────────
    ///
    /// 旧実装はバニラの竜巻災害を借りていた。見た目と破壊が無料でバニラ品質だった
    /// 代わりに、その破壊は <c>DisasterHelpers.DestroyStuff</c> を通るので
    /// **Natural Disasters Renewal が丸ごと置き換えていた**（IL 事実文書 §F-1）。
    /// パッチは <c>BuildingAI.CollapseBuilding</c> を直接呼ぶ（＝④の風害と同じ経路）ので
    /// **NDR と完全に無衝突**である。設定キー <c>typhoonTornado</c> /
    /// <c>typhoonTornadoCount</c> は<b>退役</b>し、読む場所はもう 1 つも無い
    /// （<c>ModSettings</c> の doc。**別の意味で再利用しないこと**）。
    ///
    /// ── 台帳を持たない ────────────────────────────────────
    ///
    /// パッチの状態は<b>台風の経過フレームだけ</b>である。位置も大きさも寿命も
    /// 「何番目のパッチか」の関数なので（<see cref="GustPatchPlan"/>）、
    /// ここが覚えているのは「前回いつ被害を出したか」と診断カウンタだけ。
    /// **したがって台風が消えたらパッチも 1 個残らず消える** ——
    /// 寿命の管理を忘れて残る、という壊れ方が構造的に起きない。
    /// <see cref="Reset"/> は <c>TyphoonController.Forget</c> と
    /// <c>TyphoonFeature.OnLevelUnloading</c> から呼ばれる（どちらも冪等）。
    ///
    /// ── 1 tick あたりの仕事量の上限（全部ここに書く）───────────────────
    ///
    /// - 被害の走査が走るのは<b>台風の経過フレーム <see cref="DamageIntervalFrames"/>
    ///   ごとに 1 回</b>まで
    /// - 1 回で見るパッチは<b>最大 <see cref="GustPatchPlan.MaxActivePatches"/> 個</b>
    /// - 1 個のパッチが見るグリッドセルは<b>最大 <see cref="MaxCellsPerPatch"/> 個</b>
    ///   （半径 95 m ＋ セル 64 m ＝ 5×5 で足りる）
    /// - 1 回で調べる建物は<b>全パッチ合計 <see cref="MaxBuildingsPerPass"/> 棟</b>
    /// - <c>AddWind</c> / <c>DestroyTrees</c> / <c>DispatchEffect</c> は
    ///   1 回につきパッチ 1 個あたり 1 度ずつ
    ///
    /// ── 破壊の経路（風害とまったく同じ規律）────────────────────────
    ///
    /// - <c>BuildingAI.CollapseBuilding(demolish: false, burnAmount: 0)</c> を直接呼ぶ。
    ///   <c>DisasterHelpers.DestroyBuildings</c> / <c>DestroyNetSegments</c> は**通さない**
    /// - <b><c>Building.m_fireIntensity</c> には 1 バイトも書かない。</b>
    ///   書くと誰も消さない永久の幽霊火災になり、セーブに焼き付いて MOD を外しても残る
    /// - <c>PowerPoleAI</c> / <c>CableCarPylonAI</c> は <c>testOnly: true</c> に false を
    ///   返してから本当に倒れるので、**dry-run でフィルタしない**（本番は必ず呼ぶ）
    /// - Shelter / DoomsdayVault / DamPowerHouse / DecorationBuilding / TsunamiBuoy は
    ///   <c>demolish: false</c> を本当に断る。**それは正しい挙動なので
    ///   <c>demolish: true</c> へ逃げない。** <see cref="LastRefused"/> に積む
    ///
    /// ── 乱数にフレームを混ぜない ──────────────────────────────
    ///
    /// 目は (パッチの種, 建物 ID) だけで決まる。混ぜると同じ建物が毎 tick 抽選し直され、
    /// パッチの中の建物が確率 1 で全滅する。混ぜないので、**パッチが近づいて確率が
    /// 上がったときに初めて倒れる** ＝「通り過ぎた跡が壊れている」になる。
    /// </summary>
    public static class TyphoonGust
    {
        /// <summary>被害の走査の間隔（台風の経過フレーム）。パッチが動くので短くする。</summary>
        private const uint DamageIntervalFrames = 16u;

        /// <summary>1 個のパッチが見るグリッドセルの上限（半径 95 m なら 5×5 で足りる）。</summary>
        private const int MaxCellsPerPatch = 25;

        /// <summary>1 回の走査で調べる建物の上限（全パッチ合計）。</summary>
        private const int MaxBuildingsPerPass = 256;

        /// <summary>建物グリッドの 1 辺のセル数（1 セル 64 m）。</summary>
        private const int GridSide = 270;

        /// <summary>1 セルの連結リストを辿る回数の上限（壊れた保存データ対策）。</summary>
        private const int GridChainGuard = 49152;

        /// <summary>候補にするフラグ条件。<c>TyphoonWind.CandidateMask</c> と同じ。</summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing
            | Building.Flags.Collapsed;

        /// <summary>パッチの種を作るときの混ぜ物。**固定値**。</summary>
        private const uint PatchSeedSalt = 0x47555354u;   // "GUST"

        /// <summary>吹き上げの鉛直成分・回転成分・求心成分（§B-1 の竜巻の実引数と同じ）。</summary>
        private const float WindUpward = 80f;

        private const float WindRotational = 0.5f;

        private const float WindRadial = -40f;

        /// <summary><c>AddWind</c> の半径倍率（パッチ半径に対して）。</summary>
        private const float WindRadiusFactor = 1.6f;

        /// <summary>倒木の「確実に倒れる」内側半径 ÷ パッチ半径。</summary>
        private const float TreeInnerFraction = 0.35f;

        /// <summary>
        /// 粉塵の密度。§B-4 の一発モードの式
        /// <c>count = max(100, πr²) × magnitude × 0.01 × rateOverTime</c> から、
        /// 半径 70 m・rate 20 でおよそ 150 粒になる値を選んである。
        /// **大きくしすぎない** —— <c>Collapse Particles</c> は本物の建物崩壊と
        /// 粒子予算（<c>maxParticles</c>）を共有しているので、食い潰すと街の崩壊が薄くなる。
        /// </summary>
        private const float DustMagnitude = 0.05f;

        /// <summary>倒木の <c>Degraded</c> 自己申告キー。</summary>
        private const string TreeNoteKey = "typhoonGustTrees";

        private static ushort _typhoonId;
        private static uint _lastDamageElapsed;
        private static bool _damagedOnce;

        /// <summary>粉塵に借りている粒子エフェクト。**参照 1 個**で持ち、
        /// 毎回 <c>== null</c> で見る（破棄済みは Unity の fake-null で null と等価になる）。</summary>
        private static ParticleEffect _dust;

        private static bool _dustMissing;
        private static bool _treesUnavailable;
        private static bool _treeNotePosted;
        private static bool _errorLogged;

        // ── 診断カウンタ（全て sim スレッドからのみ読み書きする）──────────────
        private static int _passes;
        private static int _lastActive;
        private static int _lastScanned;
        private static int _lastCollapsed;
        private static int _lastRefused;
        private static int _totalCollapsed;
        private static bool _lastCapped;

        /// <summary>これまでに走った走査の回数（セッション累計）。</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>直近の走査で生きていたパッチの数。**0 は「今は無い」で不具合ではない。**</summary>
        public static int LastActive { get { return _lastActive; } }

        /// <summary>直近の走査で調べた建物数（パッチの円の中にあったもの）。</summary>
        public static int LastScanned { get { return _lastScanned; } }

        /// <summary>直近の走査で倒壊した棟数。</summary>
        public static int LastCollapsed { get { return _lastCollapsed; } }

        /// <summary>直近の走査で**バニラが設計上断った**棟数。**0 でないのは正常。**</summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>セッション累計の倒壊棟数。</summary>
        public static int TotalCollapsed { get { return _totalCollapsed; } }

        /// <summary>直近の走査が上限で打ち切られたか。</summary>
        public static bool LastCapped { get { return _lastCapped; } }

        /// <summary>
        /// 台風を手放すとき（<c>TyphoonController.Forget</c>）とレベルアンロードで呼ぶ。
        /// **冪等。**
        ///
        /// ★ ここで「パッチを止める」処理は要らない —— パッチは台帳ではなく
        ///   台風の経過フレームの関数なので、台風が無くなった時点で 1 個も存在しなくなる
        ///   （クラス doc）。戻すのはカウンタと、次の台風へ持ち越してはいけない
        ///   走査位置だけである。
        /// </summary>
        public static void Reset()
        {
            _typhoonId = 0;
            _lastDamageElapsed = 0u;
            _damagedOnce = false;
            _passes = 0;
            _lastActive = 0;
            _lastScanned = 0;
            _lastCollapsed = 0;
            _lastRefused = 0;
            _totalCollapsed = 0;
            _lastCapped = false;

            // ★ 借りたエフェクトの参照は都市をまたいで持ち越さない（クローンしていないので
            //   破棄はしない —— 破棄したら**街じゅうの建物崩壊の粉塵が消える**）。
            _dust = null;
            _dustMissing = false;

            if (_treeNotePosted)
            {
                _treeNotePosted = false;
                FeatureHost.ClearDegraded(TyphoonFeature.FeatureName, TreeNoteKey);
            }
            // ★ _errorLogged / _treesUnavailable は戻さない。どちらも「この DLL が
            //    参照しているゲームのビルドに対する事実」であって都市ごとの状態ではない。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>TyphoonFeature.OnSimulationTick</c> のポーズガードより
        /// 下から、台風が動いているときだけ呼ぶこと**（ポーズ中に建物が倒れる）。
        /// 設定が OFF のときは呼び出し側が呼ばない。
        ///
        /// <paramref name="snapshot"/> は**前 tick の状態**なので位置も強度も読まない
        /// （<see cref="TyphoonSnapshot"/> の T3 節の注記）。<c>TyphoonController</c> の
        /// static から同じスレッドで直接読む。引数に残してあるのは他の要素と
        /// 呼び出しの形をそろえるためである。
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame, float deltaMinutes)
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
                    Log.Error("typhoon gust patches failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGust",
                             "typhoon gust patches failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step()
        {
            if (!TyphoonController.Active)
            {
                _typhoonId = 0;
                _lastActive = 0;
                return;
            }

            ushort id = TyphoonController.DisasterId;
            if (id != _typhoonId)
            {
                // 新しい台風。走査位置を持ち越さない（前の台風の経過フレームで
                // 「もう出した」と判断すると、最初のパッチが丸ごと消える）。
                _typhoonId = id;
                _damagedOnce = false;
                _lastDamageElapsed = 0u;
            }

            uint elapsed = TyphoonController.ElapsedFrames;
            if (_damagedOnce && elapsed - _lastDamageElapsed < DamageIntervalFrames) return;

            _damagedOnce = true;
            _lastDamageElapsed = elapsed;

            int strength = ModSettings.TyphoonGustStrength.value;
            if (strength < 0) strength = 0;
            if (strength > 10) strength = 10;
            if (strength == 0)
            {
                // スライダーで完全に無効化できることの保証。演出も出さない。
                _lastActive = 0;
                _lastScanned = 0;
                _lastCollapsed = 0;
                _lastRefused = 0;
                _lastCapped = false;
                return;
            }

            Sweep(id, elapsed, strength);
        }

        private static void Sweep(ushort typhoonId, uint elapsed, int strength)
        {
            uint first, last;
            if (!GustPatchPlan.AliveRange(elapsed, out first, out last))
            {
                _lastActive = 0;
                _lastScanned = 0;
                _lastCollapsed = 0;
                _lastRefused = 0;
                _lastCapped = false;
                return;
            }

            // プレハブ半径が読めていなければ何もしない（設計書 §6：推測しない）。
            float stormRadius = TyphoonController.StormRadius;
            if (!(stormRadius > 0f))
            {
                _lastActive = 0;
                return;
            }

            var centre3 = TyphoonController.Centre;
            if (float.IsNaN(centre3.X) || float.IsNaN(centre3.Z))
            {
                _lastActive = 0;
                return;
            }

            // ★ Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
            //    new GameObject を走らせる main スレッド専用 API なので exists で先に確認する。
            if (!Singleton<BuildingManager>.exists) return;

            var bm = Singleton<BuildingManager>.instance;
            if (bm == null) return;

            var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
            var grid = bm.m_buildingGrid;
            if (buildings == null || grid == null) return;

            // ★ 偏りと同じく、進行方位は**毎走査読み直す**。パッチの相対角は
            //   GustPatchPlan が進行方位からの相対で出しているので、
            //   経路が曲がればパッチの散らばりも一緒に回る。
            float heading = TyphoonController.HeadingRadians;
            bool southern = ModSettings.TyphoonSouthernHemisphere.value;
            var group = GroupOf(typhoonId);

            int active = 0, scanned = 0, collapsed = 0, refused = 0;
            bool capped = false;

            for (uint ordinal = first; ordinal <= last; ordinal++)
            {
                float relative, orbitFraction, patchRadius, strengthFraction;
                GustPatchPlan.Patch(typhoonId, ordinal, southern,
                                    out relative, out orbitFraction,
                                    out patchRadius, out strengthFraction);
                if (!(patchRadius > 0f)) continue;

                float angle = heading + relative;
                float r = orbitFraction * stormRadius;
                float px = centre3.X + Mathf.Cos(angle) * r;
                float pz = centre3.Z + Mathf.Sin(angle) * r;

                active++;

                uint seed = DeterministicRandom.Hash((uint)typhoonId ^ PatchSeedSalt, ordinal);

                if (scanned < MaxBuildingsPerPass)
                {
                    if (!Strike(buildings, grid, px, pz, patchRadius, strengthFraction,
                                strength, seed, group,
                                ref scanned, ref collapsed, ref refused))
                    {
                        capped = true;
                    }
                }
                else
                {
                    capped = true;
                }

                var position = new Vector3(px, centre3.Y, pz);
                PushWind(position, patchRadius, group);
                FellTrees(seed, position, patchRadius, group);
                Dust(position, patchRadius, strengthFraction, typhoonId);
            }

            _passes++;
            _lastActive = active;
            _lastScanned = scanned;
            _lastCollapsed = collapsed;
            _lastRefused = refused;
            _lastCapped = capped;
            _totalCollapsed += collapsed;

            WriteDiag(typhoonId, strength, elapsed, first, last, active, scanned,
                      collapsed, refused, capped);
        }

        /// <summary>
        /// パッチ 1 個ぶんの被害。上限に当たったら false（呼び出し側が capped を立てる）。
        /// </summary>
        private static bool Strike(Building[] buildings, ushort[] grid,
                                   float px, float pz, float patchRadius,
                                   float strengthFraction, int strength, uint seed,
                                   InstanceManager.Group group,
                                   ref int scanned, ref int collapsed, ref int refused)
        {
            int minX = Clamp((int)((px - patchRadius) / 64f + 135f));
            int maxX = Clamp((int)((px + patchRadius) / 64f + 135f));
            int minZ = Clamp((int)((pz - patchRadius) / 64f + 135f));
            int maxZ = Clamp((int)((pz + patchRadius) / 64f + 135f));

            int cells = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (cells >= MaxCellsPerPatch || scanned >= MaxBuildingsPerPass) return false;
                    cells++;

                    int index = z * GridSide + x;
                    if (index < 0 || index >= grid.Length) continue;

                    ushort id = grid[index];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        // ★ 次の ID は**行動する前に**控える（②の LongPeriodDamage と同じ）。
                        ushort next = buildings[id].m_nextGridBuilding;

                        if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            float dx = p.x - px;
                            float dz = p.z - pz;
                            float distance = (float)System.Math.Sqrt(dx * dx + dz * dz);

                            float chance = GustDamageModel.CollapseChance(
                                distance / patchRadius, strengthFraction, strength);

                            if (chance > 0f)
                            {
                                scanned++;

                                // ★ フレームを混ぜない（クラス doc）。
                                if (DeterministicRandom.Unit(seed, id) < chance)
                                {
                                    bool accepted;
                                    if (Collapse(buildings, id, group, out accepted)) collapsed++;
                                    else if (!accepted) refused++;
                                }
                            }
                        }

                        id = next;
                        if (++guard > GridChainGuard) break;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 実際に倒す。**<c>DisasterHelpers</c> を経由しない**（クラス doc）。
        /// **dry-run が false でも本番は必ず呼ぶ** —— <c>PowerPoleAI</c> /
        /// <c>CableCarPylonAI</c> は <c>if (testOnly) return false;</c> の直後に本物の
        /// 倒壊を行う。
        /// </summary>
        private static bool Collapse(Building[] buildings, ushort id,
                                     InstanceManager.Group group, out bool accepted)
        {
            accepted = false;

            var info = buildings[id].Info;
            if (info == null || info.m_buildingAI == null) return false;

            var ai = info.m_buildingAI;

            // demolish: false（瓦礫を残す。防災施設は断る＝正しい挙動）、
            // burnAmount: 0（風は吹き飛ばして潰すのであって焼損ではない）。
            // ★ m_fireIntensity には触れない。
            accepted = ai.CollapseBuilding(id, ref buildings[id], group, true, false, 0);
            return ai.CollapseBuilding(id, ref buildings[id], group, false, false, 0);
        }

        /// <summary>
        /// 市民と車両を吹き飛ばす。**無害**（<c>AddWindCitizens</c> ＋
        /// <c>AddWindVehicles</c> の 2 行だけで、建物・道路・樹木には触らない）。
        /// </summary>
        private static void PushWind(Vector3 position, float patchRadius,
                                     InstanceManager.Group group)
        {
            var lifted = new Vector3(position.x, position.y + patchRadius, position.z);
            var directional = new Vector3(0f, WindUpward, 0f);
            DisasterHelpers.AddWind(lifted, patchRadius * WindRadiusFactor, directional,
                                    WindRotational, WindRadial, group);
        }

        /// <summary>
        /// 倒木。**燃やさない**（<c>burnRadiusMin</c> / <c>burnRadiusMax</c> は 0）。
        /// この 1 経路だけが解決できない環境がありうる（そのときは倒木を諦め、
        /// 建物の被害はそのまま出す）。
        /// </summary>
        private static void FellTrees(uint seed, Vector3 position, float patchRadius,
                                      InstanceManager.Group group)
        {
            if (_treesUnavailable) return;

            try
            {
                DisasterHelpers.DestroyTrees((int)seed, group, position,
                                             patchRadius,                       // totalRadius
                                             0f,                                // removeRadius
                                             patchRadius * TreeInnerFraction,   // destructionMin
                                             patchRadius,                       // destructionMax
                                             0f, 0f);                           // ★ 燃やさない
            }
            catch (System.Exception e)
            {
                _treesUnavailable = true;
                Log.Warn("typhoon gust: DisasterHelpers.DestroyTrees is unusable in this build ("
                         + e.GetType().Name + "); the damage patches keep running without "
                         + "felling trees");
                UpdateTreeNote();
            }
        }

        private static void UpdateTreeNote()
        {
            if (_treeNotePosted) return;
            _treeNotePosted = true;
            FeatureHost.NoteDegraded(TyphoonFeature.FeatureName, TreeNoteKey,
                "DisasterHelpers.DestroyTrees could not be called; the tornado-strength damage "
                + "patches still collapse buildings and push citizens, but they fell no trees");
        }

        /// <summary>
        /// 粉塵を 1 発。**バニラの <c>Collapse Particles</c> を借りるだけで、
        /// クローンも改変もしない** —— 街じゅうの建物崩壊と同じインスタンスなので、
        /// ここで色や粒径を変えると街の崩壊まで変わる（エフェクト実測文書 §D-5）。
        ///
        /// <c>MultiEffect</c>（<c>Collapse Effect</c>）ではなく**粒子の子だけ**を借りる。
        /// 束のほうを撃つと <c>Collapse Sound</c> まで鳴り、走査ごとに崩壊音が繰り返す。
        ///
        /// <c>DispatchEffect</c> は <c>Monitor.TryEnter</c> のキュー投入なので
        /// **sim スレッドから呼んでよい**（IL 事実文書 §C）。
        /// 取れなければ 1 度だけ諦めて以後は呼ばない。**被害は続く。**
        /// </summary>
        private static void Dust(Vector3 position, float patchRadius, float strengthFraction,
                                 ushort typhoonId)
        {
            if (_dustMissing) return;

            // ★ 参照そのものを毎回見る。破棄済みなら fake-null で null と等価になり、
            //   ここで引き直される（2 つ目の都市の自己修復）。
            if (_dust == null)
            {
                _dust = ResolveDust();
                if (_dust == null)
                {
                    _dustMissing = true;
                    Log.Warn("typhoon gust: the game's collapse dust particles could not be "
                             + "resolved; the damage patches run without a dust plume");
                    return;
                }
            }

            if (!Singleton<EffectManager>.exists) return;

            InstanceID id = InstanceID.Empty;
            id.Disaster = typhoonId;

            var area = new EffectInfo.SpawnArea(position, Vector3.up, patchRadius * 0.6f);

            // ★ 音のグループには null を渡す。ParticleEffect は RequirePlay() が
            //   false なので音のキューには 1 件も積まれず、null が読まれることも無い
            //   （IL 事実文書 §C の DispatchEffect の分岐）。AudioManager を
            //   sim スレッドから触りに行く理由が無い。
            Singleton<EffectManager>.instance.DispatchEffect(
                _dust, id, area, Vector3.zero, 0f,
                DustMagnitude * strengthFraction, null);
        }

        /// <summary>
        /// <c>BuildingProperties.m_collapseEffect</c> から粒子の子を取り出す。
        /// **束（<c>MultiEffect</c>）ではなく粒子だけ**を返す（<see cref="Dust"/> の doc）。
        /// sim スレッドから呼ぶので <c>Singleton&lt;T&gt;.exists</c> を先に見る。
        /// </summary>
        private static ParticleEffect ResolveDust()
        {
            try
            {
                if (!Singleton<BuildingManager>.exists) return null;

                var properties = Singleton<BuildingManager>.instance.m_properties;
                if (properties == null) return null;

                EffectInfo info = properties.m_collapseEffect;
                if (info == null) return null;

                var direct = info as ParticleEffect;
                if (direct != null) return direct;

                var multi = info as MultiEffect;
                if (multi != null && multi.m_effects != null)
                {
                    for (int i = 0; i < multi.m_effects.Length; i++)
                    {
                        var child = multi.m_effects[i].m_effect as ParticleEffect;
                        if (child != null) return child;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 災害グループ。渡すとバニラ側の集計（災害ごとの被害棟数）が正しく積まれる。
        /// <c>InstanceManager</c> がまだ居なければ <c>null</c>。
        /// </summary>
        private static InstanceManager.Group GroupOf(ushort disasterId)
        {
            if (disasterId == 0 || !Singleton<InstanceManager>.exists) return null;

            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            return Singleton<InstanceManager>.instance.GetGroup(groupId);
        }

        /// <summary>
        /// **倒壊 0 のときも毎回出す。** <c>Log.Diag</c> は同一キーで間引かれるが、
        /// **引数の文字列連結は毎回走ってしまう**ので <c>DiagEnabled</c> で先に落とす。
        /// </summary>
        private static void WriteDiag(ushort typhoonId, int strength, uint elapsed,
                                      uint first, uint last, int active, int scanned,
                                      int collapsed, int refused, bool capped)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyGust",
                "gust pass#" + _passes + " typhoon#" + typhoonId
                + " strength=" + strength
                + " elapsed=" + elapsed
                + " patches=" + first + ".." + last + " (" + active + " alive)"
                + " scanned=" + scanned
                + " collapsed=" + collapsed + " (total " + _totalCollapsed + ")"
                + " refused=" + refused
                + (_treesUnavailable ? " trees=unavailable" : " trees=felled")
                + (_dustMissing ? " dust=unavailable" : " dust=ok")
                + (capped ? " (capped; some patches were not rolled this pass)" : ""));
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > GridSide - 1) return GridSide - 1;
            return v;
        }
    }
}
