namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The properties of a single ash-plume particle. **This is a table of numbers written
    /// straight into the clones of vanilla's <c>ParticleEffect</c> + <c>ParticleSystem</c>**
    /// (see <c>Game/Volcano/VolcanoVanillaFx.Clones</c>).
    ///
    /// ── Why it lives in Core ─────────────────────────────────
    ///
    /// These are ⑤'s presentation values, deciding "how the particles look"; they are not
    /// Unity API. Keeping them in Core is what lets <c>tools/VolcanoPreview</c> **draw the
    /// eruption column with the same numbers without launching the game** — this project's
    /// rule that "a visual change gets rendered and measured offline by me before I ask for
    /// a playtest" cannot hold if the numbers live in two places.
    ///
    /// ── What they map to in the game (measured from IL, §A-6 / §B-4) ────────────
    ///
    /// <code>
    /// LifeMinSeconds / LifeMaxSeconds  -> ParticleEffect.m_min/maxLifeTime
    /// SpeedMin / SpeedMax              -> ParticleEffect.m_min/maxStartSpeed
    /// SpawnAngleMinDegrees / Max       -> ParticleEffect.m_min/maxSpawnAngle
    /// SizeMetres                       -> ParticleSystem.main.startSize
    /// GravityModifier                  -> ParticleSystem.main.gravityModifier
    /// RateOverTime                     -> ParticleSystem.emission.rateOverTime  ★ never 0
    /// MaxParticles                     -> ParticleSystem.main.maxParticles
    /// </code>
    ///
    /// Set <c>rateOverTime</c> to 0 and not one particle comes out (even with
    /// <c>emission.enabled</c> false, <c>EmitParticles</c> keeps reading it as a multiplier
    /// on the particle count). It is the easiest trap here to fall into.
    /// </summary>
    public struct EruptionAshProfile
    {
        /// <summary>Lower bound on lifetime (seconds).</summary>
        public readonly float LifeMinSeconds;

        /// <summary>Upper bound on lifetime (seconds).</summary>
        public readonly float LifeMaxSeconds;

        /// <summary>Lower bound on initial speed (m/s). **The column's shape comes from where
        /// the particles are spawned, not from their initial speed.**</summary>
        public readonly float SpeedMin;

        /// <summary>Upper bound on initial speed (m/s).</summary>
        public readonly float SpeedMax;

        /// <summary>Lower bound on emission angle (degrees). 0 is along the axis (i.e. up), 90
        /// is straight sideways.</summary>
        public readonly float SpawnAngleMinDegrees;

        /// <summary>Upper bound on emission angle (degrees).</summary>
        public readonly float SpawnAngleMaxDegrees;

        /// <summary>The size of one particle (m).</summary>
        public readonly float SizeMetres;

        /// <summary>Gravity multiplier (negative means it floats up).</summary>
        public readonly float GravityModifier;

        /// <summary>Multiplier on the particle count. **Never 0.**</summary>
        public readonly float RateOverTime;

        /// <summary>The cap on how many particles this system holds. Anything past it is
        /// throttled automatically (§B-4).</summary>
        public readonly int MaxParticles;

        public EruptionAshProfile(float lifeMinSeconds, float lifeMaxSeconds,
                                  float speedMin, float speedMax,
                                  float spawnAngleMinDegrees, float spawnAngleMaxDegrees,
                                  float sizeMetres, float gravityModifier,
                                  float rateOverTime, int maxParticles)
        {
            LifeMinSeconds = lifeMinSeconds;
            LifeMaxSeconds = lifeMaxSeconds;
            SpeedMin = speedMin;
            SpeedMax = speedMax;
            SpawnAngleMinDegrees = spawnAngleMinDegrees;
            SpawnAngleMaxDegrees = spawnAngleMaxDegrees;
            SizeMetres = sizeMetres;
            GravityModifier = gravityModifier;
            RateOverTime = rateOverTime;
            MaxParticles = maxParticles;
        }

        /// <summary>
        /// The <b>column</b> particles. Dark greyish-brown, **short-lived** (4-9 s), and
        /// barely self-propelled.
        ///
        /// ★★ Cutting the initial speed is the substance of the fix for point ③. The
        ///   stock <c>Factory Smoke</c> is 10-15 m/s, and ⑤ itself used to put 26-48 m/s
        ///   in. If the particles keep climbing under their own steam, **the umbrella
        ///   never gets a ceiling** — stopping at the level of neutral buoyancy and
        ///   spreading sideways is what an eruption column actually looks like.
        ///   Now the shape is decided by <see cref="EruptionColumn"/>'s nine tiers, and
        ///   the particles just swell a little where they were spawned and disappear.
        /// </summary>
        public static EruptionAshProfile Column
        {
            get { return new EruptionAshProfile(4f, 9f, 3f, 11f, 0f, 22f, 36f, -0.02f, 42f, 6000); }
        }

        /// <summary>
        /// The <b>umbrella</b> particles. Pale, large and **long-lived** (18-34 s), with an
        /// emission angle of 55-95 degrees, i.e. **spreading out almost horizontally**
        /// (the axis points up, so that angle becomes sideways initial speed as it stands).
        /// They linger and stack up, which is what makes the flat sheet.
        /// </summary>
        public static EruptionAshProfile Umbrella
        {
            get { return new EruptionAshProfile(18f, 34f, 5f, 13f, 55f, 95f, 95f, 0.01f, 30f, 6000); }
        }
    }
}
