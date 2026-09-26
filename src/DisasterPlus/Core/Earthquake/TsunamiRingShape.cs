using System;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>The "shape" of a tsunami rising in concentric rings from the hypocentre.</b>
    /// The layer that does not touch the engine.
    ///
    /// ── What was rebuilt (2026-08-31, the owner's instruction) ────────────────
    ///
    /// &gt; If you just use the DLC one as it is, there is no point going to the trouble of
    /// &gt; doing it in a mod. Take the mechanism apart, apply it, and make a tsunami that
    /// &gt; forms in concentric rings from around the hypocentre.
    ///
    /// The <c>TsunamiSource</c> up to that point produced a <c>TYPE_IMPACT</c> water wave,
    /// i.e. <b>a virtual hill placed underwater</b>. A hill only displaces water; it does
    /// not <b>make</b> any. All it can produce is a net-zero external force — a dipole, not
    /// a wall of water. Measured offline (same shelf, 174 m water depth, same shoreline,
    /// 2026-08-31):
    ///
    /// <list type="bullet">
    /// <item>The DLC tsunami (intensity 100) … <b>84.8 m</b> at the shoreline (barely
    ///   decaying from 88.8 m at the hypocentre)</item>
    /// <item>Our hill (drive 3857) … <b>19.3 m</b> at the shoreline (down to a quarter of
    ///   the 72.7 m at the hypocentre)</item>
    /// </list>
    ///
    /// The difference was not the amplitude but <b>the mechanism</b>. The DLC rewrites the
    /// sea level of the outermost cells, and those are a Dirichlet boundary, so
    /// <b>water wells up</b>. No amount of making the hill bigger can imitate that.
    ///
    /// ★★ **So use a different tool the game already has.** <c>WaterSource</c>'s
    ///   <c>TYPE_NATURAL</c> (the springs that feed the map's rivers) is a device that
    ///   <b>"fills or drains a given circle to a given water level"</b>, and you can put it
    ///   where you like — <b>the boundary condition brought over to the hypocentre</b>.
    ///   The waveform is the DLC's, used as it stands; only where it is placed moves. That
    ///   is the substance of "take the mechanism apart and apply it".
    ///
    /// ── The waveform (identical to <c>WaterWave.GetSeaLevel</c> IL_0000-00B6) ──────
    ///
    /// <code>
    /// amp = delta * (65536 - t) / 65536                  // slow decay
    /// arg = amp * (1 - cos(2*pi * t / T))                // an envelope 0 -> 2amp -> 0
    /// off = arg * sin(2*pi * 1.5 * t / T) / 2            // 1.5 cycles
    /// level = seaLevel - off
    /// </code>
    ///
    /// t advances by +64 per water step, and <c>T = 16384</c> = **256 water steps**.
    /// The crest of the push wave is at <c>t = T/2</c>, at <c>seaLevel + amp</c>.
    /// A drawback comes either side of it — <b>draw → push → draw</b>.
    /// </summary>
    public static class TsunamiRingShape
    {
        /// <summary>Vanilla's <c>m_duration</c>. <c>256 &lt;&lt; 6</c>.</summary>
        public const int DurationTicks = 16384;

        /// <summary>How far the clock advances in one water step (<c>m_currentTime</c> goes
        /// +64).</summary>
        public const int TicksPerWaterStep = 64;

        /// <summary>How many water steps the waveform lasts. 256 = 16,384 sim frames ≈ 4.5
        /// real minutes.</summary>
        public const int WaterSteps = DurationTicks / TicksPerWaterStep;

        /// <summary><c>m_target</c> is a ushort. A water level above 1023.98 m cannot be
        /// written.</summary>
        public const int MaxLevelUnits = 65535;

        /// <summary>
        /// The circle's radius (m). Unlike a building, <c>TYPE_NATURAL</c> has <b>no upper
        /// bound</b> (IL_1D0D-1D21: <c>sqrt(rate)*0.4 + 10</c>, with the clamp to 10..50
        /// applied only to types 2 and 3).
        /// </summary>
        public static float RadiusMetresForRate(long rate)
        {
            if (rate <= 0L) return 0f;
            return (float)Math.Sqrt(rate) * 0.4f + 10f;
        }

        /// <summary>
        /// The flow rate needed to produce that radius. <b>Radius and rate cannot be
        /// separated</b> — the radius goes as the square root of the rate, so a wide
        /// hypocentre always means a large flow.
        /// What decides the height is not the rate but <c>m_target</c>.
        /// </summary>
        public static long RateForRadiusMetres(float radiusMetres)
        {
            if (radiusMetres <= 10f) return 0L;
            double q = (radiusMetres - 10.0) / 0.4;
            return (long)(q * q);
        }

        /// <summary>
        /// Vanilla's <c>m_delta</c> (1/64 m). <c>round(64 * 64 * intensity / 55)</c>.
        /// 7447 at intensity 100 (116.4 m), 18991 at 255 (296.7 m).
        /// </summary>
        public static int VanillaDeltaUnits(int intensity)
        {
            if (intensity < 0) intensity = 0;
            return (int)Math.Round(64.0 * 64.0 * intensity / 55.0);
        }

        /// <summary>
        /// The offset from normal sea level (1/64 m). <b>Positive is a push wave, negative
        /// a drawback.</b>
        ///
        /// Vanilla's formula is <c>level = original - off</c>, so what is returned here is
        /// that <c>-off</c> as it stands (so the caller is never handed a sign to get wrong).
        /// </summary>
        /// <param name="elapsedTicks">Elapsed time (one water step = 64).</param>
        /// <param name="deltaUnits">The source of the amplitude (equivalent to <c>m_delta</c>,
        /// 1/64 m).</param>
        /// <param name="durationTicks">
        /// The waveform's length. **It is both the period and the cut-off.**
        ///
        /// ★★ <b>This must not be a constant.</b> (2026-08-31, discovered by cross-checking
        ///   the IL.) The game builds the period from it too, as
        ///   <c>den = m_duration &gt;&gt; 6</c> (IL_0089), and <c>m_duration</c> is a
        ///   <b>value</b> decided per disaster. Pinning this to <c>DurationTicks</c> meant
        ///   that when the caller thought it was running 768 water steps, <b>it actually
        ///   finished in 256</b> — what came out was a wave equivalent to 256 steps, not the
        ///   768 steps measured offline (67 m at the shoreline).
        ///
        /// ★ The decay term <c>(65536 - t)/65536</c> works on <b>absolute time</b> (it is
        ///   not divided by the period). So the longer the waveform, the more the amplitude
        ///   falls off in its second half. It reaches 0 at 65536 ticks = 1024 water steps.
        /// </param>
        public static int LevelOffsetUnits(int elapsedTicks, int deltaUnits, int durationTicks)
        {
            if (durationTicks <= 0) return 0;
            if (elapsedTicks <= 0 || elapsedTicks >= durationTicks) return 0;
            if (deltaUnits <= 0) return 0;

            double amp = (double)deltaUnits * (65536 - elapsedTicks) / 65536.0;
            double phase = 2.0 * Math.PI * elapsedTicks / durationTicks;
            double arg = amp - amp * Math.Cos(phase);
            double off = arg * Math.Sin(1.5 * phase) / 2.0;

            return -(int)off;
        }

        /// <summary>
        /// Holds back the depth of the drawback. **The sea must not be emptied.**
        ///
        /// ★★ Measured offline (2026-08-31): without this, the water column at the
        ///   hypocentre <b>drained 100% and exposed the seabed</b> (intensity 255, radius
        ///   1280 m, 20 water steps). Drawback does happen in reality, but a seabed in
        ///   plain view is broken as a picture. Keep it to
        ///   <paramref name="maxFraction"/> of the water depth.
        /// </summary>
        /// <param name="offsetUnits">The raw <see cref="LevelOffsetUnits"/>.</param>
        /// <param name="depthUnits">The water depth at the hypocentre (1/64 m).</param>
        public static int ClampDraw(int offsetUnits, int depthUnits, float maxFraction)
        {
            if (offsetUnits >= 0) return offsetUnits;
            if (depthUnits <= 0) return 0;

            int limit = (int)(depthUnits * maxFraction);
            if (limit < 0) limit = 0;
            return offsetUnits < -limit ? -limit : offsetUnits;
        }
    }
}
