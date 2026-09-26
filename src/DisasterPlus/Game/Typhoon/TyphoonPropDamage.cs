using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The gale carries off <b>props such as signs</b>. **Sim thread only.**
    ///
    /// ── What the owner asked for (2026-08-22) ─────────────────────────────
    ///
    /// &gt; What I want is small buildings and billboard props in the city being
    /// &gt; destroyed by the gale and the heavy rain, and localised flooding.
    ///
    /// Buildings (<c>TyphoonWind.Gale</c>) and flooding (<c>TyphoonFlood</c>) were
    /// already there. <b>Props were the one thing that never broke at all.</b>
    ///
    /// ── How the sweep is built (measured from the IL) ─────────────────────
    ///
    /// <c>PropManager.m_propGrid</c> is <b>270×270</b> (72,900 elements, confirmed from
    /// the <c>newarr</c> in <c>PropManager.Awake</c>), and the map is 17,280 m on a side,
    /// so <b>one cell is 64 m</b>. Within a cell the entries are chained through a
    /// singly-linked list in <c>PropInstance.m_nextGridProp</c> (the same construction as
    /// the building and road grids).
    ///
    /// ★★ <b>Do not lick the whole storm radius in one tick.</b> At a radius of 4 km
    ///   that is 125×125 = 15,625 cells, which can hold tens of thousands of props.
    ///   We advance the cursor <see cref="CellsPerTick"/> cells at a time.
    ///
    /// ★★ <b><c>ReleaseProp</c> cannot be undone.</b> That is why the thresholds are
    ///   mean (the class doc on <c>PropGaleModel</c>). "Every sign in the city vanishes
    ///   when a typhoon comes" is a kind of breakage you cannot repair.
    ///
    /// ★ Trees are left alone. Trees belong to <c>TreeManager</c> and are not in this
    ///   grid.
    /// </summary>
    public static class TyphoonPropDamage
    {
        /// <summary>The side of the prop grid (72,900 = 270² from
        /// <c>PropManager.Awake</c>).</summary>
        private const int GridSide = 270;

        /// <summary>The side of one cell (m). 17,280 / 270.</summary>
        private const float CellSizeMetres = 64f;

        /// <summary>Half the map extent (m). Used to work out the grid origin.</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>Cells licked per tick. **Never look at the whole storm radius at
        /// once.**</summary>
        private const int CellsPerTick = 256;

        /// <summary>The interval between checks (frames). The same step as
        /// <c>TyphoonWind.Gale</c>.</summary>
        private const int IntervalFrames = 64;

        /// <summary>Ceiling on how many are blown away in one tick. **The last line of
        /// defence against a whole neighbourhood disappearing at once.**</summary>
        private const int MaxTakenPerTick = 24;

        private static int _cursor;
        private static uint _round;
        private static uint _lastFrame;
        private static int _takenTotal;
        private static int _takenLastTick;
        private static int _scannedLastTick;
        private static bool _errorLogged;

        /// <summary>How many props have been blown away so far (diagnostics).</summary>
        public static int TakenTotal { get { return _takenTotal; } }

        /// <summary>How many were blown away on the most recent tick
        /// (diagnostics).</summary>
        public static int TakenLastTick { get { return _takenLastTick; } }

        /// <summary>How many cells were looked at on the most recent tick
        /// (diagnostics). 0 means "it is not running".</summary>
        public static int ScannedLastTick { get { return _scannedLastTick; } }

        /// <summary>The most recent failure (diagnostics). **We do not do nothing
        /// silently.**</summary>
        public static string LastFailure { get; private set; }

        /// <summary>Call this on level load/unload and when a typhoon ends.</summary>
        public static void Reset()
        {
            _cursor = 0;
            _round = 0u;
            _lastFrame = 0u;
            _takenTotal = 0;
            _takenLastTick = 0;
            _scannedLastTick = 0;
            LastFailure = null;
            // _errorLogged is not reset (it is a fact about this environment).
        }

        /// <summary>
        /// **Sim thread, below the pause guard.**
        /// </summary>
        public static void Tick(TyphoonSnapshot snapshot, uint frame)
        {
            _takenLastTick = 0;
            _scannedLastTick = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.Active) return;
            if (frame - _lastFrame < IntervalFrames) return;
            _lastFrame = frame;

            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                LastFailure = "the prop sweep threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon prop damage failed", e);
                }
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (!Singleton<PropManager>.exists) { LastFailure = "no PropManager"; return; }

            PropManager props = Singleton<PropManager>.instance;
            ushort[] grid = props.m_propGrid;
            PropInstance[] buffer = props.m_props.m_buffer;
            if (grid == null || buffer == null) { LastFailure = "the prop grid is not readable"; return; }

            float radius = snapshot.GaleRadius;
            if (!(radius > 0f)) return;

            Vec3 centre = snapshot.Centre;

            // The storm radius's bounding box, in cell coordinates. **Drop whatever
            // sticks out past the map.**
            int minX = CellOf(centre.X - radius);
            int maxX = CellOf(centre.X + radius);
            int minZ = CellOf(centre.Z - radius);
            int maxZ = CellOf(centre.Z + radius);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0) return;

            int total = width * height;
            if (_cursor >= total)
            {
                // ★ We have been round once. **Advance the round** — without that, a sign
                //   that survived once would never be blown away again (the doc on
                //   PropGaleModel.Takes).
                _cursor = 0;
                _round++;
            }

            // The seed is per typhoon. **For the same typhoon, reading it any number of
            // times gives the same answer.**
            uint seed = DeterministicRandom.Hash(snapshot.TyphoonId, 0x50524F50u);

            int end = _cursor + CellsPerTick;
            if (end > total) end = total;

            for (int i = _cursor; i < end; i++)
            {
                int cx = minX + (i % width);
                int cz = minZ + (i / width);
                _scannedLastTick++;

                if (cx < 0 || cx >= GridSide || cz < 0 || cz >= GridSide) continue;

                ushort id = grid[cz * GridSide + cx];
                int guard = 0;

                while (id != 0 && guard++ < 16384)
                {
                    ushort next = buffer[id].m_nextGridProp;

                    if (_takenLastTick < MaxTakenPerTick) TryTake(props, buffer, id, centre,
                                                                  snapshot, seed);

                    id = next;
                }
            }

            _cursor = end;
            LastFailure = null;
        }

        private static void TryTake(PropManager props, PropInstance[] buffer, ushort id,
                                    Vec3 centre, TyphoonSnapshot snapshot, uint seed)
        {
            // ★ Do not touch slots that are already gone (Created is cleared).
            if ((buffer[id].m_flags & (ushort)PropInstance.Flags.Created) == 0) return;

            PropInfo info = buffer[id].Info;
            if (info == null) return;

            // ★ Decals and markers are not "things". Blowing them away is meaningless,
            //   and deleting them just punches holes in the pattern on the ground.
            if (info.m_isDecal || info.m_isMarker) return;

            float size = SizeOf(info);
            float fragility = PropGaleModel.FragilityOf(size);
            if (fragility <= 0f) return;

            Vector3 position = buffer[id].Position;
            float dx = position.x - centre.X;
            float dz = position.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance > snapshot.GaleRadius) return;

            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.StormRadius);

            if (!PropGaleModel.Takes(id, _round, wind, fragility, seed)) return;

            props.ReleaseProp(id);
            _takenLastTick++;
            _takenTotal++;
        }

        /// <summary>
        /// The prop's representative dimension (m). We use **the longest edge of its
        /// collision box** (<c>PropInfo.m_generatedInfo.m_size</c>). If it cannot be
        /// read, 0 = never blown away.
        /// </summary>
        private static float SizeOf(PropInfo info)
        {
            if (info.m_generatedInfo == null) return 0f;

            Vector3 s = info.m_generatedInfo.m_size;
            float longest = s.x;
            if (s.y > longest) longest = s.y;
            if (s.z > longest) longest = s.z;

            return longest;
        }

        private static int CellOf(float world)
        {
            return Mathf.FloorToInt((world + MapHalfExtent) / CellSizeMetres);
        }
    }
}
