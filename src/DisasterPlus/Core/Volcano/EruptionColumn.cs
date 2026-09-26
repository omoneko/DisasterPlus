using System;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The instructions for spawning one segment of the plume column. **Not one Unity type
    /// appears here.**
    /// <c>Game/Volcano/VolcanoEruptionFx</c> copies it straight into
    /// <c>EffectInfo.SpawnArea(position, Vector3.up, radius, halfHeight)</c> and
    /// <c>RenderEffect</c>'s <c>velocity</c> argument.
    /// </summary>
    public struct EruptionColumnSegment
    {
        /// <summary>The horizontal offset from the vent (m; how far it leans
        /// downwind).</summary>
        public readonly float OffsetX;

        /// <summary>The height above the vent (m). **This is the segment's lower
        /// edge.**</summary>
        public readonly float OffsetY;

        /// <summary>The horizontal offset from the vent (m).</summary>
        public readonly float OffsetZ;

        /// <summary>The radius (m) of the disc we spawn in.</summary>
        public readonly float RadiusMetres;

        /// <summary>
        /// The extent along the axis (upwards) in m. <c>SpawnArea</c>'s fourth argument
        /// scatters in that direction with a uniform random value in
        /// <c>[0, halfHeight]</c> (**it is not symmetric about the centre**; measured from
        /// the IL, §B-4), so making <see cref="OffsetY"/> the lower edge stacks the segments
        /// exactly.
        /// </summary>
        public readonly float HalfHeightMetres;

        /// <summary>The velocity added to the particles (m/s). The rise and the downwind
        /// drift come from here.</summary>
        public readonly float DriftX;

        /// <summary>The same, vertically.</summary>
        public readonly float DriftY;

        /// <summary>The same.</summary>
        public readonly float DriftZ;

        /// <summary><c>RenderEffect</c>'s <c>magnitude</c> (i.e. the particle density, not a
        /// size).</summary>
        public readonly float Magnitude;

        /// <summary>
        /// Whether this is an umbrella segment. If <c>true</c> it is drawn with **a different
        /// clone** — the umbrella is paler, has larger particles and a longer lifetime
        /// (<c>VolcanoVanillaFx.CloneAshUmbrella</c>).
        /// </summary>
        public readonly bool Umbrella;

        public EruptionColumnSegment(float offsetX, float offsetY, float offsetZ,
                                     float radiusMetres, float halfHeightMetres,
                                     float driftX, float driftY, float driftZ,
                                     float magnitude, bool umbrella)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RadiusMetres = radiusMetres;
            HalfHeightMetres = halfHeightMetres;
            DriftX = driftX;
            DriftY = driftY;
            DriftZ = driftZ;
            Magnitude = magnitude;
            Umbrella = umbrella;
        }
    }

    /// <summary>
    /// The shape and density of the <b>eruption column</b>. **Pure, engine-free functions
    /// only.** Treated the same way as ④'s <c>TyphoonSpiral</c> and ③'s
    /// <c>CloudAnimation</c> (the missile mod): **the state lives in Core and the particles
    /// in Game**.
    ///
    /// ── Why it was rebuilt (2026-08-22, on-hardware observation ③) ────────────────
    ///
    /// > The plume has ended up as just a puddle of smoke. I'd like you to take the method
    /// > from the MissileMOD's mushroom cloud as a reference and build a realistic plume
    /// > (not a mushroom cloud — please study footage of volcanic eruptions and reproduce
    /// > the behaviour).
    ///
    /// The old plume was **one** <c>RenderEffect</c> call, placing a disc of radius 40-80 m
    /// directly above the vent and spawning smoke in it. The particles rise on their own
    /// initial speed (26-48 m/s) for 7-16 seconds and then vanish, so what you get is
    /// <b>a blob of smoke floating over the crater</b>.
    /// It becomes neither a column nor an umbrella. **Exactly as observed.**
    ///
    /// ── ★★ It is not a mushroom cloud. It is an eruption column ─────────────────
    ///
    /// From the missile mod's <c>CloudAnimation</c> / <c>CloudPuffs</c> **we borrow the
    /// technique** (keep pure state parameterised by time in Core and have Game merely copy
    /// it every frame); **we do not borrow the shape**. A nuclear cloud and an eruption
    /// column are different physics:
    ///
    /// <list type="bullet">
    /// <item><b>Nuclear</b>: <b>a single bubble</b>, one fireball detaching and rising. Hence
    ///   the thin stem and the round cap, and the stem thins away and vanishes because "there
    ///   is no more supply"</item>
    /// <item><b>Volcanic</b>: <b>continuously supplied</b> from the crater. The column is
    ///   unbroken and gets fatter as it rises by entraining the surrounding air</item>
    /// </list>
    ///
    /// The cross-section this type builds is the three regions straight out of a volcanology
    /// textbook:
    ///
    /// <code>
    /// 1. gas thrust region   0 - 0.11 H     rises on the eruption's momentum. Narrow and fast
    /// 2. convective region   0.11 - 0.78 H  rises by buoyancy. While decelerating, it widens
    ///                                       almost linearly at dr/dz ≒ 0.14
    /// 3. umbrella            0.78 - 1.00 H  the level of neutral buoyancy. It stops rising and
    ///                                       spreads sideways. Far wider and flatter than the column
    /// </code>
    ///
    /// On top of that it <b>leans downwind</b>. The lean is stronger the higher it is
    /// (<c>(z/H)^1.5</c>), so the column bows rather than standing straight. Ash falls out of
    /// the umbrella, so **only the downwind side is long, low and thin** — which is why the
    /// umbrella is split into three segments (the body, the downwind side and the fallout
    /// tail).
    ///
    /// ── Splitting into segments (how to draw a column with vanilla particle effects) ────
    ///
    /// We do not use our own <c>ParticleSystem</c>. <c>Shader.Find</c> fails for everything
    /// in this environment, <c>"Standard"</c> included (IL facts §D-3 / <c>ShaderPool</c>),
    /// so the only thing we can actually draw with is **the game's own particle effects**.
    /// So we split the column into <see cref="MaxSegments"/> segments and, for each one,
    /// spawn a single <b>cylinder</b> via
    /// <c>SpawnArea(position, up, radius, height)</c>.
    /// The shape comes from "where we spawn", never from the particles' own initial speed —
    /// rely on that and they keep rising for their whole lifetime, and the umbrella gets no
    /// ceiling.
    ///
    /// ── The density is normalised by area (**get this wrong and the particles flood**) ────
    ///
    /// The number of particles spawned per frame is
    /// <c>max(100, π r²) × dt × magnitude × 0.01 × rateOverTime</c>
    /// (measured from the IL, §B-4). Hand a 700 m umbrella the same magnitude as the column
    /// and, by area, you fire **70 times** as many particles. <see cref="SegmentAt"/> returns
    ///
    /// <code>
    /// magnitude_i = (refRadius² × PlumeMagnitude(unit)) × weight_i / radius_i²
    /// </code>
    ///
    /// so **each segment's particle count is proportional to weight_i regardless of its
    /// radius**. The weights sum to 1, so <b>the whole column stays within the same budget as
    /// the old single call</b>.
    /// <c>refRadius</c> and <c>PlumeMagnitude</c> come from the same
    /// <see cref="EruptionEffectPlan"/> as the old plume —
    /// **the eruption's strength envelope stays a single curve.**
    /// </summary>
    public struct EruptionColumn
    {
        /// <summary>The number of segments (<see cref="ColumnSegments"/> for the column plus
        /// <see cref="UmbrellaSegments"/> for the umbrella).</summary>
        public const int MaxSegments = 9;

        /// <summary>The number of column segments. **They are shorter lower down** (things
        /// change fastest near the vent).</summary>
        public const int ColumnSegments = 6;

        /// <summary>The number of umbrella segments (the body, the downwind side and the
        /// fallout tail).</summary>
        public const int UmbrellaSegments = 3;

        /// <summary>The column height (m) of the weakest eruption. **A presentation value ⑤
        /// chose.**</summary>
        public const float HeightMinMetres = 420f;

        /// <summary>The column height (m) of the strongest eruption. Likewise.</summary>
        public const float HeightMaxMetres = 1800f;

        /// <summary>The reference (m) for adjusting the height by the crater's size. The
        /// default stratovolcano's crater radius.</summary>
        public const float ReferenceVentRadiusMetres = 144f;

        /// <summary>The floor on the height multiplier from the crater's size.</summary>
        public const float MinVentScale = 0.6f;

        /// <summary>The ceiling on the same.</summary>
        public const float MaxVentScale = 1.4f;

        /// <summary>The top of the gas thrust region (as a ratio of the column's
        /// height).</summary>
        public const float GasThrustFraction = 0.11f;

        /// <summary>The bottom of the umbrella (same units). This is the level of neutral
        /// buoyancy.</summary>
        public const float UmbrellaBaseFraction = 0.78f;

        /// <summary>The column's radius at the vent (as a ratio of the crater's radius). It
        /// does not erupt across the crater's full width.</summary>
        public const float VentRadiusFactor = 0.60f;

        /// <summary>The radius at the top of the gas thrust region (same units). It has
        /// barely widened yet.</summary>
        public const float GasTopRadiusFactor = 0.95f;

        /// <summary>
        /// How much it widens per metre of rise in the convective region (dimensionless).
        /// By entraining the surrounding air, the plume column's radius grows roughly in
        /// proportion to the height. **It is a presentation value, but the order of magnitude
        /// matches the textbook's, around 0.1.**
        /// </summary>
        public const float EntrainmentSlope = 0.14f;

        /// <summary>How many times the radius at the top of the convective region the
        /// umbrella's radius is.</summary>
        public const float UmbrellaSpread = 2.8f;

        /// <summary>The exponent biasing the column's divisions downwards (1 gives even
        /// spacing).</summary>
        public const float SegmentBias = 1.35f;

        /// <summary>How far it leans downwind (as a ratio of the column's height, at the
        /// reference wind speed).</summary>
        public const float BendFactor = 0.30f;

        /// <summary>The lean's exponent. **The larger it is, the more "only the top gets
        /// carried away"** (wind shear).</summary>
        public const float BendPower = 1.5f;

        /// <summary>The reference wind speed for the lean (m/s).</summary>
        public const float ReferenceWindMetresPerSecond = 12f;

        /// <summary>The exponent by which the wind's carrying speed grows with
        /// height.</summary>
        public const float WindSharePower = 0.7f;

        /// <summary>The rise speed at the vent (m/s). **A presentation value.**</summary>
        public const float RiseVentMetresPerSecond = 34f;

        /// <summary>The deceleration exponent until the rise stops (the larger it is, the
        /// faster low down and the more abruptly it stops up top).</summary>
        public const float RiseDecayPower = 1.3f;

        /// <summary>The umbrella's slow sinking (m/s). This is the side the ash falls
        /// from.</summary>
        public const float FalloutMetresPerSecond = 2.5f;

        /// <summary>The period (seconds) over which the column sways slowly from side to
        /// side. **A drift, not a flicker.**</summary>
        public const float SwaySeconds = 37f;

        /// <summary>
        /// The sway's amplitude (radians). **It rotates the wind direction itself**, so the
        /// furthest edge of the umbrella moves sideways by this angle × that distance
        /// (about ±230 m by default). Being smaller than the umbrella's radius, it reads as
        /// "the outline slowly blurring". **Do not make it any larger** — larger and the
        /// umbrella swings its head about, moving the spawn region faster than the wind speed
        /// itself.
        /// </summary>
        public const float SwayRadians = 0.10f;

        /// <summary>A radius below this is not used (m). The same floor as
        /// <see cref="EruptionEffectPlan"/>'s.</summary>
        public const float MinRadiusMetres = EruptionEffectPlan.MinRadiusMetres;

        /// <summary>
        /// The per-segment weights (how the particle count is shared out). **They sum to 1.**
        /// The first <see cref="ColumnSegments"/> are the column and the rest the umbrella.
        /// It is denser lower down because the ash is denser and darker nearer the vent.
        ///
        /// ★ **The column and the umbrella are separate particle systems (separate clones),
        ///   so the ratio between the two has no effect on real hardware** — both are capped
        ///   by <c>maxParticles</c>. What does have an effect is <b>the ratio within one
        ///   system</b>, which sets the density across the column's 6 segments and across the
        ///   umbrella's 3.
        ///   The column's weights were flattened out because only the bottom looked dense
        ///   while the middle looked sparse (confirmed in the plume images from
        ///   <c>tools/VolcanoPreview</c>).
        /// </summary>
        private static readonly float[] Weights =
        {
            0.16f, 0.14f, 0.13f, 0.12f, 0.11f, 0.10f,   // the column (bottom → top)
            0.11f, 0.08f, 0.05f,                        // the umbrella (body, downwind, tail)
        };

        private readonly float _ventRadius;
        private readonly float _height;
        private readonly float _gasTop;
        private readonly float _umbrellaBase;
        private readonly float _gasTopRadius;
        private readonly float _umbrellaRadius;
        private readonly float _bendMetres;
        private readonly float _windX;
        private readonly float _windZ;
        private readonly float _windSpeed;
        private readonly float _budget;

        /// <summary>The column's height (m, up to the top of the umbrella).</summary>
        public float HeightMetres { get { return _height; } }

        /// <summary>The umbrella's radius (m).</summary>
        public float UmbrellaRadiusMetres { get { return _umbrellaRadius; } }

        /// <summary>The height of the umbrella's bottom (m) = the level of neutral
        /// buoyancy.</summary>
        public float UmbrellaBaseMetres { get { return _umbrellaBase; } }

        /// <summary>How far the column's top is displaced downwind (m).</summary>
        public float BendMetres { get { return _bendMetres; } }

        /// <summary>The number of segments. **Always <see cref="MaxSegments"/>** (a fixed
        /// length, so that no array need be allocated).</summary>
        public int SegmentCount { get { return MaxSegments; } }

        /// <summary>
        /// Builds one eruption column. **Zero heap allocation** (it is a <c>struct</c>), so
        /// it is fine to build one every frame.
        /// </summary>
        /// <param name="ventRadiusMetres">The crater's radius (m). The reference for the
        /// column's width and height.</param>
        /// <param name="intensityUnit">The eruption's strength <c>[0,1]</c>
        /// (<c>VolcanoEruption.IntensityUnit</c>).</param>
        /// <param name="windX">The wind direction (a unit vector; we normalise inside, so the
        /// length does not matter).</param>
        /// <param name="windZ">The same.</param>
        /// <param name="windMetresPerSecond">The wind speed (m/s).</param>
        public EruptionColumn(float ventRadiusMetres, float intensityUnit,
                              float windX, float windZ, float windMetresPerSecond)
        {
            float vent = IsBad(ventRadiusMetres) || ventRadiusMetres < MinRadiusMetres
                ? MinRadiusMetres : ventRadiusMetres;
            float unit = Clamp01(intensityUnit);

            _ventRadius = vent;

            float ventScale = Clamp(0.55f + 0.45f * (vent / ReferenceVentRadiusMetres),
                                    MinVentScale, MaxVentScale);
            _height = (HeightMinMetres + (HeightMaxMetres - HeightMinMetres) * unit) * ventScale;

            _gasTop = _height * GasThrustFraction;
            _umbrellaBase = _height * UmbrellaBaseFraction;
            _gasTopRadius = vent * GasTopRadiusFactor;

            float convectiveTop = _gasTopRadius + EntrainmentSlope * (_umbrellaBase - _gasTop);
            _umbrellaRadius = convectiveTop * UmbrellaSpread;

            // The wind. A length of 0 (no wind) means "it does not lean", not NaN.
            float length = (float)Math.Sqrt(windX * windX + windZ * windZ);
            if (IsBad(length) || length <= 0f)
            {
                _windX = 0f;
                _windZ = 0f;
            }
            else
            {
                _windX = windX / length;
                _windZ = windZ / length;
            }

            float speed = IsBad(windMetresPerSecond) || windMetresPerSecond < 0f
                ? 0f : windMetresPerSecond;
            _windSpeed = speed > ReferenceWindMetresPerSecond * 2f
                ? ReferenceWindMetresPerSecond * 2f : speed;

            _bendMetres = _height * BendFactor
                          * (_windSpeed / ReferenceWindMetresPerSecond);

            // ★ The particle budget is the same as one call's worth of the old plume (the
            //   normalisation in the class doc).
            float refRadius = EruptionEffectPlan.PlumeRadiusMetres(vent, unit);
            _budget = refRadius * refRadius * EruptionEffectPlan.PlumeMagnitude(unit);
        }

        /// <summary>
        /// How to spawn segment number <paramref name="index"/>. **Out of range it returns a
        /// segment with a density of 0** (so the caller need only draw while
        /// <c>Magnitude &gt; 0</c>).
        /// </summary>
        public EruptionColumnSegment SegmentAt(int index)
        {
            if (index < 0 || index >= MaxSegments)
            {
                return new EruptionColumnSegment(0f, 0f, 0f, MinRadiusMetres, 0f,
                                                 0f, 0f, 0f, 0f, false);
            }

            return index < ColumnSegments ? ColumnSegment(index) : UmbrellaSegment(index);
        }

        /// <summary>One segment of the column: a cylinder from its lower edge <c>z0</c> to its
        /// upper edge <c>z1</c>.</summary>
        private EruptionColumnSegment ColumnSegment(int index)
        {
            float z0 = ColumnBoundary(index);
            float z1 = ColumnBoundary(index + 1);
            float mid = (z0 + z1) * 0.5f;

            float radius = RadiusAt(mid);
            float offset = BendAt(mid);

            float share = WindShareAt(mid);
            float rise = RiseAt(mid);

            return new EruptionColumnSegment(
                _windX * offset, z0, _windZ * offset,
                radius, z1 - z0,
                _windX * _windSpeed * share, rise, _windZ * _windSpeed * share,
                MagnitudeFor(index, radius), false);
        }

        /// <summary>
        /// One segment of the umbrella. <b>0 = the body, 1 = the downwind side, 2 = the
        /// fallout tail</b>, getting lower and thinner the further downwind (ash falls from
        /// the umbrella's underside, so only the downwind side trails out long).
        /// </summary>
        private EruptionColumnSegment UmbrellaSegment(int index)
        {
            int i = index - ColumnSegments;

            float alongFactor = i == 0 ? 0f : (i == 1 ? 1.0f : 2.1f);
            float dropFactor = i == 0 ? 0f : (i == 1 ? 0.04f : 0.13f);
            float radiusFactor = i == 0 ? 0.80f : (i == 1 ? 0.65f : 0.75f);
            float pushFactor = i == 0 ? 0.55f : (i == 1 ? 0.80f : 0.95f);
            float fall = i == 0 ? 0f : (i == 1 ? -FalloutMetresPerSecond * 0.4f
                                               : -FalloutMetresPerSecond);

            float along = BendAt(_height) + _umbrellaRadius * alongFactor;
            float y = _umbrellaBase - _height * dropFactor;
            if (y < 0f) y = 0f;

            float thickness = _height - _umbrellaBase;
            float radius = _umbrellaRadius * radiusFactor;
            if (radius < MinRadiusMetres) radius = MinRadiusMetres;

            return new EruptionColumnSegment(
                _windX * along, y, _windZ * along,
                radius, thickness,
                _windX * _windSpeed * pushFactor, fall, _windZ * _windSpeed * pushFactor,
                MagnitudeFor(index, radius), true);
        }

        /// <summary>The height (m) of the column's <paramref name="index"/>-th boundary.
        /// Finer divisions lower down.</summary>
        public float ColumnBoundary(int index)
        {
            if (index <= 0) return 0f;
            if (index >= ColumnSegments) return _umbrellaBase;

            float t = index / (float)ColumnSegments;
            return _umbrellaBase * (float)Math.Pow(t, SegmentBias);
        }

        /// <summary>
        /// The column's radius (m) at a height of <paramref name="metres"/>.
        /// Almost constant in the gas thrust region; in the convective region it <b>widens
        /// roughly in proportion to the height</b> (entrainment).
        /// </summary>
        public float RadiusAt(float metres)
        {
            float r0 = _ventRadius * VentRadiusFactor;

            float r;
            if (IsBad(metres) || metres <= 0f)
            {
                r = r0;
            }
            else if (metres <= _gasTop)
            {
                float t = _gasTop > 0f ? metres / _gasTop : 1f;
                r = r0 + (_gasTopRadius - r0) * t;
            }
            else
            {
                float top = metres > _umbrellaBase ? _umbrellaBase : metres;
                r = _gasTopRadius + EntrainmentSlope * (top - _gasTop);
            }

            // ★ Apply the floor in exactly one place. **Particles spawn even if you pass a
            //   radius of 0** (the area floor is max(100, πr²), so it would erupt from a
            //   single point; §B-4).
            return r < MinRadiusMetres ? MinRadiusMetres : r;
        }

        /// <summary>The downwind displacement (m) at a height of
        /// <paramref name="metres"/>.</summary>
        public float BendAt(float metres)
        {
            if (IsBad(metres) || metres <= 0f || !(_height > 0f)) return 0f;

            float t = metres / _height;
            if (t > 1f) t = 1f;
            return _bendMetres * (float)Math.Pow(t, BendPower);
        }

        /// <summary>The rise speed (m/s) at a height of <paramref name="metres"/>. 0 in the
        /// umbrella.</summary>
        public float RiseAt(float metres)
        {
            if (IsBad(metres) || !(_umbrellaBase > 0f)) return 0f;
            if (metres >= _umbrellaBase) return 0f;

            float t = metres <= 0f ? 0f : metres / _umbrellaBase;
            float left = 1f - t;
            return RiseVentMetresPerSecond * (float)Math.Pow(left, RiseDecayPower);
        }

        /// <summary>How much the wind carries it at a height of <paramref name="metres"/>,
        /// in <c>[0,1]</c>.</summary>
        public float WindShareAt(float metres)
        {
            if (IsBad(metres) || metres <= 0f || !(_height > 0f)) return 0f;

            float t = metres / _height;
            if (t > 1f) t = 1f;
            return (float)Math.Pow(t, WindSharePower);
        }

        /// <summary>
        /// A segment's density. **Normalised by area**, so the particle count is proportional
        /// to the weight regardless of the radius (see the class doc). A bad radius never
        /// lets a division by zero out.
        /// </summary>
        public float MagnitudeFor(int index, float radiusMetres)
        {
            if (index < 0 || index >= Weights.Length) return 0f;

            float r = IsBad(radiusMetres) || radiusMetres < MinRadiusMetres
                ? MinRadiusMetres : radiusMetres;
            float area = r * r;
            if (!(area > 0f)) return 0f;

            float m = _budget * Weights[index] / area;
            if (IsBad(m) || m < 0f) return 0f;
            return m;
        }

        /// <summary>
        /// The angle (radians) by which the column sways slowly from side to side.
        /// **A single sine of period <see cref="SwaySeconds"/> and nothing more** — add a
        /// faster component and it becomes "a flicker" rather than "a drift"
        /// (we made the same mistake with the lava's glow; see <c>LavaGlow</c>'s class doc).
        /// </summary>
        public static float SwayAt(float clockSeconds)
        {
            if (IsBad(clockSeconds)) return 0f;
            return SwayRadians * (float)Math.Sin(2.0 * Math.PI * clockSeconds / SwaySeconds);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (IsBad(v)) return min;
            if (v < min) return min;
            return v > max ? max : v;
        }

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
