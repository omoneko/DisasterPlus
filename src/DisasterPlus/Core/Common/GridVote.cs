using System.Collections.Generic;

namespace DisasterPlus.Core.Common
{
    /// <summary>
    /// Votes points into square cells so that the cells near a given spot can be pulled out.
    /// It exists purely to avoid an all-pairs sweep over every building (O(n^2)); there is
    /// nothing more to it than that.
    /// </summary>
    public class GridVote
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

        public GridVote(float cellSize)
        {
            // Guards against division by zero and against the cell count exploding.
            // A caller that configured this wrongly gets absorbed here.
            _cellSize = cellSize < 1f ? 1f : cellSize;
        }

        private long KeyOf(int cx, int cz)
        {
            // Pack two ints into one long. Coordinates can be negative, so fold with an
            // unchecked cast.
            return ((long)cx << 32) ^ (uint)cz;
        }

        private int CellOf(float v)
        {
            return (int)System.Math.Floor(v / _cellSize);
        }

        /// <summary>Registers the point with index <c>index</c> in the cell containing
        /// <c>position</c>.</summary>
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
        /// Gathers the indices of the points that could be within <c>radius</c> of
        /// <c>position</c>. This is a coarse, cell-granularity filter, so the caller must
        /// always re-check the real distance.
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
