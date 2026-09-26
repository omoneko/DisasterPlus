using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Collects the buildings that are on fire. Sweeps read-only from the sim thread.
    ///
    /// Building.Flags.Fire does not exist (confirmed in the IL). A building is burning
    /// when m_fireIntensity &gt; 0.
    ///
    /// The building buffer is a fixed 49152 slots and mostly empty. Sweeping all of it
    /// every tick would be wasteful, so one pass is spread over several ticks. The result
    /// is only swapped in when a full pass completes (so the spawn check never runs
    /// against a half-finished list).
    /// </summary>
    public class BurningBuildingScanner
    {
        /// <summary>How many slots are swept per tick. 49152 takes 8 ticks for one
        /// pass.</summary>
        private const int SliceSize = 6144;

        private int _cursor;
        private List<BurningBuilding> _building = new List<BurningBuilding>();
        private List<BurningBuilding> _current = new List<BurningBuilding>();

        /// <summary>The result of the most recently completed pass.</summary>
        public IList<BurningBuilding> Current { get { return _current; } }

        public void Reset()
        {
            _cursor = 0;
            _building = new List<BurningBuilding>();
            _current = new List<BurningBuilding>();
        }

        /// <summary>Advances by one slice. Call from the sim thread.</summary>
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
                // A full pass. Only now do we swap the result in.
                _cursor = 0;
                _current = _building;
                _building = new List<BurningBuilding>();
            }
        }

        /// <summary>One line for the diagnostics display. Shows how far the sweep has
        /// got.</summary>
        public string DiagnosticSummary()
        {
            // _cursor is the sweep position within the building buffer. We report it as
            // progress through one pass.
            return "burning " + (_current == null ? 0 : _current.Count)
                 + " / cursor " + _cursor
                 + " (+" + SliceSize + "/tick)";
        }
    }
}
