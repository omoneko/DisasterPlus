using System.Collections.Generic;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// 点を正方セルに投票し、ある地点の近傍セルを引けるようにする。
    /// 全建物同士の総当たり（O(n^2)）を避けるためだけの構造で、それ以上の意味はない。
    /// </summary>
    public class GridVote
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public GridVote(float cellSize)
        {
            // 0 除算とセル爆発を防ぐ。呼び出し側の設定ミスをここで吸収する。
            _cellSize = cellSize < 1f ? 1f : cellSize;
        }

        private long KeyOf(int cx, int cz)
        {
            // int 2 つを long 1 つに詰める。負座標があるので unchecked キャストで畳む。
            return ((long)cx << 32) ^ (uint)cz;
        }

        private int CellOf(float v)
        {
            return (int)System.Math.Floor(v / _cellSize);
        }

        /// <summary>インデックス index の点を position のセルに登録する。</summary>
        public void Add(int index, Vec2 position)
        {
            long key = KeyOf(CellOf(position.X), CellOf(position.Z));
            List<int> bucket;
            if (!_cells.TryGetValue(key, out bucket))
            {
                bucket = new List<int>();
                _cells[key] = bucket;
            }
            bucket.Add(index);
        }

        /// <summary>
        /// position から radius 以内にありうる点のインデックスを集める。
        /// セル単位の粗い絞り込みなので、呼び出し側で実距離を必ず再判定すること。
        /// </summary>
        public void CollectNear(Vec2 position, float radius, List<int> into)
        {
            into.Clear();
            int span = (int)System.Math.Ceiling(radius / _cellSize);
            int cx = CellOf(position.X);
            int cz = CellOf(position.Z);
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    List<int> bucket;
                    if (_cells.TryGetValue(KeyOf(cx + dx, cz + dz), out bucket))
                    {
                        into.AddRange(bucket);
                    }
                }
            }
        }
    }
}
