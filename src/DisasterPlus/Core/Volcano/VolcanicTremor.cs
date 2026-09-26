using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **Volcanic earthquakes.** The synthesis of that shaking which goes on while the magma
    /// moves beneath the crater.
    /// Pure engine-free functions only; no Unity types, no game types and no
    /// <c>System.Random</c> appear here.
    ///
    /// ── Why we do not simply trigger ②'s earthquake (2026-08-22, the owner's request) ──────
    ///
    /// > Please also make volcanic earthquakes occur at the same time as the eruption.
    ///
    /// **Triggering vanilla's <c>EarthquakeAI</c> would be wrong.** That carves fault cracks
    /// into the map — which means another writer barging in on the very terrain cells ⑤ is
    /// writing a mountain into (it breaks the same way as the flat trench in design doc
    /// §1.2). And volcanic earthquakes are not fault earthquakes to begin with.
    ///
    /// **Nor do we use ②'s synthetic seismogram (<c>SeismogramModel</c>) as it stands.**
    /// That is a model of **a single fault rupture**: the P wave arrives, the S wave
    /// arrives, the coda decays and it is over. Volcanic earthquakes are not like that:
    ///
    ///   1. **Swarms** — tens to hundreds of small earthquakes. Each one is short, and the
    ///      big ones are rare
    ///   2. **Harmonic tremor** — a low shaking of uniform period that continues
    ///      <b>without a break</b> while the magma is moving
    ///   3. **It starts before the eruption, peaks during it, and trails off afterwards**
    ///
    /// Those three, and nothing else, are what this builds. **It looks at not one of ②'s
    /// settings (<c>eqSeismogram</c> / <c>eqShakeBoost</c>)** — those are off by default
    /// because they replace the camera shake of vanilla's earthquakes, whereas ⑤'s volcano
    /// is <b>⑤'s own phenomenon, which the player triggered themselves</b>.
    /// Turn every part of ② off and the volcano still shakes
    /// (<c>Game/Volcano/VolcanoTremorShake</c>).
    ///
    /// ── How the swarm is built (★ it holds no state) ───────────────────────────
    ///
    /// Time is cut into slots of <see cref="SlotSeconds"/>, with **exactly one earthquake
    /// placed per slot**.
    /// "Does it happen or not" is not decided by the activity level —
    /// decide it that way and <b>an earthquake already sounding in the past disappears the
    /// instant the activity changes</b> (<see cref="DisplacementAt"/> is a closed-form
    /// expression in t, so it re-derives the past slots every time).
    /// Instead the activity scales <b>the size</b>. When things are quiet you get "barely
    /// perceptible earthquakes happening incessantly", which is exactly what a volcanic
    /// swarm looks like.
    ///
    /// The size distribution is <c>u⁴</c> (see <see cref="MagnitudeCurve"/>) — **small ones
    /// overwhelmingly common, large ones rare**. It goes the same way as the real
    /// frequency-magnitude relation (Gutenberg–Richter), and **what is built here is the
    /// shape of the distribution only** (not a magnitude).
    /// Measured (and pinned by tests): 60% fall on the "barely perceptible" side and only a
    /// little over 10% on the large side.
    ///
    /// ── Do not alias ───────────────────────────────────────
    ///
    /// The camera shake is evaluated **per rendered frame** (60 fps ⇒ Nyquist 30 Hz).
    /// The fastest component is the earthquake carrier <see cref="EventFastHz"/> = 3.2 Hz,
    /// which gets 18 points per cycle. **Do not add any component faster than that.**
    ///
    /// ── The same volcano shakes the same way ───────────────────────────────
    ///
    /// It uses <see cref="DeterministicRandom"/> alone and **never mixes the frame number
    /// into the seed**. <see cref="DisplacementAt"/> is a closed-form expression in t, so
    /// even if frames are skipped the value is the same as if it had been evaluated on that
    /// frame.
    /// </summary>
    public static class VolcanicTremor
    {
        /// <summary>The swarm's slot (seconds). **Exactly one earthquake per slot.**</summary>
        public const float SlotSeconds = 1.6f;

        /// <summary>How many slots back an earthquake still contributes to the present
        /// shaking.</summary>
        public const int TailSlots = 3;

        /// <summary>An earthquake's rise time (seconds). **Not 0** (a discontinuity shows on
        /// screen).</summary>
        public const float EventRiseSeconds = 0.10f;

        /// <summary>The time constant of an earthquake's decay (seconds).</summary>
        public const float EventDecaySeconds = 0.55f;

        /// <summary>An earthquake's carrier (Hz). **This is the fastest component here.**</summary>
        public const float EventFastHz = 3.2f;

        /// <summary>An earthquake's lower carrier (Hz).</summary>
        public const float EventSlowHz = 1.1f;

        /// <summary>The harmonic tremor's carrier (Hz). The low shaking that continues without
        /// a break.</summary>
        public const float TremorHz = 1.8f;

        /// <summary>The period over which the tremor's amplitude swells (Hz). **The
        /// unsteadiness of the magma flow.**</summary>
        public const float TremorSwellHz = 0.11f;

        /// <summary>The same again, a second one (an incommensurate value, so it does not
        /// sound repetitive).</summary>
        public const float TremorSwell2Hz = 0.037f;

        /// <summary>
        /// The tremor's amplitude (at activity 1). **Keep it below the swarm's peaks** —
        /// larger and the seismogram is buried under a single sinusoid, so it no longer
        /// looks as though earthquakes are happening
        /// (lowered from 0.34 to 0.26 against <c>docs/images/volcano/tremor-waveform.png</c>).
        /// </summary>
        public const float TremorAmplitude = 0.26f;

        /// <summary>The floor on the tremor's amplitude as a fraction (it swells but never
        /// cuts out completely).</summary>
        public const float TremorFloor = 0.45f;

        /// <summary>The size of the smallest earthquake (at activity 1).</summary>
        public const float MinEventMagnitude = 0.06f;

        /// <summary>The size of the largest earthquake (at activity 1).</summary>
        public const float MaxEventMagnitude = 1.0f;

        /// <summary>The floor on the activity during uplift (rising magma). **It is shaking
        /// before the eruption.**</summary>
        public const float BuildUpFloor = 0.18f;

        /// <summary>The activity at the point the uplift finishes.</summary>
        public const float BuildUpCeiling = 0.62f;

        /// <summary>The floor on the activity during the eruption (at eruption strength
        /// 0).</summary>
        public const float EruptionFloor = 0.55f;

        /// <summary>The activity remaining after the eruption until everything has cooled (the
        /// afterglow).</summary>
        public const float AfterglowUnit = 0.42f;

        /// <summary>Salt (for offsetting a slot's phase).</summary>
        private const uint OffsetSalt = 0x0F5E7u;

        /// <summary>Salt (for a slot's size).</summary>
        private const uint MagnitudeSalt = 0x4D41475u;

        /// <summary>Salt (for the tremor's phase).</summary>
        private const uint TremorSalt = 0x54524Du;

        /// <summary>
        /// The current activity <c>[0,1]</c>. **A quantity ⑤ decided**, not a real observable.
        ///
        /// <paramref name="upliftProgressUnit"/> is the uplift's progress (corresponding to
        /// the magma rising), <paramref name="eruptionUnit"/> is the eruption strength, and
        /// <paramref name="coolUnit"/> is how far the lava has cooled (1 = cooled right
        /// through).
        ///
        /// If <paramref name="erupting"/> is false and <paramref name="afterEruption"/> is
        /// false too, then it "has not erupted yet" and the uplift-side formula is used.
        /// </summary>
        public static float ActivityUnit(float upliftProgressUnit, bool erupting,
                                         float eruptionUnit, bool afterEruption, float coolUnit)
        {
            if (erupting)
            {
                return Clamp01(EruptionFloor
                               + (1f - EruptionFloor) * Clamp01(eruptionUnit));
            }

            if (afterEruption)
            {
                // From the end of the eruption until everything has cooled, the afterglow
                // fades away.
                return Clamp01(AfterglowUnit * (1f - Clamp01(coolUnit)));
            }

            // Uplift (rising magma). **Shaking before the eruption** is what a volcanic swarm is.
            return Clamp01(BuildUpFloor
                           + (BuildUpCeiling - BuildUpFloor) * Clamp01(upliftProgressUnit));
        }

        /// <summary>The number of the slot the time <paramref name="clockSeconds"/> falls
        /// in.</summary>
        public static int SlotAt(float clockSeconds)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0;
            return (int)(clockSeconds / SlotSeconds);
        }

        /// <summary>
        /// The size <c>[0,1]</c> of slot <paramref name="slot"/>'s earthquake.
        /// **Proportional to the activity** (a slot always has exactly one; see the class doc).
        /// </summary>
        public static float MagnitudeUnit(uint seed, int slot, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (slot < 0) return 0f;

            float u = DeterministicRandom.Unit(seed, unchecked((uint)slot ^ MagnitudeSalt));
            float shaped = MagnitudeCurve(u);
            return a * (MinEventMagnitude
                        + (MaxEventMagnitude - MinEventMagnitude) * shaped);
        }

        /// <summary>
        /// The shape of the frequency-magnitude relation (<c>u⁴</c>). **Small ones
        /// overwhelmingly common, large ones rare.**
        /// It is not a magnitude as such (see the class doc).
        /// </summary>
        public static float MagnitudeCurve(float u)
        {
            float x = Clamp01(u);
            float x2 = x * x;
            return x2 * x2;
        }

        /// <summary>When within the slot it occurs (seconds, <c>[0, SlotSeconds)</c>).</summary>
        public static float OffsetInSlot(uint seed, int slot)
        {
            if (slot < 0) return 0f;
            return SlotSeconds * DeterministicRandom.Unit(seed,
                                                          unchecked((uint)slot ^ OffsetSalt));
        }

        /// <summary>
        /// The displacement <c>[-1,1]</c> of the (continuous) harmonic tremor. **Having no
        /// break in it** is what this actually is; it sounds on independently of the
        /// individual earthquakes in the swarm.
        /// </summary>
        public static float TremorAt(uint seed, float clockSeconds, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float phase = 6.2831853f * DeterministicRandom.Unit(seed, TremorSalt);

            float swell = 0.5f * (1f + (float)Math.Sin(6.2831853 * TremorSwellHz * clockSeconds))
                        * 0.6f
                        + 0.5f * (1f + (float)Math.Sin(6.2831853 * TremorSwell2Hz * clockSeconds
                                                        + 1.7)) * 0.4f;
            float envelope = TremorFloor + (1f - TremorFloor) * swell;

            float carrier = (float)Math.Sin(6.2831853 * TremorHz * clockSeconds + phase);
            return TremorAmplitude * a * envelope * carrier;
        }

        /// <summary>
        /// The waveform of one earthquake (<c>[-1,1]</c> multiplied by
        /// <paramref name="magnitude"/>).
        /// It rises and then decays exponentially. 0 if <paramref name="ageSeconds"/> is
        /// negative.
        /// </summary>
        public static float EventAt(float ageSeconds, float magnitude)
        {
            if (IsBad(ageSeconds) || ageSeconds < 0f) return 0f;
            if (IsBad(magnitude) || magnitude <= 0f) return 0f;

            float rise = ageSeconds < EventRiseSeconds
                ? ageSeconds / EventRiseSeconds
                : 1f;
            float decay = (float)Math.Exp(-(ageSeconds - EventRiseSeconds < 0f
                                            ? 0f : ageSeconds - EventRiseSeconds)
                                          / EventDecaySeconds);

            float carrier = 0.72f * (float)Math.Sin(6.2831853 * EventFastHz * ageSeconds)
                          + 0.28f * (float)Math.Sin(6.2831853 * EventSlowHz * ageSeconds);

            return magnitude * rise * decay * carrier;
        }

        /// <summary>
        /// The ground motion <c>[-1,1]</c> directly beneath the crater (the tremor plus the
        /// swarm over the last <see cref="TailSlots"/> slots).
        /// **A closed-form expression in t, holding not one piece of state.**
        /// </summary>
        public static float DisplacementAt(uint seed, float clockSeconds, float activityUnit)
        {
            float a = Clamp01(activityUnit);
            if (!(a > 0f)) return 0f;
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float v = TremorAt(seed, clockSeconds, a);

            int slot = SlotAt(clockSeconds);
            for (int k = 0; k <= TailSlots; k++)
            {
                int s = slot - k;
                if (s < 0) break;

                float start = s * SlotSeconds + OffsetInSlot(seed, s);
                float age = clockSeconds - start;
                if (age < 0f) continue;

                v += EventAt(age, MagnitudeUnit(seed, s, a));
            }

            if (v > 1f) return 1f;
            if (v < -1f) return -1f;
            return v;
        }

        /// <summary>
        /// The attenuation with distance <c>[0,1]</c>. <paramref name="distanceMetres"/> is
        /// the distance from the crater and <paramref name="reachMetres"/> is the distance
        /// out to which it can be felt.
        /// Outside <paramref name="reachMetres"/> it is exactly 0 — **the far side of the
        /// city does not shake.**
        /// </summary>
        public static float AttenuationAt(float distanceMetres, float reachMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return 0f;
            if (IsBad(reachMetres) || reachMetres <= 0f) return 0f;
            if (distanceMetres >= reachMetres) return 0f;

            // 1 / (1 + (d/ref)^2), cut by a window so that it comes to exactly 0 at reach.
            float x = distanceMetres / reachMetres;
            float near = 1f / (1f + 9f * x * x);
            float window = 1f - x * x;
            float v = near * window;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
