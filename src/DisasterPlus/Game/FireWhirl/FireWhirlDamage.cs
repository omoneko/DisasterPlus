using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

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

        private static readonly List<IgnitionCandidate> _candidates = new List<IgnitionCandidate>();
        private static readonly List<ushort> _selected = new List<ushort>();

        /// <summary>前回の延焼判定からの経過（ゲーム内分）。レベルアンロードで必ず 0 に戻す。</summary>
        private static float _minutesSinceSpread;

        public static void Reset()
        {
            _candidates.Clear();
            _selected.Clear();
            _minutesSinceSpread = 0f;
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

            for (int w = 0; w < views.Count; w++)
            {
                var v = views[w];
                if (v.Ending) continue;

                CollectNearby(buildings, v);
                if (_candidates.Count == 0) continue;

                IgnitionSpread.Select(v.Center.ToVec2(), v.Radius, spreadStrength,
                                      _candidates, frameIndex, _selected);
                if (_selected.Count == 0) continue;

                int ignited = Ignite(buildings, v.DisasterId);
                if (ignited > 0)
                {
                    Log.Diag("ignite", "fire whirl " + v.DisasterId + " ignited " + ignited);
                }
            }
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
        private static int Ignite(Building[] buildings, ushort disasterId)
        {
            // 災害グループを渡すと m_buildingFireCount が正しく積まれる。
            // グループは DisasterAI.CreateDisaster が m_ownerInstance.Disaster = 災害ID で
            // 作って InstanceManager に登録している（IL 確認済み）ので、ここで引ける。
            var groupId = InstanceID.Empty;
            groupId.Disaster = disasterId;
            var group = InstanceManager.instance.GetGroup(groupId);

            int ignited = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                ushort id = _selected[i];
                if (id == 0 || id >= buildings.Length) continue;
                if (buildings[id].m_fireIntensity != 0) continue;   // 判定後に燃え出した分を弾く

                var info = buildings[id].Info;
                if (info == null || info.m_buildingAI == null) continue;

                if (info.m_buildingAI.BurnBuilding(id, ref buildings[id], group, false)) ignited++;
            }
            return ignited;
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
                        if ((buildings[id].m_flags & Building.Flags.Created) != Building.Flags.None)
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
