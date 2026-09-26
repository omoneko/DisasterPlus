using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The relief of a mountain's flanks. This type multiplies the **axisymmetric cone**
    /// returned by <see cref="VolcanoShape.ProfileAt"/> by azimuth-dependent relief, breaking
    /// up the "cone turned on a lathe" look.
    ///
    /// ── Why not just "add noise" ─────────────────────────
    ///
    /// Add undirected noise to the height and it looks less like a mountain than like **a
    /// sandstorm**. Three things determine the surface of a real stratovolcano, in this order
    /// of priority.
    ///
    ///   1. **Radial gullies (barrancos)** — channels running down the slope from top to
    ///      bottom, spaced almost evenly around the azimuth, **shallow near the summit and
    ///      deepening towards the foot**. This is the look of a stratovolcano itself
    ///   1b. **Rills** — narrow, shallow, short channels cutting the ridges between the main
    ///      gullies. They sit in a band three harmonics above the main gullies and
    ///      **exist only towards the foot** (because of the grid floor described below)
    ///   2. **A non-circular foot** — low-frequency azimuthal variation, so the footprint
    ///      stops being a perfect circle
    ///   3. **General roughness** — four octaves of undulation with wavelengths of 64-380 m
    ///   4. **Asymmetry** — one side's foot is longer or steeper (carried by the first
    ///      azimuthal harmonic)
    ///
    /// ── Gullies are a hierarchy, not a single level (2026-08-22, the owner's observation) ──
    ///
    /// > For the fine detail of each type of volcano too, I'd like more natural-looking
    /// > relief (at the moment there are only the thick gully lines — please make finer
    /// > gullies as well)
    ///
    /// With only the main gullies (<see cref="GullyCount"/> of them), the ridges are left
    /// correspondingly broad and flat (unavoidable, given that we make the gullies at the
    /// zero crossings). On a real stratovolcano those ridges are cut by **finer channels**,
    /// arranged in a hierarchy that is **sparser upstream and denser downstream**.
    /// So we stack the same mechanism (an azimuthal band × zero crossings) **once more, at a
    /// higher harmonic**. The rills are made shallow inside the main gullies
    /// (<see cref="RillRidgeFloor"/>) and **deepest on top of the ridges** — that is what
    /// "cutting between the main gullies" actually is.
    ///
    /// ── Two hard constraints, upheld structurally by "multiplication only" ───────────
    ///
    /// **It never exceeds the radius R by a single millimetre.** Raise ground the
    /// preparation (destruction) has not reached and the roads pull the cells back to the
    /// road's y via <c>NetSegment.TerrainUpdated</c>, leaving flat trenches inside the
    /// mountain (design document §1.2). So the azimuthal modulation
    /// **only ever works in the direction of shrinking the effective radius**:
    ///
    /// <code>
    /// Reff(θ) = R × (1 − shrink(θ))     shrink(θ) ∈ [0, shrinkMax]   ⇒  Reff ≤ R
    /// </code>
    ///
    /// **It never exceeds the final height H by a single millimetre.** <c>HeightFor</c>,
    /// <c>HeadroomMetres</c>, <c>HeightWasLimitedByCeiling</c> and the 1024 m raw ceiling
    /// (§C-10) all reason in terms of H. So the relief
    /// **only ever works in the direction of carving away**:
    ///
    /// > ★ The H the caller (<c>VolcanoCrater.ProfileAt</c>) passes in is
    /// > **a virtual summit, rebuilt so that the crater's rim reaches the mountain's
    /// > height**. This type's promise is "never exceed the H you were given", and that does
    /// > not change one bit — the virtual summit is always cut off by the crater's ceiling,
    /// > so not a single cell of it is ever written to the terrain (see that class's doc).
    ///
    /// <code>
    /// profile = VolcanoShape.ProfileAt(form, d, Reff(θ), H) × carve(θ, d)
    ///                                                         carve ∈ [0, 1]
    /// </code>
    ///
    /// <c>VolcanoShape.ProfileAt</c> always returns H at d=0 and less than H everywhere else,
    /// so as long as <c>carve ≤ 1</c> the result is always at most H. **Do not bring in a
    /// single addition** — the moment you do, both of the above drop to "probably fine".
    /// Carving only is physically right too: the cone is the envelope of deposition, and the
    /// gullies are what erosion has cut out of it.
    ///
    /// ── A strength of 0 must be "exactly identical to today" ────────────
    ///
    /// When <see cref="StrengthUnit"/> is 0, this type **returns
    /// <c>VolcanoShape.ProfileAt</c> unchanged** (an early exit on a single branch). Someone
    /// who sets the setting to 0 gets **today's output itself**, not "a mountain with a
    /// little relief".
    ///
    /// ── Randomness ────────────────────────────────────────────
    ///
    /// Only <see cref="DeterministicRandom"/> is used. <c>VanillaRandomizer</c> is
    /// **not used** — that type exists purely to read ahead the values vanilla draws, and ⑤
    /// does not sit in a vanilla disaster slot, so there is not one draw to stay in step
    /// with. Neither <c>System.Random</c> nor <c>Mathf.PerlinNoise</c> is used either (Core
    /// is engine-free and has to give the same values on net35 and net8.0).
    ///
    /// ── The 16 m grid ───────────────────────────────────────
    ///
    /// <c>RawHeights</c>' cells are 16 m. **There is a floor on "make it finer".**
    /// There are two floors, and both were decided from the **extremum density** (how often
    /// the slope's direction reverses per cell advanced) counted by the
    /// <c>## grid limit</c> section of <c>tools/VolcanoPreview</c>. It is a measure that
    /// gives 2 per wavelength on a smooth slope and 1 per cell (= 0.5) for a chequerboard.
    ///
    ///   - **Radially** (value noise): <see cref="MinWavelengthMetres"/> = **64 m (4 cells)**.
    ///     Measured, 64 m gives an extremum density of 0.19, 48 m (3 cells) gives 0.28 and
    ///     32 m (2 cells) gives 0.42 — and since 0.5 is a chequerboard exactly, **3 cells is
    ///     no longer relief at all.**
    ///     4 cells is the finest grid on which interpolation still involves 4 points, and
    ///     that is the honest floor.
    ///   - **Azimuthally**: <see cref="MinAzimuthWavelengthMetres"/> = **96 m (6 cells)**.
    ///     The azimuthal wavelength is the circumference ÷ the harmonic, so **it gets shorter
    ///     the nearer the summit** — unlike the radial case it varies with position, so it
    ///     acts not as a floor but as a fade that "removes it once it goes below" (see
    ///     <see cref="ProfileAt"/>).
    ///     It is 6 cells because one wave in the circumferential direction becomes a
    ///     staircase running diagonally across the 16 m grid, and at 4 cells the steps of
    ///     that staircase are as big as the wave itself (confirmed by measurement).
    ///
    /// ★★ These two are also why the rills **only appear towards the foot**.
    ///   Running them in a band three harmonics above the main gullies, on a stratovolcano
    ///   (R = 1200 m) the azimuthal wavelength falls below 96 m inside 0.24R and they vanish,
    ///   reaching full depth only beyond 0.36R.
    ///   **This is not a limitation; it matches how real volcanoes look (sparser
    ///   upstream).**
    ///
    /// ── Cost ────────────────────────────────────────────
    ///
    /// Zero trigonometric calls per cell. The azimuthal harmonics cos(kθ) / sin(kθ) are built
    /// from powers of the unit complex number (ux + i·uz) (the loop in
    /// <see cref="ProfileAt"/>), so nothing but multiplications and additions appear.
    /// Even so it is around 300 flops per cell, so the caller
    /// (<c>Game/Volcano/VolcanoUplift</c>) **evaluates every cell exactly once at the start of
    /// the uplift and bakes the result into an array**. This is not a type to call every tick.
    /// </summary>
    public sealed class VolcanoRelief
    {
        /// <summary>The cap on the strength taken from the settings. 1 is the per-form default
        /// itself.</summary>
        public const float MaxStrengthUnit = 1.5f;

        /// <summary>
        /// The floor on the finest radial component's wavelength (m). **4 cells on the 16 m
        /// grid.** The basis for lowering it from 100 m (a little over 6 cells) is the
        /// measured extremum density (see "The 16 m grid" in the class doc).
        /// </summary>
        public const float MinWavelengthMetres = 64f;

        /// <summary>
        /// The shortest azimuthal wavelength at which a gully holds up (m). **The azimuthal
        /// wavelength gets shorter the nearer the summit** (the circumference 2πd divided by
        /// the number of gullies), so without respecting this the gullies bite into the 16 m
        /// grid around the summit and give a chequerboard rather than relief.
        /// 6 cells.
        /// </summary>
        public const float MinAzimuthWavelengthMetres = 96f;

        /// <summary>The number of azimuthal harmonics used for the foot's outline
        /// (k = 1..4). The first carries the asymmetry.</summary>
        private const int ShapeHarmonics = 4;

        /// <summary>The number of azimuthal harmonics used for the gullies
        /// (k = n-2 .. n+2). The sidebands break up the even spacing.</summary>
        private const int GullyHarmonics = 5;

        /// <summary>The number of azimuthal harmonics used for the rills. The same 5-wide band
        /// as the main gullies.</summary>
        private const int RillHarmonics = 5;

        /// <summary>
        /// <b>How many harmonics above</b> the main gullies the rills sit. **A difference, not
        /// a multiplier.**
        ///
        /// ── ★★ This was reverted once in the review of 2026-08-22 ────────────
        ///
        /// It was originally "2.2× the main gullies" (40 of them on a stratovolcano).
        /// **In the on-hardware images, the rills at the foot looked like dotted lines,
        /// every one of them.** After the revert we measured again:
        ///
        /// | Rill count | Arc width at half depth (0.85R) | Appearance |
        /// |---|---|---|
        /// | 40 (2.2×)  | 1.8 cells | **dotted. Unusable** |
        /// | 28 (+5)    | 4.0 cells | 2-3 bands still come out dotted |
        /// | **24 (+3)** | **4.7 cells** | **solid. As continuous as the main gullies** (shipped) |
        ///
        /// **What mattered was neither the width nor the depth but the count.** Whether we
        /// moved the width (0.55→0.95), the depth (0.25→0.80) or the suppression on the
        /// ridges, the continuity of any single channel did not budge from about 0.83
        /// (that is what the measurement below is about).
        /// What did change was **the total number of channels on screen**, and the more there
        /// are, the more the grid's inherent property that "every channel breaks up 15% of the
        /// time" catches the eye.
        /// </summary>
        private const int RillHarmonicOffset = 3;

        /// <summary>The rills' depth (as a ratio of the main gullies'). **Shallower than the
        /// main gullies** is the whole point of the hierarchy.</summary>
        private const float RillDepthRatio = 0.60f;

        /// <summary>
        /// The rills' width (the threshold on the azimuthal series' <c>|A|</c>). Taken
        /// **wider** than the main gullies'.
        ///
        /// ★ The width that matters is not the full width of <c>|A| &lt; W</c> but
        ///   <b>the width at half depth</b>, <c>2 d·asin(W/2) / n₂</c>. Estimate from the full
        ///   width and it looks nearly twice as wide, which is how you make the mistake of
        ///   saying "there are 3 cells" when there are actually only 1.6
        ///   (the first version did exactly that).
        ///   At the shipped values (n₂ = 12, W = 0.85) the half-depth width at 0.85R is
        ///   **4.7 cells**.
        /// </summary>
        private const float RillChannelWidth = 0.85f;

        /// <summary>
        /// The minimum flank needed before we emit rills at all (as a ratio of the effective
        /// radius).
        ///
        /// ★ Because of the azimuthal floor (<see cref="MinAzimuthWavelengthMetres"/>), the
        ///   smaller the mountain the narrower the outer ring the rills can live in. **Too
        ///   narrow a ring reads not as channels but as "a ring of dimples around the
        ///   foot"**, so unless we can have the outer 30% of the flank we emit <b>none at
        ///   all</b>.
        ///   ⑤ is a feature where the player chooses the size, so it must not fall apart on a
        ///   mountain they made small.
        /// </summary>
        private const float RillMinFlankFraction = 0.70f;

        /// <summary>The distance from the summit at which the rills start (as a ratio of the
        /// effective radius). Further out than the main gullies.</summary>
        private const float RillStartFraction = 0.34f;

        /// <summary>The distance over which the rills reach full depth (same units).</summary>
        private const float RillRampFraction = 0.26f;

        /// <summary>
        /// The rills' depth when at the bottom of a main gully (as a ratio, taking the ridge
        /// tops as 1).
        /// **Never 0** — if the rills vanished only inside the main gullies, an unnatural band
        /// would be left there.
        /// This ratio is the one and only expression that produces "the rills cut the ridges
        /// between the main gullies".
        /// </summary>
        private const float RillRidgeFloor = 0.30f;

        /// <summary>The minimum distance from the summit at which the gullies start (as a
        /// ratio of the effective radius).</summary>
        private const float GullyStartFraction = 0.16f;

        /// <summary>
        /// The spread over which each gully's start distance is offset (same units).
        /// **Set it to 0 and every gully converges on a single point at the summit, giving a
        /// radial stripe pattern rather than a mountain.**
        /// </summary>
        private const float GullyStartSpread = 0.24f;

        /// <summary>The distance from starting to reaching full depth (same units). Deep at
        /// the foot.</summary>
        private const float GullyRampFraction = 0.30f;

        /// <summary>
        /// The gullies' width. The **zero crossings** of the azimuthal series A(θ) are the
        /// gully floors, and anywhere |A| exceeds this value is outside the gully (on a
        /// ridge).
        ///
        /// Use a sine directly as the depth and you get corrugated iron (half the flank
        /// becomes gully). Use the zero crossings and each gully always reaches full depth
        /// while **the ridges stay broad and flat** — which is how a stratovolcano looks.
        /// The width is set by A's local gradient, so gullies come out naturally wider and
        /// narrower. The number of gullies is the number of zero crossings, that is
        /// **twice the central harmonic** (see <see cref="GullyCount"/>).
        /// </summary>
        private const float ChannelWidth = 0.30f;

        /// <summary>
        /// How much the gullies meander (radians). **A dead straight ray looks artificial.**
        /// It only rotates a unit vector by a small angle, so no trigonometry is needed.
        /// </summary>
        private const float WarpRadians = 0.13f;

        /// <summary>The distance at which the roughness starts (same units). We leave the
        /// summit alone, since the crater is carved there.</summary>
        private const float RoughStartFraction = 0.04f;

        /// <summary>The distance at which the roughness reaches full strength (same
        /// units).</summary>
        private const float RoughFullFraction = 0.18f;

        /// <summary>The factor that normalises the azimuthal series to [-1,1] (an empirical
        /// value derived from the variance).</summary>
        private const float NormaliseSpread = 1.45f;

        /// <summary>The cap on how much we carve. Beyond this, carve could swing
        /// negative.</summary>
        private const float MaxCarveAmplitude = 0.6f;

        /// <summary>The cap on how much the effective radius shrinks.</summary>
        private const float MaxShrink = 0.30f;

        /// <summary>The longest roughness component's wavelength (as a ratio of
        /// <c>coarse</c>). The undulation of the foot.</summary>
        private const float BroadOctaveRatio = 2.2f;

        /// <summary>
        /// The finest roughness component's wavelength (as a ratio of <c>fine</c>).
        /// The result always hits the floor at <see cref="MinWavelengthMetres"/>.
        /// </summary>
        private const float MicroOctaveRatio = 0.58f;

        /// <summary>The floor on how much the gullies' depth varies with azimuth (1 means
        /// all the same depth).</summary>
        private const float GullyDepthFloor = 0.40f;

        private readonly VolcanoForm _form;
        private readonly float _strength;

        private readonly float[] _shapeCos;
        private readonly float[] _shapeSin;
        private readonly float[] _gullyCos;
        private readonly float[] _gullySin;
        private readonly float[] _gullyVarCos;
        private readonly float[] _gullyVarSin;
        private readonly float[] _rillCos;
        private readonly float[] _rillSin;

        /// <summary>The harmonic number at which the gully series starts (= the count
        /// n − 2).</summary>
        private readonly int _gullyFirst;

        /// <summary>The harmonic number at which the rill series starts (= the count
        /// n₂ − 2).</summary>
        private readonly int _rillFirst;

        /// <summary>The top of the main gullies' band (= n + 2). Used for the azimuthal
        /// aliasing test.</summary>
        private readonly int _gullyMaxHarmonic;

        /// <summary>The highest harmonic the loop runs to (= the higher of the main gullies'
        /// and the rills' band tops).</summary>
        private readonly int _maxHarmonic;

        /// <summary>The top of the rills' band (= n₂ + 2). **The azimuthal aliasing test is
        /// done separately from the main gullies'.**</summary>
        private readonly int _rillMaxHarmonic;

        private readonly float _shapeNorm;
        private readonly float _gullyNorm;
        private readonly float _rillNorm;

        private readonly float _shrink;
        private readonly float _gullyAmplitude;
        private readonly float _rillAmplitude;
        private readonly float _roughAmplitude;
        private readonly float _wavelengthBroad;
        private readonly float _wavelengthCoarse;
        private readonly float _wavelengthFine;
        private readonly float _wavelengthMicro;
        private readonly uint _seedBroad;
        private readonly uint _seedCoarse;
        private readonly uint _seedFine;
        private readonly uint _seedMicro;
        private readonly uint _seedWarp;

        /// <summary>This relief's strength (0 = today's smooth cone exactly).</summary>
        public float StrengthUnit { get { return _strength; } }

        /// <summary>
        /// The form this relief was built for. <see cref="VolcanoCrater"/> uses it to work out
        /// the crater's ceiling (the fraction of the cone remaining at the crater radius
        /// differs by form).
        /// </summary>
        public VolcanoForm Form { get { return _form; } }

        /// <summary>The number of radial gullies (for diagnostics and tests). It is the
        /// number of zero crossings of the azimuthal series.</summary>
        public int GullyCount { get { return (_gullyFirst + 2) * 2; } }

        /// <summary>
        /// The number of rills (for diagnostics and tests). **It is a count that only holds
        /// up towards the foot**; nearer the summit, the
        /// <see cref="MinAzimuthWavelengthMetres"/> fade has removed them.
        /// </summary>
        public int RillCount { get { return (_rillFirst + 2) * 2; } }

        /// <summary>
        /// Whether rills can be emitted on a mountain of this radius. **If not, none at all
        /// are emitted** (see <see cref="RillMinFlankFraction"/>).
        /// </summary>
        public bool RillsFitOn(float radiusMetres)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return false;
            return RillOnsetRadiusMetres <= RillMinFlankFraction * radiusMetres;
        }

        /// <summary>
        /// The smallest radius (m) at which the rills start at full depth.
        /// **There is not a single rill inside this** (the azimuthal wavelength is too short
        /// for the 16 m grid).
        /// </summary>
        public float RillOnsetRadiusMetres
        {
            get
            {
                return MinAzimuthWavelengthMetres * 2f * _rillMaxHarmonic / 6.2831853f;
            }
        }

        /// <summary>The wavelength of the roughness's third octave (m; for diagnostics and
        /// tests).</summary>
        public float FineWavelengthMetres { get { return _wavelengthFine; } }

        /// <summary>
        /// The finest component's wavelength (m; for diagnostics and tests).
        /// **It never falls below <see cref="MinWavelengthMetres"/>.**
        /// </summary>
        public float MicroWavelengthMetres { get { return _wavelengthMicro; } }

        /// <summary>
        /// Builds one relief. Pass a <paramref name="seed"/> derived from the volcano's
        /// location (<c>DeterministicRandom.Hash(round(X), round(Z))</c>) —
        /// **the same location always gives the same mountain, however many times it is
        /// rebuilt.**
        ///
        /// <paramref name="strengthUnit"/> is 0 for "exactly identical to today", 1 for the
        /// per-form default, capped at <see cref="MaxStrengthUnit"/>.
        /// NaN and negatives fall back to 0 (the settings file can be hand-edited).
        /// </summary>
        public static VolcanoRelief For(VolcanoForm form, uint seed, float strengthUnit)
        {
            return new VolcanoRelief(form, seed, strengthUnit);
        }

        private VolcanoRelief(VolcanoForm form, uint seed, float strengthUnit)
        {
            _form = form;

            float s = float.IsNaN(strengthUnit) || strengthUnit < 0f ? 0f : strengthUnit;
            if (s > MaxStrengthUnit) s = MaxStrengthUnit;
            _strength = s;

            // ── The character of each form ────────────────────────────────
            // Shield: a basaltic shield volcano really is smooth, so this is the closest to
            //   today.
            // Strato: **the radial gullies belong to this one.** The change shows up most
            //   strongly here.
            // Dome:  craggy and blocky. The shortest wavelengths and the roughest.
            int harmonic;
            float shrink, gully, rough, coarse, fine;
            switch (form)
            {
                case VolcanoForm.Shield:
                    harmonic = 4; shrink = 0.06f; gully = 0.08f; rough = 0.07f;
                    coarse = 340f; fine = 170f;
                    break;

                case VolcanoForm.Dome:
                    harmonic = 5; shrink = 0.16f; gully = 0.14f; rough = 0.28f;
                    coarse = 220f; fine = 110f;
                    break;

                default:
                    harmonic = 9; shrink = 0.11f; gully = 0.30f; rough = 0.16f;
                    coarse = 220f; fine = 110f;
                    break;
            }

            _shrink = Clamp(shrink * s, 0f, MaxShrink);
            _gullyAmplitude = Clamp(gully * s, 0f, MaxCarveAmplitude);
            _rillAmplitude = Clamp(gully * RillDepthRatio * s, 0f, MaxCarveAmplitude);
            _roughAmplitude = Clamp(rough * s, 0f, MaxCarveAmplitude);

            // ★ The 16 m grid. Any wavelength below MinWavelengthMetres degenerates into
            //   noise.
            _wavelengthCoarse = coarse < MinWavelengthMetres ? MinWavelengthMetres : coarse;
            _wavelengthFine = fine < MinWavelengthMetres ? MinWavelengthMetres : fine;
            _wavelengthBroad = _wavelengthCoarse * BroadOctaveRatio;
            float micro = _wavelengthFine * MicroOctaveRatio;
            _wavelengthMicro = micro < MinWavelengthMetres ? MinWavelengthMetres : micro;

            _gullyFirst = harmonic - 2;
            _gullyMaxHarmonic = harmonic + 2;

            // The rills' central harmonic (the history of the measurements is in
            // RillHarmonicOffset's doc).
            int rillHarmonic = harmonic + RillHarmonicOffset;
            _rillFirst = rillHarmonic - 2;
            _rillMaxHarmonic = rillHarmonic + 2;

            _maxHarmonic = _rillMaxHarmonic > _gullyMaxHarmonic
                         ? _rillMaxHarmonic : _gullyMaxHarmonic;

            _seedBroad = DeterministicRandom.Hash(seed, 0x5EEDB40Du);
            _seedCoarse = DeterministicRandom.Hash(seed, 0x5EEDC0DEu);
            _seedFine = DeterministicRandom.Hash(seed, 0x5EEDF14Eu);
            _seedMicro = DeterministicRandom.Hash(seed, 0x5EEDBEEFu);
            _seedWarp = DeterministicRandom.Hash(seed, 0x5EED1A2Bu);

            // The foot's outline. Making the first harmonic the largest is what produces "one
            // side's foot is longer".
            float[] shapeWeights = { 1.00f, 0.55f, 0.35f, 0.22f };
            // The gullies. The centre (k = n) is the largest, and the sidebands n±1 / n±2
            // break up the even spacing and the depth.
            float[] gullyWeights = { 0.55f, 0.80f, 1.00f, 0.80f, 0.55f };
            // The rills. Heavier sidebands than the main gullies, to vary the count and depth
            // more.
            float[] rillWeights = { 0.70f, 0.88f, 1.00f, 0.88f, 0.70f };

            _shapeCos = new float[ShapeHarmonics];
            _shapeSin = new float[ShapeHarmonics];
            _gullyCos = new float[GullyHarmonics];
            _gullySin = new float[GullyHarmonics];
            _gullyVarCos = new float[GullyHarmonics];
            _gullyVarSin = new float[GullyHarmonics];
            _rillCos = new float[RillHarmonics];
            _rillSin = new float[RillHarmonics];

            for (int i = 0; i < ShapeHarmonics; i++)
            {
                double phase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x100 + i));
                _shapeCos[i] = shapeWeights[i] * (float)Math.Cos(phase);
                _shapeSin[i] = shapeWeights[i] * (float)Math.Sin(phase);
            }

            for (int i = 0; i < GullyHarmonics; i++)
            {
                double phase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x200 + i));
                _gullyCos[i] = gullyWeights[i] * (float)Math.Cos(phase);
                _gullySin[i] = gullyWeights[i] * (float)Math.Sin(phase);

                // The same band at a different phase. A series whose only job is **varying the
                // height at which each gully starts**; without it every gully converges on a
                // single point at the summit.
                double varPhase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x300 + i));
                _gullyVarCos[i] = gullyWeights[i] * (float)Math.Cos(varPhase);
                _gullyVarSin[i] = gullyWeights[i] * (float)Math.Sin(varPhase);
            }

            for (int i = 0; i < RillHarmonics; i++)
            {
                double phase = 2.0 * Math.PI * DeterministicRandom.Unit(seed, (uint)(0x400 + i));
                _rillCos[i] = rillWeights[i] * (float)Math.Cos(phase);
                _rillSin[i] = rillWeights[i] * (float)Math.Sin(phase);
            }

            _shapeNorm = NormOf(shapeWeights);
            _gullyNorm = NormOf(gullyWeights);
            _rillNorm = NormOf(rillWeights);
        }

        /// <summary>
        /// **The rise above the terrain** (m) at the point <paramref name="dx"/> /
        /// <paramref name="dz"/> from the centre.
        ///
        /// **The result is always in [0, <paramref name="heightMetres"/>], and is always
        /// exactly 0 when <c>√(dx²+dz²) ≥ radiusMetres</c>.**
        /// Those two are the class doc's two hard constraints themselves, upheld structurally
        /// by using nothing but multiplication. Bad input (NaN, R≤0, H≤0) also gives 0.
        /// </summary>
        public float ProfileAt(float dx, float dz, float radiusMetres, float heightMetres)
        {
            if (float.IsNaN(dx) || float.IsNaN(dz)) return 0f;
            if (float.IsNaN(radiusMetres) || float.IsNaN(heightMetres)) return 0f;
            if (radiusMetres <= 0f || heightMetres <= 0f) return 0f;

            float d2 = dx * dx + dz * dz;
            float d = (float)Math.Sqrt(d2);

            // ★★ A strength of 0 is **today's output itself**, not "a mountain with a little
            //    relief".
            if (!(_strength > 0f)) return VolcanoShape.ProfileAt(_form, d, radiusMetres, heightMetres);

            if (d >= radiusMetres) return 0f;
            if (!(d > 0f)) return VolcanoShape.ProfileAt(_form, 0f, radiusMetres, heightMetres);

            float inv = 1f / d;
            float ux = dx * inv;
            float uz = dz * inv;

            // ── 2. The non-circular foot. **A function of azimuth alone**, so the outline is
            //    a closed, smooth curve ──
            //   ★ No trigonometry. cos(kθ) / sin(kθ) come from powers of a unit complex
            //     number: the real part of (ux + i·uz)^k is cos(kθ) and the imaginary part is
            //     sin(kθ).
            float shapeAz = 0f;
            float cr = ux;
            float ci = uz;
            for (int k = 1; k <= ShapeHarmonics; k++)
            {
                shapeAz += _shapeCos[k - 1] * cr + _shapeSin[k - 1] * ci;
                float nr0 = cr * ux - ci * uz;
                ci = cr * uz + ci * ux;
                cr = nr0;
            }
            shapeAz = Clamp(shapeAz * _shapeNorm, -1f, 1f);

            // ★★ **It only ever works in the direction of shrinking** (constraint 1 in the
            //    class doc).
            float effectiveRadius = radiusMetres * (1f - _shrink * 0.5f * (1f - shapeAz));
            if (!(effectiveRadius > 0f)) return 0f;
            if (d >= effectiveRadius) return 0f;

            float baseMetres = VolcanoShape.ProfileAt(_form, d, effectiveRadius, heightMetres);
            if (!(baseMetres > 0f)) return 0f;

            float t = d / effectiveRadius;

            // ── 1. The radial gullies (barrancos) ───────────────────────────
            //   A dead straight ray looks artificial, so we rotate the azimuth by a small
            //   angle that varies with position. A small-angle rotation is just normalising
            //   (ux − uz·w, uz + ux·w); no trigonometry needed.
            float warp = WarpRadians
                       * ValueNoise(dx / (_wavelengthCoarse * 2f), dz / (_wavelengthCoarse * 2f),
                                    _seedWarp);
            float wx = ux - uz * warp;
            float wz = uz + ux * warp;
            float wlen = (float)Math.Sqrt(wx * wx + wz * wz);
            if (wlen > 0f) { float wi = 1f / wlen; wx *= wi; wz *= wi; }
            else { wx = ux; wz = uz; }

            float gullyAz = 0f;
            float gullyVarAz = 0f;
            float rillAz = 0f;
            cr = wx;
            ci = wz;
            for (int k = 1; k <= _maxHarmonic; k++)
            {
                int g = k - _gullyFirst;
                if (g >= 0 && g < GullyHarmonics)
                {
                    gullyAz += _gullyCos[g] * cr + _gullySin[g] * ci;
                    gullyVarAz += _gullyVarCos[g] * cr + _gullyVarSin[g] * ci;
                }

                // ★ The rills are picked off **the same ladder of powers** (no trigonometry
                //   and no second ladder needed).
                //   They ride the same meander (warp), so they bend together with the main
                //   gullies — which is what "tributaries feeding into the main gullies" looks
                //   like.
                int rl = k - _rillFirst;
                if (rl >= 0 && rl < RillHarmonics)
                {
                    rillAz += _rillCos[rl] * cr + _rillSin[rl] * ci;
                }

                float nr = cr * wx - ci * wz;
                ci = cr * wz + ci * wx;
                cr = nr;
            }
            gullyAz = Clamp(gullyAz * _gullyNorm, -1f, 1f);
            gullyVarAz = Clamp(gullyVarAz * _gullyNorm, -1f, 1f);
            rillAz = Clamp(rillAz * _rillNorm, -1f, 1f);

            //   Each gully starts at a different height. **Without this every gully converges
            //   on a single point at the summit and it looks like a radial stripe pattern
            //   rather than a mountain.**
            float start = GullyStartFraction + GullyStartSpread * 0.5f * (1f + gullyVarAz);
            float depth = SmoothStep(start, start + GullyRampFraction, t);

            //   ★★ **The nearer the summit, the shorter the azimuthal wavelength.** Where
            //   the circumference divided by the number of gullies becomes too short against
            //   the 16 m grid, we remove the gullies — without that, the area around the
            //   summit becomes a chequerboard rather than relief (confirmed by measurement).
            float azWavelength = 6.2831853f * d / _gullyMaxHarmonic;
            depth *= SmoothStep(MinAzimuthWavelengthMetres, MinAzimuthWavelengthMetres * 2f,
                                azWavelength);

            //   The gully floors are the azimuthal series' zero crossings. **The ridges come
            //   out broad and flat** (see the class doc).
            float abs = gullyAz < 0f ? -gullyAz : gullyAz;
            float channel = 1f - SmoothStep(0f, ChannelWidth, abs);

            //   The depth varies with azimuth and position. **With every gully the same depth
            //   it looks like a flower pattern.**
            float broad = ValueNoise(dx / _wavelengthBroad, dz / _wavelengthBroad, _seedBroad);
            float depthScale = GullyDepthFloor
                             + (1f - GullyDepthFloor) * 0.5f * (1f + broad);

            float carve = 1f - _gullyAmplitude * depth * channel * depthScale;

            // ── 1b. The rills. **They cut the ridges between the main gullies** ───────────
            //   The mechanism is the same as the main gullies' (zero crossings as the floors),
            //   but
            //     * three harmonics higher → 6 more of them, with slightly narrower arcs
            //       (recovered via W)
            //     * they start further out → shorter
            //     * shallow inside the main gullies (RillRidgeFloor) → they look like they are
            //       cutting the ridges
            //   ★ The azimuthal aliasing test uses **the rills' own harmonic**. Test against
            //     the main gullies' harmonic and the rills survive inwards past where they
            //     break the 16 m grid, giving a chequerboard.
            float rillDepth = SmoothStep(RillStartFraction,
                                         RillStartFraction + RillRampFraction, t);
            float rillAzWavelength = 6.2831853f * d / _rillMaxHarmonic;
            rillDepth *= SmoothStep(MinAzimuthWavelengthMetres, MinAzimuthWavelengthMetres * 2f,
                                    rillAzWavelength);

            // ★ On a mountain where they would only fit in a narrow ring, we emit **none at
            //   all** (RillMinFlankFraction).
            if (rillDepth > 0f && _rillAmplitude > 0f && RillsFitOn(effectiveRadius))
            {
                float rillAbs = rillAz < 0f ? -rillAz : rillAz;
                float rillChannel = 1f - SmoothStep(0f, RillChannelWidth, rillAbs);
                // Shallow at the main gully's floor (channel = 1) and deepest on the ridge
                // (channel = 0).
                float ridgeGate = RillRidgeFloor + (1f - RillRidgeFloor) * (1f - channel);
                carve *= 1f - _rillAmplitude * rillDepth * rillChannel * ridgeGate;
            }

            // ── 3. The general roughness. Four octaves (the foot's undulation / mid / the
            //    surface / the finest surface) ──
            //   ★ The fourth sits right on the 16 m grid's floor
            //     (MinWavelengthMetres = 4 cells).
            //     **Do not add any component finer than this** (see the measurements in the
            //     class doc).
            float rough = 0.36f * broad
                        + 0.30f * ValueNoise(dx / _wavelengthCoarse, dz / _wavelengthCoarse, _seedCoarse)
                        + 0.21f * ValueNoise(dx / _wavelengthFine, dz / _wavelengthFine, _seedFine)
                        + 0.13f * ValueNoise(dx / _wavelengthMicro, dz / _wavelengthMicro, _seedMicro);
            carve *= 1f - _roughAmplitude * SmoothStep(RoughStartFraction, RoughFullFraction, t)
                                          * 0.5f * (1f - rough);

            // ★★ **carve never exceeds 1** (constraint 2 in the class doc).
            //    By the design of the amplitudes it never gets here, but the .cgs can be
            //    hand-edited.
            carve = Clamp(carve, 0f, 1f);
            return baseMetres * carve;
        }

        /// <summary>
        /// The factor that normalises the azimuthal series to [-1,1]. The raw sum can reach
        /// Σw, but in practice it rarely swings that far, so we divide by a spread derived
        /// from the RMS and let <see cref="ProfileAt"/> clamp afterwards.
        /// **That the quotient can go outside [-1,1] is accounted for.**
        /// </summary>
        private static float NormOf(float[] weights)
        {
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++) sum += weights[i] * weights[i];
            float rms = (float)Math.Sqrt(sum * 0.5);
            if (!(rms > 0f)) return 0f;
            return 1f / (rms * NormaliseSpread);
        }

        /// <summary>
        /// Two-dimensional value noise ([-1,1]). The lattice values are
        /// <see cref="DeterministicRandom"/>'s hash itself, and the interpolation is 3t²−2t³.
        /// **We do not use <c>Mathf.PerlinNoise</c>** — Core is engine-free and has to give
        /// the same values on net35 and net8.0.
        ///
        /// ★ It is <c>internal</c> so that <c>tools/VolcanoPreview</c> can measure the
        ///   <see cref="MinWavelengthMetres"/> floor for real
        ///   (the tool compiles Core's source directly, so they end up in the same assembly).
        ///   This follows the project's rule of **never deciding a floor from a rewritten
        ///   approximation**.
        /// </summary>
        internal static float ValueNoise(float x, float z, uint seed)
        {
            int ix = FloorToInt(x);
            int iz = FloorToInt(z);
            float fx = x - ix;
            float fz = z - iz;

            float u = fx * fx * (3f - 2f * fx);
            float v = fz * fz * (3f - 2f * fz);

            float n00 = Corner(ix, iz, seed);
            float n10 = Corner(ix + 1, iz, seed);
            float n01 = Corner(ix, iz + 1, seed);
            float n11 = Corner(ix + 1, iz + 1, seed);

            float a = n00 + (n10 - n00) * u;
            float b = n01 + (n11 - n01) * u;
            return a + (b - a) * v;
        }

        private static float Corner(int x, int z, uint seed)
        {
            unchecked
            {
                uint key = (uint)(x * 73856093) ^ (uint)(z * 19349663);
                uint h = DeterministicRandom.Hash(key, seed);
                return (h >> 8) * (2f / 16777216f) - 1f;
            }
        }

        /// <summary>Conversion to an integer without going through <c>Math.Floor</c> (it
        /// rounds downwards for negatives too).</summary>
        private static int FloorToInt(float v)
        {
            int i = (int)v;
            return v < 0f && v != i ? i - 1 : i;
        }

        /// <summary>Smoothly 0 → 1 over
        /// [<paramref name="from"/>, <paramref name="to"/>].</summary>
        private static float SmoothStep(float from, float to, float t)
        {
            if (!(to > from)) return t >= to ? 1f : 0f;
            float u = (t - from) / (to - from);
            u = Clamp(u, 0f, 1f);
            return u * u * (3f - 2f * u);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
