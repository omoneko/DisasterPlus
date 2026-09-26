using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// Which tier a vortex puff belongs to. <b>Each tier is drawn by its own clone (its own
    /// <c>ParticleSystem</c>).</b>
    ///
    /// Splitting them is not a preference but **a constraint** — <c>startColor</c> and
    /// <c>startSize</c> are shared state on the <c>ParticleSystem</c> and cannot be varied
    /// per <c>RenderEffect</c> call (effects measurement doc §B-4). The units over which we
    /// want to vary colour and particle size become, one for one, the number of clones.
    /// </summary>
    public enum VortexCloudLayer
    {
        /// <summary>The cloud base. **Dark, flat and large.** The underside of a
        /// cumulonimbus.</summary>
        Deck = 0,

        /// <summary>The tower. **Bright, smaller and billowing.** The cumulonimbus's body (the
        /// cauliflower).</summary>
        Tower = 1,

        /// <summary>The anvil (the canopy). **The brightest, the largest and the
        /// thinnest.**</summary>
        Canopy = 2,
    }

    /// <summary>
    /// Where one vortex puff is placed and how it flows. **Everything is "as a fraction of
    /// the vortex's outer radius"**, and converting to world coordinates is the job of
    /// <c>Game/Typhoon/TyphoonCloudFx</c> (this type touches neither Unity nor the game).
    /// </summary>
    public struct VortexPuff
    {
        /// <summary>The bearing seen from the centre (rad, [0, 2π)). The vortex's rotation is
        /// added to this.</summary>
        public readonly float AngleRadians;

        /// <summary>Distance from the centre ÷ the vortex's outer radius.</summary>
        public readonly float RadiusFraction;

        /// <summary>Where in the cloud's thickness it sits, [0, 1] (1 is the top).</summary>
        public readonly float HeightFraction;

        /// <summary>The radius of the disc the particles are scattered over ÷ the vortex's
        /// outer radius.</summary>
        public readonly float DiscFraction;

        /// <summary>
        /// How far the disc is stretched upwards ÷ the cloud's thickness.
        /// <c>SpawnArea</c>'s fourth argument (<c>halfHeight</c>) scatters
        /// <b>upwards only, uniformly over [0, halfHeight]</b> (measured from IL, §B-4), so
        /// making <see cref="HeightFraction"/> the bottom of the tier stacks the tiers
        /// exactly.
        /// **The cloud base is thin and the tower is thick** — that is the substance of
        /// "flat underneath, billowing on top".
        /// </summary>
        public readonly float BandFraction;

        /// <summary>The puff's density fraction, [0, 1] (it multiplies <c>magnitude</c>).</summary>
        public readonly float DensityFraction;

        /// <summary>The tangential speed fraction (the direction the vortex turns), [0,
        /// 1].</summary>
        public readonly float SwirlFraction;

        /// <summary>The radial speed fraction. **Negative is inflow, positive is
        /// outflow.**</summary>
        public readonly float RadialFraction;

        /// <summary>The vertical speed fraction, [0, 1] (upwards).</summary>
        public readonly float RiseFraction;

        /// <summary>Which clone draws it.</summary>
        public readonly VortexCloudLayer Layer;

        public VortexPuff(float angleRadians, float radiusFraction, float heightFraction,
                          float discFraction, float bandFraction, float densityFraction,
                          float swirlFraction, float radialFraction, float riseFraction,
                          VortexCloudLayer layer)
        {
            AngleRadians = angleRadians;
            RadiusFraction = radiusFraction;
            HeightFraction = heightFraction;
            DiscFraction = discFraction;
            BandFraction = bandFraction;
            DensityFraction = densityFraction;
            SwirlFraction = swirlFraction;
            RadialFraction = radialFraction;
            RiseFraction = riseFraction;
            Layer = layer;
        }
    }

    /// <summary>
    /// Pure data for laying the typhoon's vortex out as **an arrangement of cumulonimbus**.
    /// <b>This is Core, so it touches the engine not at all.</b>
    ///
    /// ── Why it was rebuilt (2026-08-22, the owner's report) ────────────────────
    ///
    /// &gt; The cloud effect has turned into smoke, so it looks very odd.
    /// &gt; Please rebuild it with swirling cumulonimbus in mind.
    ///
    /// **The report is correct, and the cause splits into two.**
    ///
    /// 1. <b>The material</b>. The old implementation had <c>Factory Smoke</c> as its first
    ///    choice. Reading the shipped assets directly, the texture of its particle material
    ///    <c>Smoke</c> is <b>a round blob of dark soot at a mean RGB of (75, 78, 80)</b>
    ///    (right down to the specks of embers in it), and no amount of tinting makes it look
    ///    like anything but smoke. How the material is chosen, complete with the measured
    ///    figures, is written in <c>Game/Typhoon/TyphoonCloudFx</c>'s doc.
    /// 2. <b>The shape</b>. The old implementation laid "three arms + a ring" out **flat, in
    ///    a single tier**. A cumulonimbus is <b>a tower extending vertically</b>, billowing
    ///    white in the sunlight on top (the cauliflower) and flat and dark underneath.
    ///    A single tier does not make a cloud.
    ///
    /// ── The structure of a cumulonimbus (this is the shape of this type itself) ────────────
    ///
    /// <code>
    ///        ~~~~~~~~~~~~   Canopy  the anvil … the brightest. Broad, thin, and blowing outwards
    ///         (  )(  )(  )  Tower   the tower … bright. Billowing upwards (two tiers)
    ///        ____________   Deck    the base  … dark and flat. Low, and drawn inwards
    /// </code>
    ///
    /// In a typhoon these towers line up as <b>a ring around the eye (the eyewall)</b> and
    /// flow outwards from it as <b>spiral arms (the rainbands)</b>. The closer to the
    /// centre, the taller.
    ///
    /// ── How they are laid out ────────────────────────────────────────
    ///
    /// <see cref="ColumnCount"/> **columns** are placed (<see cref="EyeWallColumns"/> for
    /// the eyewall plus <see cref="ArmCount"/> arms × <see cref="ColumnsPerArm"/>), and each
    /// column stacks <see cref="LevelsPerColumn"/> puffs at different heights.
    /// The total is <see cref="PuffCount"/>, and **that is the number of
    /// <c>RenderEffect</c> calls per frame** (up from the old implementation's 30).
    ///
    /// ── ★ The condition for the eye reading as a hole (this type enforces it itself) ───────
    ///
    /// **The old implementation left this promise to the Game side, and did in fact fill the
    /// eye in once.** A puff is not a point: it is scattered over the disc's radius and
    /// spreads a further half a particle-radius outwards.
    /// Now that <b>both the disc's radius (<see cref="VortexPuff.DiscFraction"/>) and the
    /// particle size (<see cref="SizeFractionOf"/>) are declared by this type</b>, this type
    /// can compute "how far inwards the particles reach":
    ///
    /// <code>
    /// EyeClearanceOf(index) = RadiusFraction - DiscFraction - SizeFraction/2 ≥ EyeFraction
    /// </code>
    ///
    /// **A test pins this for every puff.** Move the numbers and, if the eye fills in, the
    /// build goes red — the old doc's caveat that "the tests will not catch it either" is
    /// gone.
    ///
    /// The only escape route left on the Game side is the particle size's lower clamp
    /// (<c>MinSizeMetres</c>), and that **only bites on the very smallest typhoon** (even at
    /// intensity 1 the strong-wind radius is over 2200 m and the tower's particle size is
    /// over 110 m). It is written up in that doc.
    ///
    /// Note that "the eye" here means <b>the hole in the cloud</b>, not
    /// <c>TyphoonProfile.EyeFraction</c>'s <b>eye of the wind</b> (that one is smaller). It
    /// is sized so as to read against the particle size.
    ///
    /// ── The jitter ────────────────────────────────────────
    ///
    /// Perfectly even spacing looks mechanical, so <see cref="DeterministicRandom"/> puts a
    /// small jitter into the angle, radius and height. It is **a function of the indices
    /// alone**, so the shape is the same every frame and the puffs do not flicker (mix the
    /// frame in and the vortex reassembles itself every frame and looks like it is boiling).
    /// <c>System.Random</c> is not used (the discipline across this whole mod).
    /// The jitter draws a different index for each level even within the same column, so
    /// **the tower does not stand straight but billows out with an offset at each level** —
    /// which is the substance of the cauliflower.
    /// </summary>
    public static class VortexPuffLayout
    {
        /// <summary>The number of arms (rainbands).</summary>
        public const int ArmCount = 3;

        /// <summary>How many columns are placed on one arm.</summary>
        public const int ColumnsPerArm = 5;

        /// <summary>How many columns are placed on the ring around the eye (the eyewall).</summary>
        public const int EyeWallColumns = 7;

        /// <summary>The total number of columns.</summary>
        public const int ColumnCount = ArmCount * ColumnsPerArm + EyeWallColumns;

        /// <summary>How many puffs are stacked in one column (base, lower tower, upper tower,
        /// anvil).</summary>
        public const int LevelsPerColumn = 4;

        /// <summary>The number of <c>RenderEffect</c> calls issued per frame. **This is the
        /// per-frame cap.**</summary>
        public const int PuffCount = ColumnCount * LevelsPerColumn;

        /// <summary>The radius of the hole in the cloud (the eye) ÷ the vortex's outer radius.
        /// **No puff may reach this far in.**</summary>
        public const float EyeFraction = 0.16f;

        /// <summary>The radius the eyewall's ring is placed at ÷ the vortex's outer radius
        /// (i.e. the innermost columns).</summary>
        public const float EyeWallFraction = 0.26f;

        /// <summary>
        /// How many turns an arm makes as it runs out to the rim.
        ///
        /// ★ **Lowered from 0.85 after drawing it.** At 0.85 (i.e. 306 degrees), the arc
        ///   between neighbouring columns on an arm exceeded 3 km at a radius of 0.5R, and
        ///   the arm read as **a line of torn-off clouds** (see the plan image from
        ///   <c>tools/TyphoonPreview</c>).
        /// </summary>
        public const float SpiralTurns = 0.45f;

        // ── The per-level tables of numbers ───────────────────────────────────
        //
        // ★ The array index is the level (0=base, 1=lower tower, 2=upper tower, 3=anvil).
        //   They are readonly float[] in the same shape as Core's other tables
        //   (EruptionColumn.Weights), and **the elements are never rewritten** (they are
        //   static, but they are not Unity objects, so there is no fake-null problem).

        /// <summary>The tier for each level.</summary>
        private static readonly VortexCloudLayer[] Layers =
        {
            VortexCloudLayer.Deck,
            VortexCloudLayer.Tower,
            VortexCloudLayer.Tower,
            VortexCloudLayer.Canopy,
        };

        /// <summary>The disc radius ÷ the outer radius, per level. The base and the anvil are
        /// broad; the tower is tight.</summary>
        private static readonly float[] LevelDisc = { 0.115f, 0.062f, 0.052f, 0.095f };

        /// <summary>How much is added to the column's radius per level ÷ the outer radius.
        /// The base spreads outwards, the tower leans outwards the higher it goes (wind
        /// shear), and the anvil overhangs.</summary>
        private static readonly float[] LevelOutward = { 0.060f, 0.000f, 0.015f, 0.050f };

        /// <summary>The upward extent per level ÷ the cloud's thickness. **The base is thin
        /// and flat; the tower is thick.**</summary>
        private static readonly float[] LevelBand = { 0.06f, 0.34f, 0.34f, 0.14f };

        /// <summary>The height offset per level (as a fraction of the cloud's thickness).</summary>
        private static readonly float[] LevelHeightBase = { 0.02f, 0.12f, 0.20f, 0.34f };

        /// <summary>The part of the per-level height that is proportional to "the column's
        /// height".</summary>
        private static readonly float[] LevelHeightSpan = { 0.00f, 0.00f, 0.24f, 0.46f };

        /// <summary>The density per level. **Dense at the bottom, thinner higher up** (exactly
        /// how a cumulonimbus looks).</summary>
        private static readonly float[] LevelDensity = { 1.00f, 0.85f, 0.65f, 0.45f };

        /// <summary>The tangential speed per level. **The lowest tier is fastest** (the gale
        /// near the surface).</summary>
        private static readonly float[] LevelSwirl = { 1.00f, 0.85f, 0.65f, 0.45f };

        /// <summary>
        /// The radial speed per level. **Negative is inflow, positive is outflow.**
        /// Inflow at the bottom and outflow at the top is a typhoon's secondary circulation.
        /// </summary>
        private static readonly float[] LevelRadial = { -0.35f, -0.10f, 0.05f, 0.45f };

        /// <summary>The updraught component per level. Strongest inside the tower.</summary>
        private static readonly float[] LevelRise = { 0.05f, 0.55f, 0.75f, 0.15f };

        /// <summary>The particle size ÷ the vortex's outer radius, per tier. **It is the
        /// fraction used for the clone's <c>startSize</c>.**</summary>
        public const float DeckSizeFraction = 0.055f;

        /// <summary>The same (tower). The tower uses smaller particles to bring out the
        /// billowing.</summary>
        public const float TowerSizeFraction = 0.040f;

        /// <summary>The same (anvil). The largest, spread out thin.</summary>
        public const float CanopySizeFraction = 0.070f;

        /// <summary>The height of the eyewall's columns, [0, 1]. **The tallest.**</summary>
        public const float EyeWallTop = 1f;

        /// <summary>How much lower the arms' columns get as they go outwards.</summary>
        public const float ArmTopFalloff = 0.5f;

        /// <summary>
        /// How much thinner the arms' columns get as they go outwards.
        ///
        /// ★ **Lowered from 0.45 after drawing it.** <see cref="MagnitudeFor"/> normalises by
        ///   the disc's area, so the particle count per puff does not depend on the disc's
        ///   size — which means that the moment <see cref="ArmDiscGrowth"/> widens the disc,
        ///   the density per unit area on the outside has already dropped. Multiply that by
        ///   0.45 as well and the outside of the arms became **a scatter of dots**. Real
        ///   rainbands are thinner further out too, so the direction was right; it was
        ///   simply applied too hard.
        /// </summary>
        public const float ArmDensityFalloff = 0.15f;

        /// <summary>Even the lowest column keeps at least this much of the level's thickness
        /// (the fraction at height 0).</summary>
        public const float BandFloor = 0.45f;

        /// <summary>
        /// How many times larger the disc is on the arms' outermost column, minus 1.
        ///
        /// ★ **This too was added after drawing it.** The columns on an arm get further
        ///   apart the further out they go (the arc lengthens), so holding the disc's size
        ///   constant leaves the cloud broken up on the outside alone. Real rainbands are
        ///   broader and thinner further out too, so **broaden and thin** is the right
        ///   direction (the thinning is already done by
        ///   <see cref="ArmDensityFalloff"/>).
        /// </summary>
        public const float ArmDiscGrowth = 1.4f;

        /// <summary>The amplitude of the angle jitter (rad).</summary>
        public const float AngleJitterRadians = 0.14f;

        /// <summary>The amplitude of the radius jitter (as a fraction of the outer
        /// radius).</summary>
        public const float RadiusJitterFraction = 0.035f;

        /// <summary>The amplitude of the height jitter (as a fraction of the thickness). **It
        /// offsets the tower level by level.**</summary>
        public const float HeightJitterFraction = 0.025f;

        /// <summary>The jitter's seed. **A fixed value** (so it stays a function of the
        /// indices alone).</summary>
        private const uint JitterSeed = 0x54595048u;   // "TYPH"

        private const float TwoPi = 6.28318530718f;

        /// <summary>
        /// The particle size ÷ the vortex's outer radius, per tier. The <c>Game</c> side
        /// sets the clone's <c>startSize</c> from this fraction, and **this type uses the
        /// same value to compute the eye's clearance** (so the two cannot drift apart).
        /// </summary>
        public static float SizeFractionOf(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return DeckSizeFraction;
            if (layer == VortexCloudLayer.Canopy) return CanopySizeFraction;
            return TowerSizeFraction;
        }

        /// <summary>
        /// Where puff <paramref name="index"/> is placed and how it flows. All of it in
        /// **normalised fractions**.
        ///
        /// **An out-of-range index does not throw; it is rounded to the first puff of the
        /// eyewall.** This is a path taken every frame, so a miscount by the caller must not
        /// break the level load.
        /// </summary>
        public static VortexPuff PuffAt(int index)
        {
            if (index < 0 || index >= PuffCount) index = 0;

            int column = index / LevelsPerColumn;
            int level = index - column * LevelsPerColumn;

            float columnAngle;
            float columnRadius;
            float columnTop;
            float columnDensity;
            float columnDisc;
            Column(column, out columnAngle, out columnRadius, out columnTop, out columnDensity,
                   out columnDisc);

            VortexCloudLayer layer = Layers[level];

            // The jitter. **A function of the indices alone**, so the shape is the same every
            // frame (see the class doc).
            // A different index per level = the tower does not stand straight but billows out
            // with an offset at each level.
            float ja = DeterministicRandom.Unit(JitterSeed, (uint)index) * 2f - 1f;
            float jr = DeterministicRandom.Unit(JitterSeed + 1u, (uint)index) * 2f - 1f;
            float jh = DeterministicRandom.Unit(JitterSeed + 2u, (uint)index) * 2f - 1f;

            float angle = columnAngle + ja * AngleJitterRadians;
            float radius = columnRadius + LevelOutward[level] + jr * RadiusJitterFraction;

            // The columns on an arm get further apart the further out they go, so widen the
            // disc as well (see <see cref="ArmDiscGrowth"/>).
            float disc = LevelDisc[level] * columnDisc;

            // ★★ The eye stays a hole. **This type enforces it itself** (see the class doc).
            //    What it is pushed back to is the minimum radius with "how far that level's
            //    particles spread" subtracted.
            float minimum = EyeFraction + disc + SizeFractionOf(layer) * 0.5f;
            if (radius < minimum) radius = minimum;

            float height = LevelHeightBase[level] + LevelHeightSpan[level] * columnTop
                           + jh * HeightJitterFraction;
            if (height < 0f) height = 0f;
            if (height > 1f) height = 1f;

            // ★ The level's thickness follows the column's height too. Without that, even
            //   the low columns on the outside become towers as tall as the eyewall's, and
            //   "taller towards the centre" disappears.
            float band = LevelBand[level] * (BandFloor + (1f - BandFloor) * columnTop);

            return new VortexPuff(Normalize(angle), radius, height,
                                  disc, band,
                                  LevelDensity[level] * columnDensity,
                                  LevelSwirl[level], LevelRadial[level], LevelRise[level],
                                  layer);
        }

        /// <summary>
        /// How far the inner edge of puff <paramref name="index"/> is from the eye's edge
        /// (as a fraction of the outer radius). **It must be 0 or more, or the eye fills in.**
        /// A test pins this for every puff.
        /// </summary>
        public static float EyeClearanceOf(int index)
        {
            VortexPuff puff = PuffAt(index);
            float reach = puff.DiscFraction + SizeFractionOf(puff.Layer) * 0.5f;
            return puff.RadiusFraction - reach - EyeFraction;
        }

        /// <summary>
        /// The bare placement of column <paramref name="column"/> (with neither the level
        /// offsets nor the jitter applied).
        /// The first part is the eyewall's ring and the rest are the arms.
        /// </summary>
        private static void Column(int column, out float angleRadians, out float radiusFraction,
                                   out float topFraction, out float densityFraction,
                                   out float discScale)
        {
            if (column < EyeWallColumns)
            {
                // The eyewall: a ring evenly spaced just outside the eye. The tallest and the
                // densest.
                angleRadians = TwoPi * column / EyeWallColumns;
                radiusFraction = EyeWallFraction;
                topFraction = EyeWallTop;
                densityFraction = 1f;
                discScale = 1f;
                return;
            }

            int k = column - EyeWallColumns;
            int arm = k / ColumnsPerArm;
            int step = k - arm * ColumnsPerArm;

            // t is the position along the arm, (0, 1). Offset by half a step so no column
            // sits at either end.
            float t = (step + 0.5f) / ColumnsPerArm;

            angleRadians = TwoPi * arm / ArmCount + TwoPi * SpiralTurns * t;
            radiusFraction = EyeWallFraction + (1f - EyeWallFraction) * t;
            topFraction = EyeWallTop - ArmTopFalloff * t;
            densityFraction = 1f - ArmDensityFalloff * t;
            discScale = 1f + ArmDiscGrowth * t;
        }

        /// <summary>
        /// The <c>magnitude</c> (i.e. the particle density) for one puff. Its body is
        /// <see cref="ParticleBudget.MagnitudeFor"/> itself — it was moved over there so
        /// that **④'s vortex and its rainstorm use the same formula**.
        /// It is left here because the vortex side's calls and docs are written with this name.
        /// </summary>
        /// <param name="discRadius">One puff's disc radius (m).</param>
        /// <param name="rateOverTime">The effect side's
        /// <c>emission.rateOverTime.constant</c>.</param>
        /// <param name="particlesPerSecond">The particles per second wanted across **the whole
        /// vortex**.</param>
        /// <param name="puffCount">The number of puffs (<see cref="PuffCount"/>).</param>
        public static float MagnitudeFor(float discRadius, float rateOverTime,
                                         float particlesPerSecond, int puffCount)
        {
            return ParticleBudget.MagnitudeFor(discRadius, rateOverTime,
                                               particlesPerSecond, puffCount);
        }

        private static float Normalize(float radians)
        {
            if (float.IsNaN(radians)) return 0f;

            float a = radians % TwoPi;
            if (a < 0f) a += TwoPi;
            return a;
        }
    }
}
