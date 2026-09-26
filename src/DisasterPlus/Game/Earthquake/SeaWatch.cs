using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Measures the map's entire sea with a single ruler.</b>
    /// **A sim-thread-only diagnostic.**
    ///
    /// ── Why it is needed (2026-08-31, the owner's suggestion) ──────────────
    ///
    /// &gt; How about actually triggering the original CS tsunami and having a look at
    /// &gt; how it behaves?
    ///
    /// **He was right.** Until then every number was "our own wave measured with our own
    /// ruler", and we were tuning it <b>without knowing what a satisfying tsunami looks
    /// like as a number</b>. Measure vanilla's tsunami with <b>the same ruler</b> and, for
    /// the first time, there is something to compare against.
    ///
    /// ★★ So this measurement is <b>not tied to our own wave</b>. It just watches the
    ///   sea. Whether vanilla's <c>TsunamiAI</c> is running or ours is, <b>the same one
    ///   line</b> comes out. Put them side by side and the difference is readable.
    ///
    /// ── How it measures ─────────────────────────────────────────────
    ///
    /// <c>WaterSimulation.BeginRead()</c> returns <b>the water cell array itself</b>
    /// (measured in the IL: a <c>Cell[]</c>, with the lock taken just once). So there is
    /// no need to call <c>WaterLevel()</c> tens of thousands of times — **borrow it once
    /// and sweep across it.**
    ///
    /// <list type="bullet">
    /// <item>Over <b>sea</b> (<c>blockHeights &lt; sea level</c>): the largest rise above
    ///   the resting level</item>
    /// <item>Over <b>land</b> (<c>blockHeights &gt;= sea level</c>): the number of cells
    ///   with water on them = <b>the flooded area</b>. This, in the end, is what makes a
    ///   player think "that's a tsunami"</item>
    /// </list>
    ///
    /// ★ Never read land elevation as "the wave". <c>WaterLevel</c> returns terrain plus
    ///   water column, so on a map with a sea level of 207 m, sampling a 277 m mountain
    ///   gives +70 m with no water at all (we walked into this once already, at the ★★ in
    ///   <c>TsunamiWave.RiseAt</c>).
    ///
    /// ── ★★ Subtract a baseline (2026-08-31, found on the first real measurement) ──
    ///
    /// The first implementation printed <b>absolute</b> values. This was line 1 from the
    /// game:
    ///
    /// <code>sea watch @0 steps: highest sea 55.1 m, land under water 3721 cells</code>
    ///
    /// **55 m and 15 km2 at step 0, before any wave had arrived.** What it was counting
    /// was <b>the rivers the map already had, and the valleys whose beds drop below sea
    /// level</b>. A river is "water sitting on a land cell", so it cannot be told apart
    /// from flooding; a valley has <c>ground &lt; sea level</c>, so it cannot be told
    /// apart from sea.
    ///
    /// So we <b>remember the entire water surface at the moment measuring starts, and
    /// from then on report only the difference from it</b>. What is remembered is the
    /// water surface (terrain plus water column), not the terrain alone — rivers do not
    /// move, so they cancel out in the difference. The same subtraction is applied to
    /// vanilla's wave and to ours, so the comparison still holds.
    /// </summary>
    public static class SeaWatch
    {
        /// <summary>The number of 16 m cells. <c>BlockHeights</c> is indexed <c>z*(1080+1)+x</c>.</summary>
        private const int GridCells = 1080;

        /// <summary>How many cells to skip between samples. **At 4 we look at 1 point in 16.**</summary>
        private const int SampleStride = 4;

        /// <summary>
        /// How many sim frames between measurements (64 = one water step).
        ///
        /// ★ Every 8 steps gives 300 lines for a single tsunami and buries
        ///   <c>output_log.txt</c> (2026-08-31, fourth round of verification). Every 60
        ///   steps — roughly one real minute — is plenty.
        /// </summary>
        private const int EveryFrames = 64 * 60;

        /// <summary>The depth (m) at which we accept there is water on land. **It separates spray from flooding.**</summary>
        private const float FloodMetres = 0.5f;

        private static uint _lastFrame;
        private static bool _armed;
        private static uint _armedFrame;
        private static string _reason;
        private static float _peakRise;
        private static int _peakFloodCells;
        private static bool _errorLogged;
        private static bool _sawVanilla;

        /// <summary>
        /// The water surface at the moment measuring started (in 1/64 m). Indexed on the
        /// <b>sampled grid</b>: <c>(z/Stride)*BaseSide + (x/Stride)</c>. Null means "not
        /// captured yet".
        /// </summary>
        private static int[] _base;

        /// <summary>The side length of the sampled grid.</summary>
        private static readonly int BaseSide = GridCells / SampleStride + 1;

        /// <summary>Whether we are measuring right now.</summary>
        public static bool Armed { get { return _armed; } }

        /// <summary>The largest rise of the sea seen so far (m).</summary>
        public static float PeakRiseMetres { get { return _peakRise; } }

        /// <summary>The largest number of flooded cells seen so far.</summary>
        public static int PeakFloodCells { get { return _peakFloodCells; } }

        /// <summary>Call this on level load and unload.</summary>
        public static void Reset()
        {
            // ★ Never carry the baseline across cities. The terrain is a different thing
            //   entirely.
            _base = null;
            _armed = false;
            _armedFrame = 0u;
            _lastFrame = 0u;
            _reason = null;
            _peakRise = 0f;
            _peakFloodCells = 0;
            _sawVanilla = false;
        }

        /// <summary>
        /// Start measuring. <paramref name="reason"/> goes into the log ("whose wave is
        /// this"). If we are already measuring, only the reason is appended.
        /// </summary>
        public static void Arm(string reason, uint frame)
        {
            if (_armed) return;

            _armed = true;
            _armedFrame = frame;
            _reason = reason;
            _peakRise = 0f;
            _peakFloodCells = 0;

            if (!CaptureBaseline())
            {
                _armed = false;
                Log.Info("sea watch could not read the water, so it is not measuring.");
                return;
            }

            Log.Info("sea watch armed (" + reason + "). From here the whole map's water is "
                     + "sampled every " + (EveryFrames / 64) + " water steps: the highest "
                     + "the sea gets anywhere, and how many land cells are under water. "
                     + "**The same ruler is used for the DLC tsunami and for ours, so the "
                     + "two runs can be compared directly.**");
        }

        /// <summary>
        /// Remembers the entire current water surface. **A measurement without this is a
        /// lie** (see the class doc).
        /// </summary>
        private static bool CaptureBaseline()
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null) return false;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return false;

            if (_base == null) _base = new int[BaseSide * BaseSide];

            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
                if (cells == null) return false;

                for (int z = 0; z <= GridCells; z += SampleStride)
                {
                    int row = z * (GridCells + 1);
                    int baseRow = (z / SampleStride) * BaseSide;

                    for (int x = 0; x <= GridCells; x += SampleStride)
                    {
                        int at = row + x;
                        _base[baseRow + x / SampleStride] =
                            (at >= 0 && at < block.Length && at < cells.Length)
                                ? block[at] + cells[at].m_height
                                : int.MinValue;
                    }
                }
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }

            return true;
        }

        /// <summary>Stop measuring and print the closing line.</summary>
        public static void Disarm(uint frame)
        {
            if (!_armed) return;

            Log.Info("sea watch finished (" + _reason + ") after "
                     + ((frame - _armedFrame) / 64) + " water steps: the sea rose at most "
                     + _peakRise.ToString("F1") + " m above where it was, most newly "
                     + "flooded land "
                     + _peakFloodCells + " sampled cells = "
                     + (_peakFloodCells * SampleStride * SampleStride * 16f * 16f / 1000000f)
                       .ToString("F2") + " km2");

            _armed = false;
            _reason = null;
        }

        /// <summary>
        /// The ceiling (in water steps) that stops us measuring forever.
        ///
        /// ★ The source runs for 768 steps, and the wave then takes about as long again
        ///   to cross the map. At 1500 it <b>gets cut off in the middle of the
        ///   flooding</b>, so leave some headroom (2026-08-31, cross-checked).
        ///   2400 steps is roughly 43 real minutes.
        /// </summary>
        private const int MaxSteps = 2400;

        /// <summary>The prefab index of vanilla's <c>TsunamiAI</c>. -1 means "not looked up yet".</summary>
        private static int _tsunamiPrefabIndex = -1;

        /// <summary>
        /// <b>Checks whether vanilla's tsunami is running.</b> If it is, we start
        /// measuring of our own accord.
        ///
        /// ★★ When the owner triggers the DLC tsunami and we do nothing,
        ///   <b>we never get the numbers to compare against</b>. So we notice it
        ///   ourselves.
        /// </summary>
        private static bool VanillaTsunamiRunning()
        {
            DisasterManager manager = Singleton<DisasterManager>.instance;
            if (manager == null || manager.m_disasters == null) return false;

            if (_tsunamiPrefabIndex < 0)
            {
                DisasterInfo info = DisasterManager.FindDisasterInfo<TsunamiAI>();
                if (info == null) return false;
                _tsunamiPrefabIndex = info.m_prefabDataIndex;
            }

            DisasterData[] buffer = manager.m_disasters.m_buffer;
            if (buffer == null) return false;

            int size = manager.m_disasters.m_size;
            if (size > buffer.Length) size = buffer.Length;

            for (int i = 1; i < size; i++)
            {
                if (buffer[i].m_flags == DisasterData.Flags.None) continue;
                if (buffer[i].m_infoIndex != _tsunamiPrefabIndex) continue;
                return true;
            }

            return false;
        }

        /// <summary>**Sim thread.** Safe to call every tick (it throttles itself).</summary>
        public static void Tick(uint frame)
        {
            // ★ When vanilla's tsunami starts, we start measuring on our own initiative.
            if ((frame & 63u) == 0u)
            {
                bool vanilla = VanillaTsunamiRunning();

                if (!_armed && vanilla)
                {
                    Arm("the DLC's own tsunami - this is the yardstick", frame);
                }
                else if (_armed && vanilla && !_sawVanilla)
                {
                    // ★★ Don't miss it when vanilla's wave starts while we are already
                    //    measuring ours. Arm ignores a second call, so without marking it
                    //    here <b>the numbers we wanted for comparison come out labelled
                    //    as our own wave.</b>
                    _sawVanilla = true;
                    _reason += " + THE DLC TSUNAMI JOINED at step "
                               + ((frame - _armedFrame) / 64);
                    Log.Info("sea watch: the DLC's own tsunami started while we were "
                             + "already measuring. From here the numbers are both waves "
                             + "together, not ours alone.");
                }
            }

            if (_armed && (frame - _armedFrame) / 64 >= MaxSteps)
            {
                Disarm(frame);
                return;
            }

            if (!_armed) return;
            if (frame - _lastFrame < EveryFrames) return;
            _lastFrame = frame;

            try
            {
                Sample(frame);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("sea watch failed", e);
                }

                _armed = false;
            }
        }

        private static void Sample(uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null) return;

            ushort[] block = terrain.BlockHeights;
            if (block == null) return;

            if (_base == null) return;

            float seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            int seaUnits = (int)(seaLevel * 64f);

            float best = 0f;
            int bestX = 0;
            int bestZ = 0;
            int flooded = 0;
            int floodUnits = (int)(FloodMetres * 64f);

            // ★★ **Borrow it once and sweep across it.** Calling WaterLevel() tens of
            //    thousands of times re-takes the BeginRead/EndRead lock each time and
            //    fights the water thread for it.
            WaterSimulation.Cell[] cells = terrain.WaterSimulation.BeginRead();

            try
            {
                if (cells == null) return;

                for (int z = 0; z <= GridCells; z += SampleStride)
                {
                    int row = z * (GridCells + 1);
                    int baseRow = (z / SampleStride) * BaseSide;

                    for (int x = 0; x <= GridCells; x += SampleStride)
                    {
                        int at = row + x;
                        if (at < 0 || at >= block.Length || at >= cells.Length) continue;

                        int was = _base[baseRow + x / SampleStride];
                        if (was == int.MinValue) continue;

                        int ground = block[at];

                        // ★★ **The difference from the resting water surface.** Not an
                        //    absolute value (see the class doc).
                        int rise = ground + cells[at].m_height - was;
                        if (rise <= 0) continue;

                        if (ground < seaUnits)
                        {
                            // Sea. How far it has risen.
                            float m = rise / 64f;
                            if (m > best) { best = m; bestX = x; bestZ = z; }
                        }
                        else if (rise > floodUnits)
                        {
                            // Land. **At least 0.5 m deeper than it was** = newly flooded.
                            flooded++;
                        }
                    }
                }
            }
            finally
            {
                terrain.WaterSimulation.EndRead();
            }

            if (best > _peakRise) _peakRise = best;
            if (flooded > _peakFloodCells) _peakFloodCells = flooded;

            Log.Info("sea watch @" + ((frame - _armedFrame) / 64) + " steps ("
                     + _reason + "): sea up " + best.ToString("F1")
                     + " m at cell (" + bestX + "," + bestZ + ") = world ("
                     + (bestX * 16f - 8640f).ToString("F0") + ","
                     + (bestZ * 16f - 8640f).ToString("F0") + "), land under water "
                     + flooded + " sampled cells = "
                     + (flooded * SampleStride * SampleStride * 16f * 16f / 1000000f)
                       .ToString("F2") + " km2. Peaks so far: " + _peakRise.ToString("F1")
                     + " m / " + _peakFloodCells + " cells");
        }
    }
}
