using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// One step of lava. **A pure function that knows nothing about the sign of the slope.**
    ///
    /// The caller (<c>Game/Volcano/VolcanoLava</c>) takes the result of
    /// <c>TerrainManager.SampleDetailHeight(Vector3, out slopeX, out slopeZ)</c> and turns it
    /// into **a unit vector pointing downhill** before passing it in. We keep the
    /// interpretation of the sign out of here because, while §B-6 of the facts document
    /// establishes that the 3-argument overload exists, **it did not go as far as reading
    /// the signs of slopeX / slopeZ**.
    /// Get it wrong and the lava climbs the mountain — without a single exception.
    /// The sign is pinned down in one place on the Game side, where it is also observed at
    /// runtime.
    ///
    /// > **In this task we read the IL and settled the sign** (the measurement is written up
    /// > in <c>VolcanoLava</c>'s class doc). Even so, **we do not bring the sign into this
    /// > type** — this is a type that does nothing but "produce the next point from a
    /// > direction and a step length", and holding no interpretation of the terrain is what
    /// > determines how much of it the tests can pin down.
    ///
    /// **Flat ground, a hollow, or NaN all return "do not advance"** (it pools). Advance
    /// anyway "for now" and the lava sails straight through the hollow and keeps running to
    /// the edge of the map.
    ///
    /// The only randomness used is
    /// <see cref="DisasterPlus.Core.Common.DeterministicRandom"/>.
    /// <c>VanillaRandomizer</c> is **not used** — what is decided here is not a value vanilla
    /// draws but a judgement ⑤ invented (⑤ does not sit in a vanilla disaster slot, so
    /// structurally there is not one vanilla draw to stay in step with).
    /// </summary>
    public static class LavaPath
    {
        /// <summary>The length of one step (m). The terrain's detail cell is 4 m, so this is
        /// 3× that.</summary>
        public const float StepMetres = 12f;

        /// <summary>
        /// Slopes below this count as "flat" and we stop (dimensionless; 1 = 45 degrees).
        /// The gradient returned by the 3-argument <c>SampleDetailHeight</c> is in **metres
        /// per metre**, so this is the slope directly (measured in §B-6).
        /// </summary>
        public const float MinSlope = 0.002f;

        /// <summary>The most steps one flow may take. **We do not create lava that never
        /// stops.**</summary>
        public const int MaxSteps = 512;

        /// <summary>
        /// How far outside the crater rim the lava emission point sits (as a ratio of the
        /// crater's radius).
        ///
        /// ★★ **Do not emit from exactly on the rim.** Since 2026-08-22 the crater has been
        ///   part of the height profile, and the rim is <b>a ridge line (a local maximum)</b>.
        ///   Read <c>SampleDetailHeight</c>'s gradient directly on it and the downhill
        ///   direction **can point into the crater** — the lava runs down into the hollow
        ///   and ends there, pooling at <c>StopFlat</c>.
        ///   Not one exception is raised, so the only symptom is "not a single lava flow
        ///   runs down the mountain".
        /// </summary>
        public const float VentRimClearanceFactor = 1.15f;

        /// <summary>
        /// The absolute floor on the same thing (m). The terrain's raw cell is 16 m, so we
        /// go out 1.5× that to be sure of clearing "the rim cell". For a small crater
        /// (radius 40 m) this bites harder than the ratio does.
        /// </summary>
        public const float VentRimClearanceMetres = 24f;

        /// <summary>
        /// The radius (m) at which lava is emitted. When
        /// <paramref name="craterRadiusMetres"/> cannot be read, it returns one step's worth
        /// (<see cref="StepMetres"/>) — **it does not return 0 and emit from the centre.**
        /// </summary>
        public static float VentRadiusMetres(float craterRadiusMetres)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return StepMetres;

            float byFactor = craterRadiusMetres * VentRimClearanceFactor;
            float byMetres = craterRadiusMetres + VentRimClearanceMetres;
            return byFactor > byMetres ? byFactor : byMetres;
        }

        /// <summary>A flow's width (radius, m) just after it leaves the crater.</summary>
        public const float SpreadBaseMetres = 28f;

        /// <summary>The width (radius, m) it never exceeds however far it flows.</summary>
        public const float SpreadMaxMetres = 78f;

        /// <summary>
        /// The spread (m) that is **never exceeded**, even after the multiplier is applied.
        ///
        /// ★★ This is not a presentation value but <b>the limit the ignition scan can count
        ///   through</b> (2026-08-22). <c>VolcanoLava.Ignite</c> sweeps the
        ///   <c>p ± radius</c> rectangle row-major at every step, so as the radius grows the
        ///   cell count exceeds the per-step cap (<c>MaxBuildingCellsPerStep</c> /
        ///   <c>MaxTreeCellsPerStep</c>) and the scan is **silently cut short** — and then
        ///   you get "buildings under the glowing lava that do not burn".
        ///
        ///   At 96 m:
        ///     the building grid (64 m squares) … spans 192 m, so at most 4×4 = 16 cells (cap 25)
        ///     the tree grid (32 m squares)     … spans 192 m, so at most 7×7 = 49 cells (cap 64)
        ///
        ///   **If you raise this, you must raise both of those caps to match.**
        /// </summary>
        public const float SpreadHardMaxMetres = 96f;

        /// <summary>How much it spreads (m) per kilometre travelled.</summary>
        public const float SpreadPerKilometre = 25f;

        /// <summary>
        /// Takes one step downhill. <paramref name="downhill"/> is **a vector pointing
        /// downhill**; its length does not matter (we normalise inside).
        ///
        /// When it cannot advance it puts <paramref name="current"/> into
        /// <paramref name="next"/> and returns <c>false</c>. **We never do "forward anyway"**
        /// (see the class doc).
        /// </summary>
        public static bool NextPosition(Vec2 current, Vec2 downhill, float stepMetres,
                                        out Vec2 next)
        {
            next = current;

            if (IsBad(current.X) || IsBad(current.Z)) return false;
            if (IsBad(downhill.X) || IsBad(downhill.Z)) return false;
            if (IsBad(stepMetres) || stepMetres <= 0f) return false;

            float length = (float)Math.Sqrt(downhill.X * downhill.X + downhill.Z * downhill.Z);
            if (IsBad(length) || length < MinSlope) return false;

            float inv = 1f / length;
            next = new Vec2(current.X + downhill.X * inv * stepMetres,
                            current.Z + downhill.Z * inv * stepMetres);
            return true;
        }

        /// <summary>
        /// The initial direction of flow number <paramref name="flowIndex"/> (**always unit
        /// length**). Spaced evenly, with a little jitter from
        /// <see cref="DeterministicRandom"/>.
        ///
        /// The jitter is held to <c>±π/(2n)</c> so that a flow cannot swap places with its
        /// neighbour (a swap would spoil the radial arrangement).
        /// **Do not mix in the frame number** — mix it in and the same flow re-draws its
        /// direction every tick.
        /// </summary>
        public static Vec2 InitialDirection(uint seed, int flowIndex, int flowCount)
        {
            int n = flowCount <= 0 ? 1 : flowCount;
            int i = ((flowIndex % n) + n) % n;

            double baseAngle = 2.0 * Math.PI * i / n;
            float jitter = DeterministicRandom.Unit(seed, (uint)i) - 0.5f;
            double angle = baseAngle + jitter * (Math.PI / n);

            return new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        /// <summary>
        /// The current spread (radius, m) from the distance travelled. The further it goes
        /// the wider it gets, but it always levels off.
        /// Bad input falls back to <see cref="SpreadBaseMetres"/> (**we never let NaN
        /// out**).
        /// </summary>
        public static float SpreadRadiusFor(float travelledMetres)
        {
            return SpreadRadiusFor(travelledMetres, 1f);
        }

        /// <summary>
        /// The same as above, but widened by <paramref name="widthFactor"/>
        /// (<c>LavaVolume.WidthFactor</c>; 2026-08-22, at the owner's request: "I'd like the
        /// lava flows a bit wider, please — scaled to the eruption").
        ///
        /// ★★ <b>This one function decides both the look and the damage.</b>
        ///   The width of <c>VolcanoLavaFx</c>'s ribbon and the ignition radius of
        ///   <c>VolcanoLava.Ignite</c> both come from here. **Never widen only one of them**
        ///   — lava that is drawn but does not set fire to the buildings under it is a lie.
        ///
        /// The result is always at most <see cref="SpreadHardMaxMetres"/> (for the reason in
        /// that field's doc).
        /// </summary>
        public static float SpreadRadiusFor(float travelledMetres, float widthFactor)
        {
            float f = IsBad(widthFactor) || widthFactor <= 0f ? 1f : widthFactor;

            if (IsBad(travelledMetres) || travelledMetres < 0f)
            {
                return Cap(SpreadBaseMetres * f);
            }

            float r = SpreadBaseMetres + travelledMetres / 1000f * SpreadPerKilometre;
            if (r > SpreadMaxMetres) r = SpreadMaxMetres;
            return Cap(r * f);
        }

        private static float Cap(float r)
        {
            if (r < 0f) return 0f;
            return r > SpreadHardMaxMetres ? SpreadHardMaxMetres : r;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
