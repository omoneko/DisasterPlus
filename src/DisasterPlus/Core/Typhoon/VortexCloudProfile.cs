namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// The properties of one particle of the vortex cloud. **A table of numbers written
    /// straight into clones of vanilla's <c>ParticleEffect</c> + <c>ParticleSystem</c>**
    /// (<c>Game/Typhoon/TyphoonCloudFx.Clones</c>).
    ///
    /// ── Why it lives in Core ─────────────────────────────────
    ///
    /// Exactly the same reason as ⑤'s <c>EruptionAshProfile</c>. These are ④'s presentation
    /// values deciding "how the particles look"; they are not a Unity API.
    /// Because they live in Core, <c>tools/TyphoonPreview</c> can **draw the vortex with the
    /// same numbers without launching the game** — this project's rule, "for a visual change,
    /// render and measure it yourself offline before asking for a test on real hardware",
    /// does not hold if the numbers live in two places.
    ///
    /// ── What they map to in-game (measured-effects document §A-6 / §B-4) ─────────
    ///
    /// <code>
    /// LifeMinSeconds / LifeMaxSeconds  -> ParticleEffect.m_min/maxLifeTime
    /// SpeedMin / SpeedMax              -> ParticleEffect.m_min/maxStartSpeed
    /// SpawnAngleMinDegrees / Max       -> ParticleEffect.m_min/maxSpawnAngle
    /// SizeFraction x the vortex's outer radius -> ParticleSystem.main.startSize
    /// GravityModifier                  -> ParticleSystem.main.gravityModifier
    /// RateOverTime                     -> ParticleSystem.emission.rateOverTime  ★ never 0
    /// MaxParticles                     -> ParticleSystem.main.maxParticles
    /// Red/Green/Blue Bright/Dark       -> ParticleSystem.main.startColor (a gradient between two colours)
    /// </code>
    ///
    /// Set <c>rateOverTime</c> to 0 and not a single particle is emitted (even with
    /// <c>emission.enabled</c> false, <c>EmitParticles</c> keeps reading it as the particle
    /// count multiplier). It is the easiest trap of all to fall into.
    ///
    /// ── ★ The colours are those of a cumulonimbus, exactly ───────────────────
    ///
    /// A cumulonimbus is <b>white on top where the sun catches it, and dark underneath where
    /// its own thickness stops the light</b>. Particle colour is shared state on the
    /// <c>ParticleSystem</c> and cannot be varied per <c>RenderEffect</c> (§B-4), so
    /// **the number of units whose brightness we want to vary is exactly the number of
    /// clones**. ④ splits it into three (deck, tower, anvil).
    ///
    /// The old implementation put a uniform grey, <c>(0.60, 0.62, 0.66, 0.62)</c>, into a
    /// single clone. With no light and shade, whatever shape you arrange it into it only
    /// ever looks like **a blob of smoke**.
    /// </summary>
    public struct VortexCloudProfile
    {
        /// <summary>The lower bound on lifetime (seconds).</summary>
        public readonly float LifeMinSeconds;

        /// <summary>The upper bound on lifetime (seconds).</summary>
        public readonly float LifeMaxSeconds;

        /// <summary>The lower bound on initial speed (m/s). **The vortex's shape comes from
        /// where we spawn particles, not from their initial speed.**</summary>
        public readonly float SpeedMin;

        /// <summary>The upper bound on initial speed (m/s).</summary>
        public readonly float SpeedMax;

        /// <summary>The lower bound on the emission angle (degrees). 0 is along the axis
        /// (i.e. up) and 90 is straight out sideways.</summary>
        public readonly float SpawnAngleMinDegrees;

        /// <summary>The upper bound on the emission angle (degrees).</summary>
        public readonly float SpawnAngleMaxDegrees;

        /// <summary>One particle's size ÷ the vortex's outer radius.</summary>
        public readonly float SizeFraction;

        /// <summary>The gravity multiplier (negative to float upwards).</summary>
        public readonly float GravityModifier;

        /// <summary>The particle count multiplier. **Never 0.**</summary>
        public readonly float RateOverTime;

        /// <summary>The cap on particles this system holds. Anything beyond it is throttled
        /// automatically (§B-4).</summary>
        public readonly int MaxParticles;

        /// <summary>The colour on the bright side (0..1).</summary>
        public readonly float BrightRed;

        public readonly float BrightGreen;

        public readonly float BrightBlue;

        /// <summary>The colour on the dark side (0..1). <c>startColor</c> is drawn from
        /// between these two colours.</summary>
        public readonly float DarkRed;

        public readonly float DarkGreen;

        public readonly float DarkBlue;

        /// <summary>Opacity (0..1). Never 1, because **they stack up and get denser**.</summary>
        public readonly float Alpha;

        public VortexCloudProfile(float lifeMinSeconds, float lifeMaxSeconds,
                                  float speedMin, float speedMax,
                                  float spawnAngleMinDegrees, float spawnAngleMaxDegrees,
                                  float sizeFraction, float gravityModifier,
                                  float rateOverTime, int maxParticles,
                                  float brightRed, float brightGreen, float brightBlue,
                                  float darkRed, float darkGreen, float darkBlue,
                                  float alpha)
        {
            LifeMinSeconds = lifeMinSeconds;
            LifeMaxSeconds = lifeMaxSeconds;
            SpeedMin = speedMin;
            SpeedMax = speedMax;
            SpawnAngleMinDegrees = spawnAngleMinDegrees;
            SpawnAngleMaxDegrees = spawnAngleMaxDegrees;
            SizeFraction = sizeFraction;
            GravityModifier = gravityModifier;
            RateOverTime = rateOverTime;
            MaxParticles = maxParticles;
            BrightRed = brightRed;
            BrightGreen = brightGreen;
            BrightBlue = brightBlue;
            DarkRed = darkRed;
            DarkGreen = darkGreen;
            DarkBlue = darkBlue;
            Alpha = alpha;
        }

        /// <summary>
        /// <b>The deck.</b> **A dark blue-grey**, large, barely moving, sinking slightly.
        /// The underside of a cumulonimbus is the side dropping the rain, so it is the
        /// darkest.
        /// The emission angle is wide so it spreads out **flat and sideways** (the axis
        /// points up, so the larger the angle the more sideways the initial speed).
        /// </summary>
        public static VortexCloudProfile Deck
        {
            get
            {
                return new VortexCloudProfile(
                    14f, 26f, 3f, 9f, 62f, 98f,
                    VortexPuffLayout.DeckSizeFraction, 0.004f, 20f, 2600,
                    0.42f, 0.45f, 0.52f,
                    0.17f, 0.19f, 0.24f,
                    0.55f);
            }
        }

        /// <summary>
        /// <b>The tower.</b> **A bright grey-white**, with smaller particles, billowing
        /// upwards. The emission angle is narrow (0-38 degrees) so the particles scatter
        /// upwards, and together with the two stacked bands
        /// <see cref="VortexPuffLayout"/> lays down they make <b>a billowing vertical
        /// mass</b>.
        /// </summary>
        public static VortexCloudProfile Tower
        {
            get
            {
                return new VortexCloudProfile(
                    11f, 22f, 5f, 14f, 0f, 38f,
                    VortexPuffLayout.TowerSizeFraction, -0.010f, 20f, 3600,
                    0.96f, 0.97f, 0.99f,
                    0.56f, 0.59f, 0.66f,
                    0.50f);
            }
        }

        /// <summary>
        /// <b>The anvil.</b> **The whitest, the largest and the longest-lived** (26-48
        /// seconds). An emission angle of 70-100 degrees = **spreading out almost
        /// horizontally**. By lingering and piling up it forms a flat canopy (built the same
        /// way as ⑤'s plume umbrella).
        /// </summary>
        public static VortexCloudProfile Canopy
        {
            get
            {
                return new VortexCloudProfile(
                    26f, 48f, 4f, 12f, 70f, 100f,
                    VortexPuffLayout.CanopySizeFraction, 0.002f, 20f, 1800,
                    1f, 1f, 1f,
                    0.74f, 0.77f, 0.84f,
                    0.34f);
            }
        }

        /// <summary>Looks up the table of numbers by layer. **Both Game and the preview go
        /// through here.**</summary>
        public static VortexCloudProfile Of(VortexCloudLayer layer)
        {
            if (layer == VortexCloudLayer.Deck) return Deck;
            if (layer == VortexCloudLayer.Canopy) return Canopy;
            return Tower;
        }
    }
}
