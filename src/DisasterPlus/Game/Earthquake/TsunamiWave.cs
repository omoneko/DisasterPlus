using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>The trench earthquake's tsunami.</b> **Sim thread only.**
    ///
    /// The shape and the clock belong to <see cref="TsunamiSource"/> (in Core, with
    /// tests). This file does nothing but <b>hand that to the game's water simulation and
    /// measure how well it worked</b>.
    ///
    /// ── What was rebuilt (2026-08-29) ───────────────────────────
    ///
    /// The research is in <c>docs/superpowers/specs/2026-08-29-tsunami-il-facts.md</c>.
    /// The gist:
    ///
    /// &gt; Vanilla's tsunami is <b>a boundary condition that raises and lowers the sea
    /// &gt; over 1.5 cycles in one section of the map border</b>, and the wall of water is
    /// &gt; entirely the propagation of the game's shallow-water solver. The source lasts
    /// &gt; only 256 frames.
    ///
    /// The solver has <b>an external force that can be placed anywhere on the map</b>
    /// (<c>TYPE_IMPACT</c>), which "adds a slope to the water surface as though there
    /// were a hill of water there" — <b>the same thing as the sea floor rising</b>, the
    /// textbook source of a tsunami. So put one at the hypocentre and <b>the solver builds
    /// the concentric wall of water for us</b>.
    ///
    /// ── ★★ The real cause of "it does not happen" (2026-08-30) ─────────────
    ///
    /// The owner: "Is the tsunami from a trench earthquake implemented? It is not
    /// happening."
    ///
    /// The in-game log showed it <b>was</b> running. It ran, and raised the sea by all of
    /// 0.7 m:
    ///
    /// <code>
    ///   tsunami started at (-7204,-44) ... peak drive 7447 units
    ///   tsunami drive finished after 1080 frames ...
    ///       Highest sea seen over the epicentre was 0.7 m above sea level
    /// </code>
    ///
    /// There were two causes, and **neither could be seen by reading alone; both only
    /// emerged once the game's water solver was reproduced offline**
    /// (<c>tools/WaterSolverSim</c>):
    ///
    /// <list type="number">
    /// <item><b>The clock was 64 times too fast.</b> <c>SimulateWater</c> runs only once
    ///   every 64 sim frames (<see cref="FramesPerWaterStep"/>). "Drive it for 1080
    ///   frames" was, in water steps, 17 of them.</item>
    /// <item><b>Pushing continuously does not make a wave.</b> A steady external force
    ///   makes a steady outflow — that is a hole, not a wave. The force has to go
    ///   pull, push, pull (see <see cref="TsunamiSource"/>'s class doc).</item>
    /// </list>
    ///
    /// ★ A force beyond Int16's limit (32767) can be needed, so
    ///   <b>waves with the same origin and radius are stacked</b> (the forces add up
    ///   across waves).
    ///
    /// ── ★★ Rules to keep ────────────────────────────────────────
    ///
    /// <list type="bullet">
    /// <item>A <c>WaterWave</c> is <b>baked into the save</b>. <see cref="Reset"/> releases
    ///   them unconditionally.</item>
    /// <item>Keep <c>m_duration</c> <b>short and finite</b> and extend its life by
    ///   rewriting it — that way, even if the handles are lost on load, the solver clears
    ///   up within a second.</item>
    /// <item>Write <c>m_delta</c> inside a <c>Monitor.TryEnter</c> on <c>m_waterWaves</c>
    ///   (the water thread reads the same array).</item>
    /// </list>
    /// </summary>
    public static class TsunamiWave
    {
        // ★★ **Since 2026-08-31 this path no longer raises a tsunami.**
        //    <c>TYPE_IMPACT</c> only displaces water rather than creating it, so it does
        //    not survive to the shoreline (measured offline: 19.3 m at the shoreline
        //    against the DLC's 84.8 m). The current tsunami is <see cref="TsunamiRing"/>
        //    (a WaterSource placed at the hypocentre).
        //    Only two things are kept here:
        //      1. <see cref="DepthAt"/> — the depth has to be measured with the same
        //         quantity the solver uses, and that formula lives here (TrenchQuakeSlot
        //         uses it too)
        //      2. <see cref="Reset"/> — cleaning up cities that still hold water waves
        //         placed by the old version
        //    **Nobody calls Begin.**

        /// <summary><c>TYPE_IMPACT</c> for <c>WaterWave.m_type</c> (measured in the IL).</summary>
        private const ushort TypeImpact = 2;

        /// <summary>The number of 16 m cells. <c>(world + 8640) / 16</c> gives the cell (measured in the IL).</summary>
        private const int GridCells = 1080;

        /// <summary>Half the map's extent (m). The same 8640 found in <c>SplashWater</c>'s IL.</summary>
        private const float MapHalfExtent = 8640f;

        /// <summary>
        /// How many sim frames make one water step.
        ///
        /// ★★ **It is 64, not 1.** (2026-08-30)
        ///   <c>SimulateWater</c> ends by setting <c>m_waterFrameIndex</c> to
        ///   <c>start + 64</c>, and the water thread does not run again until
        ///   <c>m_simulationFrameIndex</c> overtakes that.
        ///   I.e. **the solver advances only once every 64 sim frames.**
        ///
        ///   The previous version had 8 here, so it was
        ///   <b>rewriting the force 8 times before the water moved even once</b>.
        ///   (Two corroborations in <see cref="TsunamiSource"/>'s class doc.)
        /// </summary>
        private const int FramesPerWaterStep = 64;

        /// <summary>
        /// <c>m_duration</c> (<c>m_currentTime</c> gains 64 **per water step**).
        /// 128 = **3 water steps ≈ 3.2 real seconds** (m_currentTime goes 0, 64, 128, 192,
        /// and it is only released once 192 > 128. Three, not two — one step on the safe
        /// side).
        ///
        /// ★★ 4096 -> 128 (2026-08-30, final verification). 4096 was written up as "one
        ///   real second", which was <b>wrong by a factor of 64</b>: 4096/64 = 64 water
        ///   steps = 4096 sim frames = **68 real seconds**. A wave whose handle was lost
        ///   on load would go on <b>pushing the sea for over a minute</b> with whatever
        ///   force was last written (up to 1.5× the drive). <see cref="Write"/> resets
        ///   <c>m_currentTime</c> to 0 every water step, so shortening it causes no
        ///   trouble while it is running.
        ///
        /// ★★ **Always keep it finite, and keep it short.** (Codex review P1)
        ///   A <c>WaterWave</c> is baked into the save, but <see cref="_waves"/> is a
        ///   static, so <b>it does not come back on load</b>. Set it to 65535 and
        ///   <c>m_currentTime</c> <b>sticks</b> at <c>Min(currentTime + 64, 65535)</c>, so
        ///   <c>currentTime &gt; duration</c> is never satisfied —
        ///   **a city saved mid-tsunami carries the force forever.**
        /// </summary>
        private const ushort WaveDurationTicks = 128;

        // ── State ─────────────────────────────────────────────────

        /// <summary>The handles of the stacked waves. **0 means "that slot is unused".**</summary>
        private static readonly ushort[] _waves =
            new ushort[TsunamiSource.MaxStackedWaves];

        /// <summary>
        /// A fingerprint for confirming that a slot is <b>still our own wave</b>.
        ///
        /// ★★ **<c>m_type == TYPE_IMPACT</c> alone is not enough.** (2026-08-30, final
        ///   verification) In the IL, <c>CreateWaterWave</c> <b>reuses</b> slots with
        ///   <c>m_type == 0</c>, and <c>ReleaseWaterWave</c> sets <c>m_type</c> to 0
        ///   without checking the owner and compacts the free slots at the end. On top of
        ///   that, the solver <b>releases them of its own accord</b> once
        ///   <c>m_currentTime &gt; m_duration</c>. Meanwhile
        ///   <c>DisasterHelpers.SplashWater</c> creates waves of <b>precisely
        ///   TYPE_IMPACT</b> (the water plumes from meteors and earthquakes), so in a live
        ///   city slots of this same type are constantly turning over. A slot whose
        ///   fingerprint does not match is <b>somebody else's</b> — neither write to it
        ///   nor release it.
        /// </summary>
        private static readonly int[] _fingerprints =
            new int[TsunamiSource.MaxStackedWaves];

        /// <summary>
        /// How many frames (water steps) we go on <b>following and recording the wave</b>
        /// after the force is switched off.
        ///
        /// ★★ **This is the only tool for pinning down 2026-08-30's "82 m at the
        ///   hypocentre but only a storm surge at the coast".** (The offline reproduction
        ///   gives 20-28 m at the coast, yet the game does not look like that.
        ///   **The reproduction and the game disagree**, so there is nothing for it but to
        ///   measure inside the game.)
        ///   The wave advances 8.2 m per water step, so 900 steps is 7.4 km.
        /// </summary>
        private const int WatchSteps = 900;

        /// <summary>
        /// The interval (water steps) for reporting progress.
        ///
        /// ★★ **The design that printed one line at the end was a failure.**
        ///   (2026-08-31) 900 steps is 16 real minutes. The owner closed the game before
        ///   then, and <b>not one line survived</b>. A measuring tool must not demand
        ///   that somebody sit through to the end. It reports every 120 steps (about 2
        ///   real minutes) — so 2 km is readable after 2 minutes and 4 km after 6.
        /// </summary>
        private const int WatchReportEvery = 120;

        /// <summary>The radii measured (m). They span **the distances a city is likely to be at**.</summary>
        private static readonly float[] WatchRadii = { 2000f, 4000f, 6000f, 8000f };

        /// <summary>The highest sea level seen at that radius (m).</summary>
        private static readonly float[] _watchPeak = new float[4];

        /// <summary>The water step it was seen on.</summary>
        private static readonly int[] _watchPeakStep = new int[4];

        /// <summary>The shallowest depth on that radius (m). **It shows whether a shelf throttles the wave.**</summary>
        private static readonly float[] _watchMinDepth = new float[4];

        /// <summary>
        /// How many azimuths at that radius <b>were sea</b> (out of
        /// <see cref="WatchAzimuths"/>).
        /// **0 means that ring is all land and no wave can possibly arrive there.**
        /// </summary>
        private static readonly int[] _watchSeaAzimuths = new int[4];

        private static bool _watching;
        private static uint _watchStartFrame;
        private static bool _running;
        private static uint _startFrame;
        private static uint _lastFrame;
        private static int _drive;
        private static int _delta;
        private static float _peakRiseMetres;
        private static float _peakRingMetres;
        private static float _lastCentreMetres;
        private static float _seaLevel;
        private static float _depthMetres;
        private static Vec3 _centre;
        private static bool _errorLogged;

        /// <summary>Whether a tsunami is running right now (for diagnostics and display).</summary>
        public static bool Running { get { return _running; } }

        /// <summary>The current force (in <c>m_delta</c>'s units; negative = inwards, positive = outwards).</summary>
        public static int DeltaUnits { get { return _delta; } }

        /// <summary>The magnitude of the force, decided by measurement (same units, unsigned).</summary>
        public static int DriveUnits { get { return _drive; } }

        /// <summary>The highest sea level observed over the hypocentre (m, for diagnostics).</summary>
        public static float PeakRiseMetres { get { return _peakRiseMetres; } }

        /// <summary>The highest observed at the source's rim, where the ring forms (m, for diagnostics).</summary>
        public static float PeakRingMetres { get { return _peakRingMetres; } }

        /// <summary>The water depth at the hypocentre (m, for diagnostics). **It is the solver's flow cap, exactly.**</summary>
        public static float DepthMetres { get { return _depthMetres; } }

        /// <summary>Which stage we are in (**English, for diagnostics**).</summary>
        public static string Stage
        {
            get
            {
                if (!_running) return "not running";
                return TsunamiSource.StageAt(
                    (Singleton<SimulationManager>.instance.m_currentFrameIndex - _startFrame)
                    / (float)FramesPerWaterStep);
            }
        }

        /// <summary>How the last attempt turned out (**English, for diagnostics**). Always populated on a refusal.</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// **Always call this on level load and unload.** It releases any waves placed,
        /// <b>unconditionally</b>. Forget to call it and they stay in the save (see the
        /// class doc).
        /// </summary>
        public static void Reset()
        {
            ReleaseAll();

            _running = false;
            _watching = false;
            _watchStartFrame = 0u;
            _startFrame = 0u;
            _lastFrame = 0u;
            _drive = 0;
            _delta = 0;
            _peakRiseMetres = 0f;
            _peakRingMetres = 0f;
            _lastCentreMetres = 0f;
            _depthMetres = 0f;
            Detail = null;
        }

        /// <summary>
        /// **Sim thread.** Raises a tsunami at the hypocentre
        /// <paramref name="epicentre"/>. Does nothing if one is already running (only one
        /// at a time).
        /// </summary>
        public static bool Begin(Vec3 epicentre, byte intensity, uint frame)
        {
            if (_running) return false;

            try
            {
                return BeginCore(epicentre, intensity, frame);
            }
            catch (System.Exception e)
            {
                Detail = "starting the tsunami threw " + e.GetType().Name;
                Log.Error("tsunami failed to start", e);
                ReleaseAll();
                return false;
            }
        }

        private static bool BeginCore(Vec3 epicentre, byte intensity, uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                Detail = "the water simulation is not reachable; no tsunami";
                return false;
            }

            // ★ Placed on land, nothing moves (the solver caps the flow at the depth).
            if (!terrain.HasWater(new Vector2(epicentre.X, epicentre.Z)))
            {
                Detail = "the epicentre is not on open water; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                return false;
            }

            _seaLevel = terrain.WaterSimulation.m_currentSeaLevel;
            _centre = epicentre;
            _delta = 0;
            _peakRiseMetres = 0f;
            _peakRingMetres = 0f;
            _lastCentreMetres = 0f;

            // ★★ **Measure the depth first; the force is divided by it.**
            //    The solver's flow is capped at the depth by v = min(v, m_height), so
            //    applying a deep-water force in a shallow sea <b>lays the sea floor bare
            //    at the hypocentre</b> (offline reproduction: 136 water steps ≈ 145 real
            //    seconds at a depth of 10 m).
            //    Always log it too — it is the prime suspect whenever "we push and
            //    nothing moves".
            _depthMetres = DepthAt(terrain, epicentre.X, epicentre.Z);
            _drive = TsunamiSource.DriveUnitsFor(intensity, _depthMetres);

            if (!CreateAll(terrain))
            {
                Detail = "the water simulation refused the wave; no tsunami";
                Log.Info("trench earthquake tsunami: " + Detail);
                ReleaseAll();
                return false;
            }

            _running = true;
            _startFrame = frame;
            _lastFrame = frame;
            Detail = null;

            // ★ Measure our own wave with the same ruler as vanilla's (see SeaWatch's
            //   class doc).
            SeaWatch.Arm("Disaster+ trench tsunami, drive " + _drive + " units", frame);

            Log.Info("tsunami started at (" + epicentre.X.ToString("F0") + ","
                     + epicentre.Z.ToString("F0") + "): " + CountWaves()
                     + " stacked TYPE_IMPACT water waves, radius "
                     + TsunamiSource.RadiusMetres.ToString("F0")
                     + " m. Sea level " + _seaLevel.ToString("F0")
                     + " m, water is " + _depthMetres.ToString("F1")
                     + " m deep here (the solver caps flow at the depth, so a shallow sea "
                     + "cannot carry a big wave). Drive " + _drive + " units over "
                     + TsunamiSource.WavesNeeded(_drive) + " stacked waves, written once "
                     + "per water step (" + FramesPerWaterStep
                     + " sim frames). For scale, the DLC tsunami drives the map border "
                     + "with " + TsunamiSource.VanillaDeltaUnits(intensity)
                     + " units, though that is a boundary level, not a hill. "
                     + "The drive lasts " + TsunamiSource.TotalSteps.ToString("F0")
                     + " water steps = " + (TsunamiSource.TotalSteps * FramesPerWaterStep)
                     .ToString("F0") + " sim frames");
            return true;
        }

        /// <summary>**Sim thread, below the pause guard.** Advances the force.</summary>
        public static void Tick(uint frame)
        {
            if (!_running && !_watching) return;

            try
            {
                if (_watching && !_running) { Watch(frame); return; }
                Step(frame);
            }
            catch (System.Exception e)
            {
                Detail = "the tsunami tick threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("tsunami failed", e);
                }

                // ★★ **If it falls over, fold it up.** With the path that rewrites the
                //    force stopped and the waves still in place, the sea would be pushed
                //    forever.
                ReleaseAll();
                _running = false;
            }
        }

        private static void Step(uint frame)
        {
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null || terrain.WaterSimulation == null)
            {
                ReleaseAll();
                _running = false;
                return;
            }

            // ★★ **Convert to water steps.** The solver advances in no other unit.
            float elapsed = (frame - _startFrame) / (float)FramesPerWaterStep;

            Observe(terrain);

            if (TsunamiSource.IsFinished(elapsed))
            {
                Log.Info("tsunami drive finished after " + elapsed.ToString("F0")
                         + " water steps (" + (frame - _startFrame)
                         + " sim frames). Drive settled at " + _drive + " units ("
                         + TsunamiSource.WavesNeeded(_drive) + " stacked waves). "
                         + "Highest sea over the epicentre " + _peakRiseMetres.ToString("F1")
                         + " m, over the source rim (" + TsunamiSource.RadiusMetres.ToString("F0")
                         + " m out) " + _peakRingMetres.ToString("F1")
                         + " m. Water depth here was " + _depthMetres.ToString("F1")
                         + " m. The waves are released; the solver carries the ring on its own");
                ReleaseAll();
                _running = false;

                // ★★ **Do not stop here.** Measure how far the wave reaches.
                BeginWatch(frame);
                return;
            }

            // ★★ The closed loop was abandoned (2026-08-30). The solver's response lags
            //    by about 100 steps, so any measure-and-increase control is certain to
            //    wind up (confirmed in the offline reproduction: 2000 -> 139,516 units,
            //    with the centre at -40 m, i.e. dug down to the sea floor).
            //    The force is now an open-loop constant measured with
            //    <c>tools/WaterSolverSim</c>.
            _delta = TsunamiSource.DeltaAt(elapsed, _drive);
            Write(terrain, _delta);
        }

        /// <summary>Starts watching. The depths are measured once, here (the terrain does not move).</summary>
        private static void BeginWatch(uint frame)
        {
            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) return;

            _watching = true;
            _watchStartFrame = frame;

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                _watchPeak[r] = 0f;
                _watchPeakStep[r] = -1;
                _watchMinDepth[r] = float.MaxValue;

                for (int a = 0; a < WatchAzimuths; a++)
                {
                    float ang = 6.2831853f * a / WatchAzimuths;
                    float x = _centre.X + Mathf.Cos(ang) * WatchRadii[r];
                    float z = _centre.Z + Mathf.Sin(ang) * WatchRadii[r];

                    if (x < -MapHalfExtent || x > MapHalfExtent) continue;
                    if (z < -MapHalfExtent || z > MapHalfExtent) continue;
                    if (!terrain.HasWater(new Vector2(x, z))) continue;

                    // ★ Do not mix land in as depth 0 (it would be misread as a shelf).
                    if (IsLand(terrain, x, z)) continue;

                    float d = DepthAt(terrain, x, z);
                    if (d < _watchMinDepth[r]) _watchMinDepth[r] = d;
                }

                if (_watchMinDepth[r] == float.MaxValue) _watchMinDepth[r] = 0f;
            }

            Log.Info("tsunami watch started: sampling the sea every water step at 2/4/6/8 km "
                     + "from the epicentre for " + WatchSteps + " water steps ("
                     + (WatchSteps * FramesPerWaterStep / 3600f).ToString("F0")
                     + " real minutes). Shallowest water on each ring: "
                     + _watchMinDepth[0].ToString("F0") + " / "
                     + _watchMinDepth[1].ToString("F0") + " / "
                     + _watchMinDepth[2].ToString("F0") + " / "
                     + _watchMinDepth[3].ToString("F0") + " m. The solver caps flow at the "
                     + "depth, so a shallow ring throttles the wave rather than raising it. "
                     + "NOTE: only cells whose seabed is BELOW sea level are sampled - "
                     + "hitting a mountain would otherwise read as a huge false wave");
        }

        /// <summary>The number of azimuths. **If it is high anywhere on the circle, the wave got there.**</summary>
        private const int WatchAzimuths = 12;

        /// <summary>**Sim thread.** Follows the wave and remembers the peak at each radius.</summary>
        private static void Watch(uint frame)
        {
            if (frame - _lastFrame < FramesPerWaterStep) return;
            _lastFrame = frame;

            TerrainManager terrain = Singleton<TerrainManager>.instance;
            if (terrain == null) { _watching = false; return; }

            int step = (int)((frame - _watchStartFrame) / FramesPerWaterStep);

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                float best = 0f;
                int seaCount = 0;

                for (int a = 0; a < WatchAzimuths; a++)
                {
                    float ang = 6.2831853f * a / WatchAzimuths;
                    float rise = RiseAt(terrain,
                                        _centre.X + Mathf.Cos(ang) * WatchRadii[r],
                                        _centre.Z + Mathf.Sin(ang) * WatchRadii[r]);

                    if (rise < 0f) continue;   // Land. Do not mix it in.
                    seaCount++;
                    if (rise > best) best = rise;
                }

                _watchSeaAzimuths[r] = seaCount;

                if (seaCount > 0 && best > _watchPeak[r])
                {
                    _watchPeak[r] = best;
                    _watchPeakStep[r] = step;
                }
            }

            // ★★ Progress so far. Even if the game is closed, we know this much.
            if (step > 0 && step % WatchReportEvery == 0)
            {
                Log.Info("tsunami watch @step " + step + " ("
                         + (step * FramesPerWaterStep / 3600f).ToString("F1")
                         + " real min since the drive ended): " + Rings());
            }

            if (step < WatchSteps) return;

            _watching = false;

            Log.Info("tsunami watch finished. " + Rings() + " Reading: a ring whose sea "
                     + "azimuth count is 0 is all land, so nothing can arrive there; heights "
                     + "that collapse between rings mean the sea in between is too shallow "
                     + "to carry the wave.");
        }

        /// <summary>One line per ring (the same shape for the progress reports and the closing one).</summary>
        private static string Rings()
        {
            string t = "";

            for (int r = 0; r < WatchRadii.Length; r++)
            {
                if (r > 0) t += " | ";
                t += (WatchRadii[r] / 1000f).ToString("F0") + " km: "
                     + _watchPeak[r].ToString("F1") + " m @step " + _watchPeakStep[r]
                     + " (sea " + _watchSeaAzimuths[r] + "/" + WatchAzimuths
                     + ", shallowest " + _watchMinDepth[r].ToString("F0") + " m)";
            }

            return t;
        }


        /// <summary>
        /// Looks at the sea level at the hypocentre and at its rim.
        /// **This is the only number that answers "did it work?" in the running game**,
        /// so it is always reported.
        /// </summary>
        private static void Observe(TerrainManager terrain)
        {
            // ★ Do not mix in NotSea(-1) (see the ★★ in RiseAt above).
            float centre = RiseAt(terrain, _centre.X, _centre.Z);
            _lastCentreMetres = centre > 0f ? centre : 0f;
            if (centre > _peakRiseMetres) _peakRiseMetres = centre;

            // ★ The ring forms at <b>the source's rim</b>. Watch only the centre and, the
            //   moment stages ② and ③ begin, you misread it as "not working" (because the
            //   centre goes down).
            float ring = 0f;
            for (int i = 0; i < 4; i++)
            {
                float dx = i == 0 ? TsunamiSource.RadiusMetres
                         : i == 1 ? -TsunamiSource.RadiusMetres : 0f;
                float dz = i == 2 ? TsunamiSource.RadiusMetres
                         : i == 3 ? -TsunamiSource.RadiusMetres : 0f;

                float r = RiseAt(terrain, _centre.X + dx, _centre.Z + dz);
                if (r > ring) ring = r;
            }

            if (ring > _peakRingMetres) _peakRingMetres = ring;
        }

        /// <summary>A point that is not over sea. **Never mix it into an average or a maximum.**</summary>
        private const float NotSea = -1f;

        /// <summary>
        /// How far the sea has risen above its resting level (m).
        /// **Returns <see cref="NotSea"/> anywhere that is not over sea** (the caller
        /// discards those).
        ///
        /// ★★ **Never read land elevation as "the wave".** (2026-08-31, found by
        ///   measuring in the running game) <c>TerrainManager.WaterLevel</c> returns
        ///   <b>terrain height + water column</b>. On a map with a sea level of 207 m,
        ///   sampling a 277 m mountain gives <c>WaterLevel - seaLevel = +70 m</c> without
        ///   a drop of water. That is exactly what happened — we noticed it from the
        ///   <b>impossible pairing</b> "a 70.3 m wave at 4 km, in 2 m of water". That was
        ///   not a wave; it was a mountain.
        ///
        /// ★ A cell whose sea floor is above sea level is land, and is not measured.
        ///   (Measuring water that has run up onto the shore needs a different ruler.
        ///    What we want to see here is <b>whether the wave is alive offshore</b>.)
        /// </summary>
        private static float RiseAt(TerrainManager terrain, float x, float z)
        {
            if (x < -MapHalfExtent || x > MapHalfExtent) return NotSea;
            if (z < -MapHalfExtent || z > MapHalfExtent) return NotSea;

            if (IsLand(terrain, x, z)) return NotSea;

            var xz = new Vector2(x, z);
            if (!terrain.HasWater(xz)) return NotSea;

            float rise = terrain.WaterLevel(xz) - _seaLevel;
            if (float.IsNaN(rise)) return NotSea;
            return rise < 0f ? 0f : rise;
        }

        /// <summary>Whether the sea floor is above sea level (i.e. land). **Read from the same array as the solver.**</summary>
        private static bool IsLand(TerrainManager terrain, float x, float z)
        {
            ushort[] block = terrain.BlockHeights;
            if (block == null) return true;

            int at = CellOf(z) * (GridCells + 1) + CellOf(x);
            if (at < 0 || at >= block.Length) return true;

            return block[at] / 64f >= _seaLevel;
        }

        /// <summary>
        /// The water depth at that point (m). It **is the solver's flow cap, exactly**, so
        /// <b>it must be measured with the same quantity the solver uses.</b>
        ///
        /// ★★ Never subtract using <c>SampleRawHeightSmooth</c> (2026-08-30, final
        ///   verification). In the IL, the terrain array
        ///   <c>WaterSimulation.Initialize</c> receives is
        ///   <c>TerrainManager.m_blockHeights</c>, and <c>WaterLevel</c> likewise returns
        ///   <c>blockHeights + Cell.m_height</c>. <c>SampleRawHeightSmooth</c>, however,
        ///   reads <c>m_rawHeights2</c>. The two disagree at <b>quay walls, dams, sea
        ///   defences and road foundations</b>, so subtracting gives not the water column
        ///   but <c>m_height + (block - raw)</c>.
        ///   **An overstated depth feeds straight into an overstated force, and that digs
        ///   a hole through the sea.**
        /// </summary>
        internal static float DepthAt(TerrainManager terrain, float x, float z)
        {
            float surface = terrain.WaterLevel(new Vector2(x, z));

            ushort[] block = terrain.BlockHeights;
            if (block == null) return 0f;

            // ★ The cell index is the same as measured in TsunamiAI.FindSea's IL:
            //   round (world + 8640) / 16 into 0..1080 and look up z * 1081 + x.
            int cx = CellOf(x);
            int cz = CellOf(z);
            int at = cz * (GridCells + 1) + cx;
            if (at < 0 || at >= block.Length) return 0f;

            float ground = block[at] / 64f;

            float depth = surface - ground;
            return float.IsNaN(depth) || depth < 0f ? 0f : depth;
        }

        /// <summary>Creates the stacked waves in one go. **Creating even one counts as success.**</summary>
        private static bool CreateAll(TerrainManager terrain)
        {
            int cx = CellOf(_centre.X);
            int cz = CellOf(_centre.Z);

            var data = new WaterWave();
            data.m_type = TypeImpact;
            data.m_origX = (ushort)cx;
            data.m_origZ = (ushort)cz;

            // ★ IL: R = 1 + max(maxX - origX, origX - minX) — **it looks only at X.**
            data.m_minX = (ushort)Clamp(cx - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxX = (ushort)Clamp(cx + TsunamiSource.RadiusCells, 0, GridCells);
            data.m_minZ = (ushort)Clamp(cz - TsunamiSource.RadiusCells, 0, GridCells);
            data.m_maxZ = (ushort)Clamp(cz + TsunamiSource.RadiusCells, 0, GridCells);

            data.m_dirX = 0;      // Not read for IMPACT (measured in the IL)
            data.m_dirZ = 0;
            data.m_delta = 0;
            data.m_duration = WaveDurationTicks;
            data.m_currentTime = 0;

            // ★★ **Create only as many as are needed.** (2026-08-30, final verification)
            //    The force peaks at drive × PushOvershoot, which in the current band
            //    (≤900) comes nowhere near Int16's limit (32767), so **7 of the 8 were
            //    always 0**. The solver still <b>checks every wave's bbox for every
            //    cell</b>, so they were pure waste. Not creating the unused waves also
            //    cuts <b>the surface area over which a handle can go missing by a factor
            //    of 8</b>.
            int needed = TsunamiSource.WavesNeeded(
                (int)(_drive * TsunamiSource.PushOvershoot) + 1);
            if (needed < 1) needed = 1;
            if (needed > _waves.Length) needed = _waves.Length;

            for (int i = 0; i < needed; i++)
            {
                ushort handle;

                // ★★ **Always check the return value.** false means "the limit was
                //    reached", and ignoring it and holding a 0 means going off to release
                //    somebody else's wave.
                if (!terrain.WaterSimulation.CreateWaterWave(out handle, data) || handle == 0)
                {
                    break;
                }

                _waves[i] = handle;
                _fingerprints[i] = Fingerprint(data);
            }

            return CountWaves() > 0;
        }

        private static int CountWaves()
        {
            int n = 0;
            for (int i = 0; i < _waves.Length; i++) if (_waves[i] != 0) n++;
            return n;
        }

        /// <summary>
        /// Distributes the force. <paramref name="total"/> is split into
        /// <c>MaxDeltaUnits</c> chunks across the stacked waves (any left over get 0).
        ///
        /// ★★ <c>m_waterWaves</c> is a public <c>FastList</c>, and the water thread reads
        ///   it under a <c>Monitor.TryEnter(m_waterWaves, 0)</c> spin lock (IL_0333-033F).
        ///   **Take the same lock before writing.**
        /// </summary>
        private static void Write(TerrainManager terrain, int total)
        {
            FastList<WaterWave> list = terrain.WaterSimulation.m_waterWaves;
            if (list == null) return;

            while (!System.Threading.Monitor.TryEnter(list, 0)) { }
            try
            {
                for (int i = 0; i < _waves.Length; i++)
                {
                    if (_waves[i] == 0) continue;

                    int at = _waves[i] - 1;
                    if (at < 0 || at >= list.m_size)
                    {
                        // ★ Off the register means the slot is no longer ours. Forget it.
                        _waves[i] = 0;
                        continue;
                    }

                    // ★★ **Verify identity by fingerprint** (<see cref="_fingerprints"/>).
                    if (Fingerprint(list.m_buffer[at]) != _fingerprints[i])
                    {
                        _waves[i] = 0;
                        continue;
                    }

                    list.m_buffer[at].m_delta =
                        (short)TsunamiSource.DeltaForWave(i, total);

                    // ★ Wind the lifetime back so the solver's automatic release does not
                    //   get there first.
                    list.m_buffer[at].m_currentTime = 0;
                }
            }
            finally
            {
                System.Threading.Monitor.Exit(list);
            }
        }

        /// <summary>
        /// Folds into a single int just enough of a wave's characteristics to say it is
        /// ours. <c>m_delta</c> and <c>m_currentTime</c> are rewritten every step, so they
        /// are <b>left out</b>.
        /// </summary>
        private static int Fingerprint(WaterWave w)
        {
            int h = w.m_type;
            h = h * 397 ^ w.m_origX;
            h = h * 397 ^ w.m_origZ;
            h = h * 397 ^ w.m_minX;
            h = h * 397 ^ w.m_maxX;
            h = h * 397 ^ w.m_minZ;
            h = h * 397 ^ w.m_maxZ;
            h = h * 397 ^ w.m_duration;
            return h;
        }

        /// <summary>
        /// Releases every wave placed. **Idempotent. Never throws.**
        /// This is the last line of defence — if it is not reached, the waves stay in the
        /// save.
        /// </summary>
        private static void ReleaseAll()
        {
            bool any = false;
            for (int i = 0; i < _waves.Length; i++) if (_waves[i] != 0) any = true;
            if (!any) return;

            try
            {
                TerrainManager terrain = Singleton<TerrainManager>.instance;
                if (terrain != null && terrain.WaterSimulation != null)
                {
                    FastList<WaterWave> list = terrain.WaterSimulation.m_waterWaves;

                    for (int i = 0; i < _waves.Length; i++)
                    {
                        if (_waves[i] == 0) continue;

                        // ★★ **Release only slots whose fingerprint matches.**
                        //    (2026-08-30, final verification) Release a slot that does not
                        //    match and you <b>destroy somebody else's wave</b> — because
                        //    ReleaseWaterWave does not check the owner.
                        int at = _waves[i] - 1;
                        bool mine = list != null && at >= 0 && at < list.m_size
                                    && Fingerprint(list.m_buffer[at]) == _fingerprints[i];

                        if (mine) terrain.WaterSimulation.ReleaseWaterWave(_waves[i]);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warn("tsunami: releasing the water waves failed ("
                         + e.GetType().Name + "); they may persist in this save");
            }

            for (int i = 0; i < _waves.Length; i++) { _waves[i] = 0; _fingerprints[i] = 0; }
        }

        /// <summary>World coordinate to a 16 m cell (the same formula measured in <c>SplashWater</c>'s IL).</summary>
        private static int CellOf(float world)
        {
            return Clamp((int)((world + MapHalfExtent) / 16f + 0.5f), 0, GridCells);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
