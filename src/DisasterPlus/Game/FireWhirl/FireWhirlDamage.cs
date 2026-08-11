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
        /// <summary>延焼判定を行う間隔（sim tick）。毎 tick 判定すると重く、燃え広がりも速すぎる。</summary>
        private const int IntervalTicks = 16;

        /// <summary>着火時に入れる火勢。バニラの出火と同程度にする。</summary>
        private const byte IgnitionIntensity = 128;

        private static readonly List<IgnitionCandidate> _candidates = new List<IgnitionCandidate>();
        private static readonly List<ushort> _selected = new List<ushort>();

        public static void Reset()
        {
            _candidates.Clear();
            _selected.Clear();
        }

        public static void Apply(uint frameIndex, int spreadStrength)
        {
            if (spreadStrength <= 0) return;
            if (frameIndex % IntervalTicks != 0) return;

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

                for (int i = 0; i < _selected.Count; i++)
                {
                    ushort id = _selected[i];
                    if (buildings[id].m_fireIntensity != 0) continue;   // 判定後に燃え出した分を弾く
                    buildings[id].m_fireIntensity = IgnitionIntensity;
                }

                if (_selected.Count > 0)
                {
                    Log.Diag("ignite", "fire whirl " + v.DisasterId + " ignited " + _selected.Count);
                }
            }
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
