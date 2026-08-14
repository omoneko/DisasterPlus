using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 旋風の周囲に火の粉を撒く。sim スレッドからのみ呼ぶこと（建物バッファを書く）。
    ///
    /// 意図的に DisasterHelpers を経由しない。
    /// 競合MOD（Natural Disasters Renewal）は DisasterHelpers.DestroyBuildings を
    /// Prefix で完全置換しており、竜巻と判定した呼び出しの破壊確率を半減させ、
    /// 設定次第では破壊を丸ごと無効化する。しかも竜巻判定に使われる burnRadius は
    /// VortexAI 内でリテラル 0 に固定されていて外から変えられない（設計書 3.3(b)）。
    ///
    /// そこで「周囲に火を撒く」という定義的な挙動だけは自前の経路に置き、
    /// 他 MOD の設定に左右されないようにする。
    /// </summary>
    public static class FireWhirlDamage
    {
        /// <summary>
        /// 延焼判定を行う間隔（sim フレーム換算）。毎 tick 判定すると重く、燃え広がりも速すぎる。
        ///
        /// フレーム番号の剰余では判定できない。SimulationManager.SimulationStep は
        /// 1 tick の中で FinalSimulationSpeed 回（ゲーム速度 1/2/3 で 1/3/9 回）ループするので、
        /// m_currentFrameIndex は tick ごとに 1 ずつではなく 1/3/9 ずつ飛ぶ。
        /// frameIndex % 16 では速度 2 で 3 倍、速度 3 で 9 倍まばらになり、
        /// 延焼速度が黙ってゲーム速度に依存してしまう。
        /// 経過ゲーム内時間を積算して閾値越えで判定する。
        /// </summary>
        private const int IntervalFrames = 16;

        /// <summary>
        /// 候補に採るフラグ条件。Created が立っていて Collapsed が立っていないこと。
        ///
        /// Collapsed（= BurnedDown、実測 0x400000。同じ値）を弾くのが要点。
        /// 燃え尽きた瓦礫は Created を持ったまま m_fireIntensity == 0 に戻るので、
        /// この除外が無いと「まだ燃えていない建物」として選ばれ続け、
        /// CommonBuildingAI.BurnBuilding に必ず断られる。火災旋風が成功した跡地は
        /// これで埋まるため、候補数・選定数まで恒常的に水増しされ、
        /// オーバーレイの数字が読めなくなる。
        /// </summary>
        private const Building.Flags CollectMask =
            Building.Flags.Created | Building.Flags.Collapsed;

        private static readonly List<IgnitionCandidate> _candidates = new List<IgnitionCandidate>();
        private static readonly List<ushort> _selected = new List<ushort>();

        /// <summary>前回の延焼判定からの経過（ゲーム内分）。レベルアンロードで必ず 0 に戻す。</summary>
        private static float _minutesSinceSpread;

        // --- 診断カウンタ -------------------------------------------------
        // すべて sim スレッドからのみ読み書きする（Apply も WriteDiagnostics も sim スレッド）。
        // 「延焼が動いているか」は WriteDiagnostics の enabled/active/scan では一切
        // 見えなかった。IL 前提の誤り 3 件目（m_fireIntensity 直接書込）が生まれ、
        // かつ何のログも出さなかったのがこの経路なので、ここだけは数字を出す。
        private static int _passes;
        private static int _lastCandidates;
        private static int _lastSelected;
        private static int _lastAttempted;
        private static int _lastRefused;
        private static int _lastIgnited;
        private static int _totalIgnited;

        private static readonly BarrenSpreadTracker _barren = new BarrenSpreadTracker();
        private static bool _barrenAlertPending;
        private static bool _barrenRecoveryPending;

        /// <summary>これまでに走った延焼判定の回数（セッション累計）。</summary>
        public static int Passes { get { return _passes; } }

        /// <summary>直近 1 回の判定で半径内に見つけた建物数（全旋風の合計）。</summary>
        public static int LastCandidates { get { return _lastCandidates; } }

        /// <summary>直近 1 回の判定で確率選定を通った棟数（全旋風の合計）。</summary>
        public static int LastSelected { get { return _lastSelected; } }

        /// <summary>
        /// 直近 1 回の判定で「バニラが受け付けるはずの建物に」BurnBuilding を呼んだ棟数
        /// （全旋風の合計）。空振り検出の証拠になるのはこの数。
        /// </summary>
        public static int LastAttempted { get { return _lastAttempted; } }

        /// <summary>
        /// 直近 1 回の判定で、選ばれたがバニラが設計上断る棟数（全旋風の合計）。
        /// 瓦礫・公園・消防署・水没中など。証拠には数えない。
        /// </summary>
        public static int LastRefused { get { return _lastRefused; } }

        /// <summary>直近 1 回の判定で着火した棟数（全旋風の合計）。</summary>
        public static int LastIgnited { get { return _lastIgnited; } }

        /// <summary>セッション累計の着火棟数。</summary>
        public static int TotalIgnited { get { return _totalIgnited; } }

        /// <summary>連続で「試したのに 0 棟」だった回数。</summary>
        public static int BarrenStreak { get { return _barren.Streak; } }

        /// <summary>空振りが閾値に達しているか。オーバーレイのバッジ用。</summary>
        public static bool SpreadLooksBroken { get { return _barren.Tripped; } }

        public static int BarrenThreshold { get { return _barren.Threshold; } }

        /// <summary>
        /// 空振り検出が「今しがた」閾値に達したかを 1 回だけ返す。
        /// 呼び出し側（FireWhirlFeature）がログと Degraded 記録を 1 回だけ出すために使う。
        /// </summary>
        public static bool ConsumeBarrenAlert()
        {
            if (!_barrenAlertPending) return false;
            _barrenAlertPending = false;
            return true;
        }

        /// <summary>
        /// 閾値に達していた空振りが「今しがた」解消したかを 1 回だけ返す。
        /// 呼び出し側（FireWhirlFeature）が Degraded の自己申告を取り下げるために使う。
        /// </summary>
        public static bool ConsumeBarrenRecovery()
        {
            if (!_barrenRecoveryPending) return false;
            _barrenRecoveryPending = false;
            return true;
        }

        public static void Reset()
        {
            _candidates.Clear();
            _selected.Clear();
            _minutesSinceSpread = 0f;

            _passes = 0;
            _lastCandidates = 0;
            _lastSelected = 0;
            _lastAttempted = 0;
            _lastRefused = 0;
            _lastIgnited = 0;
            _totalIgnited = 0;
            _barren.Reset();
            _barrenAlertPending = false;
            _barrenRecoveryPending = false;
        }

        public static void Apply(uint frameIndex, float deltaMinutes, int spreadStrength)
        {
            if (spreadStrength <= 0) return;

            if (deltaMinutes > 0f) _minutesSinceSpread += deltaMinutes;
            float interval = IntervalFrames / FeatureHost.FramesPerMinute;
            if (_minutesSinceSpread < interval) return;

            // 余りを繰り越さない。ロード直後などに大きな deltaMinutes が来ても、
            // 次 tick に連続発火せず「間隔ごとに 1 回」を保つ。
            _minutesSinceSpread = 0f;

            var views = FireWhirlRegistry.Snapshot();
            if (views.Count == 0) return;

            var buildings = BuildingManager.instance.m_buildings.m_buffer;

            int candidates = 0, selected = 0, attempted = 0, refused = 0, ignited = 0;

            for (int w = 0; w < views.Count; w++)
            {
                var v = views[w];
                if (v.Ending) continue;

                CollectNearby(buildings, v);
                candidates += _candidates.Count;
                if (_candidates.Count == 0) continue;

                IgnitionSpread.Select(v.Center.ToVec2(), v.Radius, spreadStrength,
                                      _candidates, frameIndex, _selected);
                selected += _selected.Count;
                if (_selected.Count == 0) continue;

                int tried, refusedHere;
                int lit = Ignite(buildings, v.DisasterId, out tried, out refusedHere);
                attempted += tried;
                refused += refusedHere;
                ignited += lit;

                if (lit > 0)
                {
                    Log.Diag("ignite", "fire whirl " + v.DisasterId + " ignited " + lit);
                }
            }

            _passes++;
            _lastCandidates = candidates;
            _lastSelected = selected;
            _lastAttempted = attempted;
            _lastRefused = refused;
            _lastIgnited = ignited;
            _totalIgnited += ignited;

            // 出力を ignited > 0 で囲ってはいけない。延焼が完全に死んでいる状態が
            // 出力ゼロになり、「延焼が壊れている」と「近くに燃やす物が無い」が
            // ログ上で区別できなくなる（③で実際に起きた失敗の形そのもの）。
            // Log.Diag はキーごとにスロットルされるので毎回書いても溢れない。
            Log.Diag("spread",
                "pass#" + _passes + " strength=" + spreadStrength
                + " candidates=" + candidates + " selected=" + selected
                + " attempted=" + attempted + " refused=" + refused
                + " ignited=" + ignited);

            bool wasTripped = _barren.Tripped;
            if (_barren.Record(attempted, ignited)) _barrenAlertPending = true;
            if (wasTripped && !_barren.Tripped) _barrenRecoveryPending = true;
        }

        /// <summary>
        /// 選ばれた建物に火を入れる。
        ///
        /// m_fireIntensity を直接書いてはいけない（IL 実測）。このフィールドを消費するのは
        /// CommonBuildingAI.SimulationStepActive → HandleFire だけで、BuildingAI.SimulationStep には
        /// 火災処理が無い。PowerPoleAI / DecorationBuildingAI / WaterJunctionAI / OutsideConnectionAI /
        /// IntersectionAI / CableCarPylonAI / MonorailPylonAI / RaceStartGantryAI /
        /// WildlifeSpawnPointAI（および MOD 製の BuildingAI 直系）は BuildingAI を直接継承しており、
        /// 書き込んだ火勢を誰も消さない。BurningBuildingScanner が永久に「燃えている」と数え続け、
        /// しかもその値はバニラの建物配列に入るのでセーブに焼き付き、MOD を外しても残る。
        ///
        /// 代わりにバニラの BuildingAI.BurnBuilding（public virtual, IL 確認済み）を呼ぶ。
        /// BuildingAI 側の既定実装は false を返すだけなので、燃えない建物は自然に弾かれる。
        /// CommonBuildingAI 側は GetFireParameters の可燃性判定（PlayerBuildingAI は
        /// m_fireHazard == 0 で false）、水没判定、Collapsed/BurnedDown 判定を通したうえで、
        /// m_fireIntensity・Frame.m_fireDamage・Active フラグ・BuildingDeactivated・
        /// レンダラ／色／フラグ更新・サブ建物への伝播・DisasterData.m_buildingFireCount を
        /// すべて面倒みる。火勢もそこで建物ごとに決まるので、こちらで定数を持たない。
        ///
        /// DisasterHelpers は経由しないままなので、競合MOD の設定に左右されない
        /// （クラスの先頭コメントの前提は保たれる）。
        /// </summary>
        /// <param name="attempted">
        /// 「バニラが受け付けるはずの建物に BurnBuilding を呼んだ」棟数。
        /// 診断（BarrenSpreadTracker）の証拠になるのはこの数だけ。
        /// <see cref="CanBurn"/> が false の棟にも BurnBuilding は呼ぶが、ここには数えない。
        /// </param>
        /// <param name="refused">
        /// 選ばれたが「バニラが設計上断る」棟数。attempted &lt; selected の理由が
        /// オーバーレイから読めるように残すだけで、証拠には使わない。
        /// </param>
        private static int Ignite(Building[] buildings, ushort disasterId,
                                  out int attempted, out int refused)
        {
            // 災害グループを渡すと m_buildingFireCount が正しく積まれる。
            // グループは DisasterAI.CreateDisaster が m_ownerInstance.Disaster = 災害ID で
            // 作って InstanceManager に登録している（IL 確認済み）ので、ここで引ける。
            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            var group = InstanceManager.instance.GetGroup(groupId);

            attempted = 0;
            refused = 0;
            int ignited = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                ushort id = _selected[i];
                if (id == 0 || id >= buildings.Length) continue;
                if (buildings[id].m_fireIntensity != 0) continue;   // 判定後に燃え出した分を弾く

                var info = buildings[id].Info;
                if (info == null || info.m_buildingAI == null) continue;

                bool burnable = CanBurn(id, ref buildings[id], info.m_buildingAI);
                if (burnable) attempted++; else refused++;

                // burnable が false でも呼ぶ。CanBurn はバニラの拒否条件の写しであって
                // バニラそのものではないので、写しが将来ずれたときに本物の着火を
                // こちらが握り潰さないようにする。着火すれば ignited が増えて
                // 空振り判定は解除されるので、証拠としての正しさも壊れない。
                if (info.m_buildingAI.BurnBuilding(id, ref buildings[id], group, false)) ignited++;
            }
            return ignited;
        }

        /// <summary>
        /// この建物への BurnBuilding が「バニラの設計として」通りうるか。
        ///
        /// これが要る理由: 空振り検出の証拠から「バニラが設計上断るもの」を除くため。
        /// 除かないと、火災旋風が成功した後の定常状態
        /// （＝まだ燃えている建物＋燃え尽きた瓦礫）で瓦礫だけが延々と試行・拒否され、
        /// 空振りの連続が正常な街でも必ず積み上がる。streak は着火でしか下りないので
        /// 閾値には確実に到達し、「BurnBuilding が全部拒否している」という
        /// 字義どおり正しく完全に誤解を招く警告が出る。
        ///
        /// IL 実測（CommonBuildingAI.BurnBuilding が false を返す経路は次の 3 つだけ。
        /// それ以外の出口は ldc.i4.1 / ret）:
        ///   1. GetFireParameters(...) が false
        ///        BuildingAI の既定実装        : ldc.i4.0; ret（＝常に false）
        ///        PlayerBuildingAI             : return m_fireHazard != 0（プレハブ側の値）
        ///        FireStationAI                : ldc.i4.0; ret（消防署は絶対に燃えない）
        ///        ParkAI / PlazaAI / MonumentAI 等は PlayerBuildingAI を継承したまま
        ///        なので、m_fireHazard が 0 の公園・広場はここで落ちる。
        ///   2. m_flags &amp; 0x400000（Building.Flags.Collapsed。BurnedDown と同値。実測）
        ///   3. TerrainManager.WaterLevel(pos.xz) &gt; m_position.y（水没中）
        ///
        /// さらに BuildingAI.BurnBuilding 自体が ldc.i4.0; ret なので、
        /// CommonBuildingAI を継承していない AI は何をしても燃えない。
        /// </summary>
        private static bool CanBurn(ushort id, ref Building b, BuildingAI ai)
        {
            if (!(ai is CommonBuildingAI)) return false;
            if ((b.m_flags & Building.Flags.Collapsed) != Building.Flags.None) return false;

            int fireHazard, fireSize, fireTolerance;
            if (!ai.GetFireParameters(id, ref b, out fireHazard, out fireSize, out fireTolerance))
                return false;

            var pos = b.m_position;
            return TerrainManager.instance.WaterLevel(new Vector2(pos.x, pos.z)) <= pos.y;
        }

        /// <summary>
        /// 旋風の半径内の建物を集める。
        /// BuildingManager の空間グリッドを使い、全 49152 スロットの走査を避ける。
        /// </summary>
        private static void CollectNearby(Building[] buildings, FireWhirlView v)
        {
            _candidates.Clear();

            var bm = BuildingManager.instance;
            float r = v.Radius;

            // 建物グリッドは 1 セル 64m、270x270。境界をはみ出さないようクランプする。
            int minX = Clamp((int)((v.Center.X - r) / 64f + 135f));
            int maxX = Clamp((int)((v.Center.X + r) / 64f + 135f));
            int minZ = Clamp((int)((v.Center.Z - r) / 64f + 135f));
            int maxZ = Clamp((int)((v.Center.Z + r) / 64f + 135f));

            var centre2d = v.Center.ToVec2();
            float r2 = r * r;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ushort id = bm.m_buildingGrid[z * 270 + x];
                    int guard = 0;

                    while (id != 0)
                    {
                        if ((buildings[id].m_flags & CollectMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            var pos = new Vec2(p.x, p.z);
                            if (centre2d.DistanceSquaredTo(pos) <= r2)
                            {
                                _candidates.Add(new IgnitionCandidate(
                                    id, pos, buildings[id].m_fireIntensity != 0));
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // 連結リストが壊れている保存データで無限ループしないための保険。
                        if (++guard > 32768) break;
                    }
                }
            }
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 269) return 269;
            return v;
        }
    }
}
