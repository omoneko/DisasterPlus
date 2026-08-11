using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 燃焼中の建物を集める。sim スレッドから読み取り専用で走査する。
    ///
    /// Building.Flags.Fire は存在しない（IL 確認済み）。燃焼中は m_fireIntensity &gt; 0。
    ///
    /// 建物バッファは 49152 スロットの固定長でほとんど空。毎 tick 全走査すると無駄なので、
    /// 数 tick かけて一周する。1 周が完了したときだけ結果を差し替える
    /// （走査途中の中途半端なリストで発生判定をしないため）。
    /// </summary>
    public class BurningBuildingScanner
    {
        /// <summary>1 tick あたりの走査スロット数。49152 を 8 tick で一周する。</summary>
        private const int SliceSize = 6144;

        private int _cursor;
        private List<BurningBuilding> _building = new List<BurningBuilding>();
        private List<BurningBuilding> _current = new List<BurningBuilding>();

        /// <summary>直近に完了した 1 周の結果。</summary>
        public IList<BurningBuilding> Current { get { return _current; } }

        public void Reset()
        {
            _cursor = 0;
            _building = new List<BurningBuilding>();
            _current = new List<BurningBuilding>();
        }

        /// <summary>1 スライスぶん進める。sim スレッドから呼ぶこと。</summary>
        public void ScanSlice()
        {
            var buffer = BuildingManager.instance.m_buildings.m_buffer;
            int len = buffer.Length;

            int end = _cursor + SliceSize;
            if (end > len) end = len;

            for (int i = _cursor; i < end; i++)
            {
                if ((buffer[i].m_flags & Building.Flags.Created) == Building.Flags.None) continue;
                if (buffer[i].m_fireIntensity == 0) continue;

                var p = buffer[i].m_position;
                _building.Add(new BurningBuilding((ushort)i, new Vec2(p.x, p.z)));
            }

            _cursor = end;

            if (_cursor >= len)
            {
                // 一周した。ここで初めて結果を差し替える。
                _cursor = 0;
                _current = _building;
                _building = new List<BurningBuilding>();
            }
        }
    }
}
