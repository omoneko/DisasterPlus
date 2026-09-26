using ColossalFramework;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The typhoon drives the weather. <b>Sim thread only.</b>
    ///
    /// Small, but with a peculiar set of ways to get it wrong, and **none of them raises
    /// an exception**.
    ///
    /// ── 1. Write every tick ──────────────────────────────────
    ///
    /// <c>WeatherManager.SimulationStepImpl</c> compares <c>m_targetRain</c> with
    /// <c>m_currentRain</c> and, **only when they are equal**, redraws it to some other
    /// value on <c>Randomizer.Int32(20000) == 0</c> (IL facts document §A-4; IL_0222
    /// re-confirmed for this task). In other words, write once and leave it and the
    /// weather is taken away from you at 1/20000 per step from the moment
    /// <c>current</c> catches up with <c>target</c>. **Write every tick and it is never
    /// taken away.** Fog and cloud work the same way; the transition rates are
    /// <c>0.0002/step</c> for rain and fog and <c>0.0008/step</c> for cloud.
    ///
    /// ── 2. Without writing <c>m_forceWeatherOn</c>, everything goes to 0 in an environment with weather off ──
    ///
    /// The same method builds <c>w = m_enableWeather ? 1 : 0</c> and, if
    /// <c>m_forceWeatherOn != 0</c>, makes it <c>w = Max(w, m_forceWeatherOn)</c>.
    /// The <c>w &lt; 1</c> branch (IL_053D) zeroes <c>m_targetRain / Fog / Cloud</c> and
    /// crushes <c>current</c> too with <c>Min(current, w)</c>. **With the player's weather
    /// switched off, the typhoon travels under clear skies.** <c>m_forceWeatherOn</c>
    /// decays at <c>0.001/step</c>, so we rewrite it every tick. The value 2f is the same
    /// one vanilla's storms and tornadoes write (§A-1).
    ///
    /// ── 3. It disagrees with the vanilla storm once every 256 frames (no action needed) ──
    ///
    /// ④'s host is a <c>ThunderStormAI</c> with <c>SelfTrigger</c>, so **vanilla itself**
    /// also writes <c>m_targetRain = 1; m_targetCloud = 1; m_targetFog = 0;
    /// m_forceWeatherOn = 2</c> in its Active branch (§A-1). But that runs only
    /// **once per 256 sim frames** (§E-2), while ④ writes every tick.
    /// <c>m_currentRain</c> only moves at <c>0.0002/step</c>, so **one tick's worth of
    /// disagreement in the target never shows on screen. No action is needed.**
    /// Do not go and suppress it with a Harmony patch.
    ///
    /// A scan for this task found one more writer of the same character:
    /// <c>ForestFireAI.SimulationStep</c> writes <c>m_targetRain = 0f</c> while Active
    /// (<c>m_flags &amp; 8</c>) (IL_001F; the facts document had no entry for this, so it
    /// is added here). ④'s lightning could start a forest fire and the two could coexist,
    /// but that too runs once per 256 frames, so the conclusion is the same.
    ///
    /// ── 4. The wind direction cannot keep up with the typhoon (do not write <c>m_windDirection</c> directly) ──
    ///
    /// <c>m_directionSpeed</c> only rises by <c>+0.001/step</c> and is capped at the
    /// remaining angle × 0.001 (§A-4). **A typhoon's sharp turns cannot be expressed.**
    /// Writing <c>m_windDirection</c> directly would make it possible, but the only
    /// writers are vanilla's <c>SimulationStepImpl</c> and <c>Data.Deserialize</c>, and
    /// swapping out from outside something that is interpolated every frame makes the tree
    /// and road sway jump. **Do not write it directly.** Instead T5's panel shows one line
    /// saying "the wind direction only changes slowly (a limitation of the game)".
    ///
    /// The angle convention was measured, not guessed. <c>WeatherManager.EndRenderingImpl</c>
    /// builds <c>_WindDirection = (sin rad, 0, cos rad, ...)</c> from
    /// <c>rad = m_windDirection * 0.01745329</c> (IL_0000-0050), so
    /// <c>m_windDirection</c> is **a bearing where 0 degrees = +Z and 90 degrees = +X**.
    /// ④'s heading φ, on the other hand, is in a mathematical convention where
    /// <c>(cos φ, sin φ)</c> is (X, Z), so the conversion is <c>θ = 90 - φ[deg]</c>
    /// (<see cref="WindDegreesOf"/>).
    ///
    /// ── 5. The rainfall and cloud formulae are ④'s own invention ─────────
    ///
    /// <code>
    /// near  = 1 - clamp01(|centre| / galeRadius)     // towards 1 as it enters the gale radius
    /// rain  = clamp01(0.35 + 0.65 * near)
    /// cloud = clamp01(0.55 + 0.45 * near)
    /// fog   = clamp01(0.45 * near)                   // ★ added on 2026-08-22 (visibility)
    /// </code>
    /// There is no vanilla constraint to refer to (design doc §1.2). The distance is
    /// measured from **the map origin, i.e. the centre of the city**, to the typhoon's
    /// centre (before clamping).
    ///
    /// ── 6. ★ The decision about rainfall above 0.8 creating vanilla thunderstorms ──
    ///
    /// **④ deliberately takes the rainfall above 0.8.** It would be a lie for it not to
    /// pour at a typhoon's peak, and as §A-5 says **there is no field that raises the wind
    /// speed**, so rainfall and cloud cover are the only means ④ has of expressing
    /// "strength" as weather. We write the price of that here rather than hide it.
    ///
    /// At the end of <c>WeatherManager.SimulationStepImpl</c> (IL_09D3-0A30):
    /// if <c>m_currentRain &gt; 0.8f &amp;&amp; m_lightningQueue.m_size == 0</c> it calls
    /// <c>QueueLightningStrike(uint)</c>. **For this task we read all of that one-argument
    /// overload's IL and narrowed down what the facts document §A-3 says**:
    ///
    /// <code>
    /// IL_00CA-0125  sweeps the disaster buffer looking for one with m_flags &amp; 8 (Active)
    ///               whose Info is the ThunderStormAI prefab
    /// IL_012A       ★ if found, a brtrue **skips CreateDisaster**
    /// IL_0131-01B1  only when none was found: CreateDisaster (with a return-value check) →
    ///               m_intensity = 10 → m_targetPosition = a uniformly random point on the map →
    ///               StartNow → ActivateNow
    /// </code>
    ///
    /// Therefore:
    ///
    /// 1. **While ④'s typhoon is Active, the game creates no new thunderstorm disaster.**
    ///    It finds ④'s existing storm, reuses it, and merely adds one strike to that
    ///    group. The "④'s rain makes the game create a storm, self-breeding" that plan
    ///    §3.1 worried about **does not happen** while Active (the conclusion that
    ///    rejected option (b) itself does not change — the second reason for rejecting it,
    ///    "the player cannot tell that the mod is the cause", still stands).
    /// 2. A new thunderstorm disaster can only be born in the window where the rainfall is
    ///    already above 0.8 while ④'s storm is still **Emerging** (not Active), and in the
    ///    window after ④ ends while <c>m_currentRain</c> comes back down through 0.8 at
    ///    <c>0.0002/step</c>. In both cases the game itself looks at
    ///    <c>CreateDisaster</c>'s return value, so the slots do not break.
    ///    **We accept this** — a thunderstorm before and after a typhoon is correct as a
    ///    phenomenon, and suppressing it would mean patching vanilla.
    /// 3. The strike point is **a uniformly random point on the map**, not the typhoon's
    ///    centre (the one-argument overload, IL_000F-004D,
    ///    <c>Randomizer.Int32(-8640, 8640)</c>). Its distribution differs from ④'s
    ///    lightning (T6), so a bolt falling far away is not one of ④'s.
    /// 4. **A note for T6 (lightning).** The condition for ambient lightning is
    ///    <c>m_lightningQueue.m_size == 0</c>. If T6 always keeps at least one entry in the
    ///    queue then ambient lightning **stops completely**, so there is no need to
    ///    estimate a share for it against the ceiling of 20. Conversely, if T6 creates a
    ///    phase where it narrows the count to 0 (inside the eye, say), ambient lightning
    ///    comes back just there.
    ///
    /// ── 7. How it is put back (<see cref="Release"/>) ──────────────────────
    ///
    /// ④ overrides four things, and there are two ways of putting them back:
    ///
    /// | What is overridden | How it is put back |
    /// |---|---|
    /// | <c>m_targetRain</c> / <c>m_targetCloud</c> | **Write 0 explicitly.** |
    /// | <c>m_targetFog</c> | **Write 0 explicitly.** (④ raises it to 0.45, so simply stopping does not clear the sky) |
    /// | <c>m_forceWeatherOn</c> | Stop writing. It expires naturally at <c>0.001/step</c> |
    /// | <c>m_targetDirection</c> | Stop writing. Vanilla redraws it the moment it is reached (§A-4) |
    ///
    /// <c>ThunderStormAI.DeactivateDisaster</c> writes
    /// <c>m_targetRain = 0; m_targetCloud = 0</c> when <c>SelfTrigger</c> is set (§A-1,
    /// re-confirmed from the IL for this task), so in principle "just stop writing" would
    /// do. **But we cannot rely on that** — <c>DisasterAI.DeactivateNow</c> does nothing
    /// without <c>m_flags &amp; Active(8)</c> (measured from the IL for this task), so it
    /// does nothing for a typhoon stopped while Emerging. That is why
    /// <see cref="Release"/> zeroes the same two as well. Writing them twice does no harm.
    ///
    /// Not writing 0 to <c>m_forceWeatherOn</c> is deliberate. Vanilla's
    /// <c>DeactivateDisaster</c> does not touch it either, and writing 0 here in the
    /// environment of a player with the weather off turns "the typhoon leaves and the rain
    /// eases off" into "the rain vanishes the instant the typhoon leaves".
    ///
    /// ── 8. Burning into the save (closed off in whole-project review I2) ──
    ///
    /// The <c>m_targetRain</c> / <c>m_targetCloud</c> / <c>m_targetFog</c> /
    /// <c>m_forceWeatherOn</c> / <c>m_targetDirection</c> that ④ writes **all burn into
    /// the save** (<c>WeatherManager+Data.Serialize</c>, measured from the IL in this
    /// review; the ordering is in <see cref="SuspendForSave"/>'s doc). Save in the middle
    /// of a typhoon and reopen it, and maximum rain continues for an expected 20,000 steps
    /// with ④ not even running.
    /// <see cref="SuspendForSave"/> and <see cref="ReapplyAfterSave"/> close that hole.
    /// **The decision not to take the host storm itself out of the save** is in the same
    /// doc.
    /// </summary>
    public static class TyphoonWeather
    {
        /// <summary>
        /// The value written to <c>m_forceWeatherOn</c>. 2f, the same as vanilla's storms
        /// and tornadoes (§A-1). It decays at <c>0.001/step</c>, so it gives 2000 steps of
        /// grace.
        /// </summary>
        private const float ForceWeatherOn = 2f;

        private const float RainBase = 0.35f;

        /// <summary>
        /// The rainfall added by intensity. <c>RainBase + RainRange</c> is the ceiling.
        ///
        /// ── ★★ Why this went from 0.65 to 0.45 (2026-08-25, the owner's instruction) ──
        ///
        /// &gt; It might work better to drop the "thunderstorm" and have just "rain", with
        /// &gt; lightning occasionally coming out of the typhoon's clouds
        ///
        /// There is <b>exactly one</b> condition under which vanilla drops lightning out of
        /// the sky (measured at IL_09D3 in
        /// <c>WeatherManager.SimulationStepImpl</c>):
        ///
        /// <code>
        ///   if (m_currentRain &lt;= 0.8f) -> do nothing
        ///   if (m_lightningQueue.m_size != 0) -> do nothing
        ///   t      = m_currentRain * 5 - 4
        ///   chance = 5000 - RoundToInt(t * 4000)
        ///   if (randomizer.UInt32(chance) == 0) QueueLightningStrike(...)
        /// </code>
        ///
        /// ④ was swinging the rain all the way to 1.0, so it <b>always entered this
        /// branch</b>. Hold the ceiling down to <see cref="MaxRainWithoutLightning"/> and
        /// **the game drops not one bolt**. The lightning is <b>drawn inside the cloud by
        /// ourselves</b>, the same way as ⑤'s ash plume (<c>TyphoonBoltFx</c>).
        ///
        /// ★ 0.8 is still a downpour (flooding's <c>MinRainForRise</c> is 0.5).
        ///   **The rainfall itself is quite enough.**
        /// </summary>
        private const float RainRange = 0.45f;

        /// <summary>
        /// Go above this and vanilla drops lightning out of the sky (measured at IL_09D3).
        /// **Do not go above it.**
        /// </summary>
        public const float MaxRainWithoutLightning = 0.8f;
        private const float CloudBase = 0.55f;
        private const float CloudRange = 0.45f;

        /// <summary>
        /// The <c>m_targetFog</c> when the typhoon is at its closest.
        ///
        /// ★★ <b>Changed from 0 on 2026-08-22</b> (the owner's note "I would like the
        ///   storm itself reproduced"). Previously we wrote 0 to match vanilla's storms.
        ///   But half of what a storm looks like is <b>visibility dropping</b>, and fog is
        ///   the only means ④ has of showing that at ground level — the rainfall is
        ///   already pinned at 1.0, and above 0.8 is the boundary at which the game creates
        ///   a thunderstorm disaster of its own, so **there is no room to raise it**
        ///   (class doc, 6.).
        ///
        ///   <b>Fog has nothing whatsoever to do with that boundary.</b> The condition
        ///   under which <c>WeatherManager</c> calls <c>QueueLightningStrike</c> is
        ///   <c>m_currentRain &gt; 0.8</c> and nothing else; <c>m_currentFog</c> appears
        ///   nowhere (re-confirmed from the IL for this task).
        ///   **The lightning balance does not move by one bit.**
        ///
        ///   It is held at 0.45 because the game becomes unplayable once you cannot see the
        ///   city. The transition rate is <c>0.0002/step</c>, the same as rain, so it hazes
        ///   over slowly as the typhoon approaches and clears slowly once it leaves.
        /// </summary>
        private const float FogPeak = 0.45f;

        private static bool _driving;
        private static float _lastRain;
        private static float _lastCloud;
        private static float _lastFog;
        private static float _lastDirectionDegrees;
        private static bool _weatherDisabledByPlayer;
        private static bool _errorLogged;

        /// <summary>Whether ④ is writing the weather on this tick.</summary>
        public static bool Driving { get { return _driving; } }

        /// <summary>The <c>m_targetRain</c> most recently written. **This is a target, not
        /// the measured rainfall.**</summary>
        public static float LastRain { get { return _lastRain; } }

        public static float LastCloud { get { return _lastCloud; } }

        public static float LastFog { get { return _lastFog; } }

        public static float LastDirectionDegrees { get { return _lastDirectionDegrees; } }

        /// <summary>
        /// Whether the player has weather switched off in the settings
        /// (<c>m_enableWeather == false</c>).
        /// **A legitimate setting, not a fault.** ④ still shows the storm in that
        /// environment through <c>m_forceWeatherOn</c>, but it does not do so silently — it
        /// puts a note in the diagnostics.
        /// </summary>
        public static bool WeatherDisabledByPlayer { get { return _weatherDisabledByPlayer; } }

        /// <summary>
        /// Sim thread. Call it below the pause guard in
        /// <c>TyphoonFeature.OnSimulationTick</c>, **immediately after**
        /// <c>TyphoonController.Tick</c>, and only while a typhoon is running.
        ///
        /// <paramref name="deltaMinutes"/> is not used. What this writes is
        /// **a target determined by the current geometry**, not a quantity accumulated over
        /// time, and the speed at which it approaches the target is decided by the game at
        /// <c>0.0002/step</c> (<c>0.0008</c> for cloud) (§A-4). It is kept as a parameter to
        /// give every element (T6 to T10) the same call shape.
        ///
        /// The typhoon state carried on <paramref name="snapshot"/> is **the previous
        /// tick's**, so it is not used (the note in <see cref="TyphoonSnapshot"/>'s T3
        /// section). Both the coordinates and the radii are read directly from
        /// <see cref="TyphoonController"/>'s statics on the same thread.
        /// </summary>
        public static void Drive(TyphoonSnapshot snapshot, float deltaMinutes)
        {
            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon weather driving failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyWeather",
                             "typhoon weather driving failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step()
        {
            // ★ Look at exists first. Singleton<T>.instance runs FindObjectOfType and
            //    new GameObject when sInstance is null, which makes it a main thread only
            //    API.
            if (!Singleton<WeatherManager>.exists)
            {
                _driving = false;
                return;
            }

            var w = Singleton<WeatherManager>.instance;

            float near = NearnessOf(TyphoonController.Centre, TyphoonController.GaleRadius);
            float rain = Clamp01(RainBase + RainRange * near);
            float cloud = Clamp01(CloudBase + CloudRange * near);
            float direction = WindDegreesOf(TyphoonController.HeadingRadians);

            // ★ Write every tick (class doc, 1.).
            float fog = Clamp01(FogPeak * near);

            // ★★ **Never let it go above 0.8.** Above that, vanilla drops lightning out of
            //    the sky, and it comes down from higher up than the cloud (the owner's
            //    report "the lightning appears above the typhoon's cloud"). The IL basis is
            //    in the doc above.
            if (rain > MaxRainWithoutLightning) rain = MaxRainWithoutLightning;

            w.m_targetRain = rain;
            w.m_targetCloud = cloud;
            w.m_targetFog = fog;

            // ★ Without this, everything is crushed to 0 in an environment with weather off
            //   (class doc, 2.).
            w.m_forceWeatherOn = ForceWeatherOn;

            // The wind direction is redrawn the moment it is reached, so we keep writing
            // this every tick too (§A-4).
            w.m_targetDirection = direction;

            _driving = true;
            _lastRain = rain;
            _lastCloud = cloud;
            _lastFog = fog;
            _lastDirectionDegrees = direction;
            _weatherDisabledByPlayer = !w.m_enableWeather;
        }

        /// <summary>
        /// How near the typhoon is as seen from the city (the map origin), [0, 1]. 0 at the
        /// rim of the gale radius, 1 at the centre.
        /// 0 if the radius could not be read (i.e. the weakest rain; we do not strengthen
        /// it from a guessed radius).
        /// </summary>
        private static float NearnessOf(Vec3 centre, float galeRadius)
        {
            if (!(galeRadius > 0f)) return 0f;   // NaN falls out here too

            float distance = (float)System.Math.Sqrt(centre.X * centre.X + centre.Z * centre.Z);
            if (float.IsNaN(distance)) return 0f;

            float near = 1f - distance / galeRadius;
            return Clamp01(near);
        }

        /// <summary>
        /// Convert ④'s heading (rad, in the mathematical convention where
        /// <c>(cos φ, sin φ)</c> is (X, Z)) into <c>m_targetDirection</c>'s bearing
        /// (degrees, 0 = +Z / 90 = +X).
        /// The convention was taken from <c>EndRenderingImpl</c>'s IL, as class doc 4.
        /// describes.
        /// </summary>
        private static float WindDegreesOf(float headingRadians)
        {
            if (float.IsNaN(headingRadians)) return 0f;

            float degrees = 90f - headingRadians * 57.29578f;
            degrees = degrees % 360f;
            if (degrees < 0f) degrees += 360f;
            return degrees;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// Let go of the weather ④ was holding. The breakdown of how each is put back is
        /// the table in class doc 7.
        ///
        /// It is called from two places, and **every route that lets go of a typhoon passes
        /// through one of those two**:
        ///
        /// - <c>TyphoonController.Forget</c> — not only the normal ending (<c>Stop</c>) but
        ///   also **when the disaster slot is taken away** (<c>LoseSlot</c>). Put the
        ///   restore on the <c>Stop</c> side and, the moment the slot is taken, the typhoon
        ///   alone disappears while the weather stays held
        /// - <see cref="Reset"/> — level unload
        ///
        /// It is idempotent (it returns immediately when <c>_driving</c> is false), so
        /// calling it repeatedly is fine.
        ///
        /// If we are not driving it does nothing — we do not go and zero a player's rain in
        /// a city where no typhoon has ever been raised.
        /// </summary>
        public static void Release()
        {
            if (!_driving) return;
            _driving = false;

            try
            {
                if (Singleton<WeatherManager>.exists)
                {
                    var w = Singleton<WeatherManager>.instance;
                    w.m_targetRain = 0f;
                    w.m_targetCloud = 0f;
                    // ★★ **Write 0 explicitly for fog too.** Back when the value ④ put in
                    //    was 0, "stop writing" was enough, but now we raise it to 0.45
                    //    (FogPeak). Merely stopping would leave it hazy until vanilla
                    //    redraws it (an expected 20,000 steps).
                    w.m_targetFog = 0f;
                    // ★ Do not write to m_forceWeatherOn or m_targetDirection (class doc 7.).
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon weather release failed", e);
                }
            }

            _lastRain = 0f;
            _lastCloud = 0f;
            _lastFog = 0f;
            _lastDirectionDegrees = 0f;
        }

        /// <summary>
        /// **Call immediately before saving** (<c>DisasterPlusSerialization.OnSaveData</c>).
        /// It lowers the weather overrides ④ holds before vanilla writes
        /// <c>WeatherManager+Data</c>. <b>Pass the return value to
        /// <see cref="ReapplyAfterSave"/>.</b>
        ///
        /// ── Why it is needed (settled by measuring the IL for this task) ─────
        ///
        /// <c>WeatherManager+Data.Serialize</c> writes, in order,
        /// <c>m_windDirection / m_targetDirection / m_directionSpeed /
        /// m_currentTemperature / m_targetTemperature / m_temperatureSpeed /
        /// m_currentRain / m_targetRain / m_currentFog / m_targetFog /
        /// m_currentCloud / m_targetCloud / m_forceWeatherOn / m_groundWetness / …</c>
        /// (<c>Deserialize</c> restores them in the same order). In other words
        /// **the four values ④ writes every tick burn straight into the save.**
        ///
        /// Open a save with them burnt in and <c>m_targetRain</c> is restored as 1.0 with
        /// ④ not running. Vanilla only redraws it once <c>m_currentRain == m_targetRain</c>
        /// and it then draws <c>Int32(20000) == 0</c> (§A-4), i.e. roughly 20,000 steps in
        /// expectation — and throughout that time it pours at maximum, and the game itself
        /// starts creating thunderstorms on the rainfall-above-0.8 test.
        /// <c>m_forceWeatherOn</c> burns in by the same route, and
        /// **weather comes back in the environment of a player who has it switched off.**
        ///
        /// ── We do not touch the host storm itself (a conscious decision) ─────
        ///
        /// ④'s host is a <c>ThunderStormAI</c> disaster with <c>SelfTrigger</c>, and that
        /// goes into the save along with the whole of <c>DisasterManager</c>'s buffer. Open
        /// a save made during a typhoon and **a motionless thunderstorm** stays there,
        /// continuing until it uses up <c>m_activeDuration</c>.
        ///
        /// **We do not fix this.** The only way to fix it would be "call
        /// <c>DeactivateNow</c> before saving", and that would mean <b>the act of saving
        /// changes the state of the simulation</b> (the typhoon disappears just because the
        /// player saved). What remains is a legitimate disaster vanilla could have created
        /// itself — a high-intensity thunderstorm — and vanilla's own lifecycle ends it on
        /// schedule and goes all the way to <c>ReleaseDisaster</c>. We lower only the
        /// weather because that side is **a global override that belongs to no disaster**,
        /// and nobody puts it back once its owner is gone. This asymmetry is deliberate and
        /// is written down in design doc §4.2 and the in-game checklist.
        ///
        /// Note that if that host storm is still Active after the load,
        /// <c>ThunderStormAI.SimulationStep</c> rewrites <c>m_targetRain = 1</c> once per
        /// 256 frames (§A-1). **That is correct** — the rain is falling because the storm
        /// is there, and it is no longer something ④ forgot to clear.
        /// </summary>
        /// <returns>Whether we actually lowered anything (whether ④ was driving).</returns>
        public static bool SuspendForSave()
        {
            if (!_driving) return false;

            try
            {
                if (!Singleton<WeatherManager>.exists) return false;

                var w = Singleton<WeatherManager>.instance;
                w.m_targetRain = 0f;
                w.m_targetCloud = 0f;
                // ★★ Lower the fog too. **It is one of the five values that burn into the
                //    save** (the ordering in WeatherManager+Data.Serialize. Class doc 8.).
                w.m_targetFog = 0f;
                // ★ Here we zero m_forceWeatherOn as well (a different decision from
                //   Release). Release leaves it alone to avoid "the rain vanishes the
                //   instant the typhoon leaves", and that is a question of **how it looks on
                //   screen**. It is not a question of what gets burnt into the save.
                w.m_forceWeatherOn = 0f;
                return true;
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not lower its weather override before saving", e);
                }
                return false;
            }
        }

        /// <summary>
        /// Put back the overrides <see cref="SuspendForSave"/> lowered.
        /// **Call it through <c>SimulationManager.AddAction</c>, after the save has
        /// finished** (the same shape as <c>TyphoonFlood.ReapplyAfterSave</c>; put them back
        /// immediately and we would be overwriting again before vanilla writes the arrays,
        /// and the leak comes back).
        ///
        /// **Do not leave it to the next tick's <see cref="Drive"/>.** If the save was made
        /// while paused, the pause guard in <c>TyphoonFeature.OnSimulationTick</c> stops
        /// <see cref="Drive"/>, so the rain stays at 0 until the player unpauses — which
        /// from the player's point of view is "I saved and the typhoon's rain alone
        /// disappeared".
        /// </summary>
        public static void ReapplyAfterSave(bool wasDriving)
        {
            // Do not rewrite if ④ lost the typhoon during the save (_driving has dropped).
            if (!wasDriving || !_driving) return;

            try
            {
                Step();
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not restore its weather override after saving", e);
                }
            }
        }

        /// <summary>
        /// **Always call on level unload.** During unloading the sim thread has already
        /// stopped, so it is fine to touch <c>WeatherManager</c> directly from here
        /// (the same note is on <c>DisasterPlusLoading.OnLevelUnloading</c>).
        /// </summary>
        public static void Reset()
        {
            Release();
            _weatherDisabledByPlayer = false;
            // ★ _errorLogged is not reset (it is a fact about the game build, not per-city
            //    state. Treated the same way as TyphoonReader).
        }
    }
}
