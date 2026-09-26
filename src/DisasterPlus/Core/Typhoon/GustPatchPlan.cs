using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// How local damage areas ("patches") are scattered, and how long they live, so as to
    /// **produce tornado-level damage without producing a tornado**.
    /// <b>This is Core, so it never touches the engine.</b>
    ///
    /// ── The owner's instruction ──────────────────────────────────────
    ///
    /// > Produce several instances of tornado damage without spawning any tornadoes
    ///
    /// So we create <b>neither the disaster itself (<c>TornadoAI</c>) nor a funnel mesh</b>.
    /// All we create is the phenomenon: "here and there beneath the typhoon, for a short
    /// while, a narrow area gets wrecked as if by a tornado". The old implementation that
    /// borrowed vanilla's tornado (the accompanying tornado) has been retired.
    ///
    /// **The benefit of dropping it is worth recording too.** Vanilla tornado damage goes
    /// through <c>DisasterHelpers.DestroyStuff</c>, which Natural Disasters Renewal replaces
    /// wholesale. A patch calls <c>BuildingAI.CollapseBuilding</c> directly (the same path as
    /// ④'s wind damage), so it is now **entirely free of conflict with NDR**.
    ///
    /// ── They are scattered in generations ────────────────────────────────
    ///
    /// One patch is born every <see cref="SpawnIntervalFrames"/> frames and dies after
    /// <see cref="LifetimeFrames"/> frames. So the number alive at once is
    /// <c>ceil(Lifetime / Interval)</c>, and <see cref="MaxActivePatches"/> declares that
    /// upper bound (tests pin down that the two agree).
    /// **This is the first of the things that cap the work done per tick.**
    ///
    /// The only state is "which patch this is" (its ordinal); its position, its size and its
    /// lifetime are all **functions of the ordinal**. The game side asks for the range of
    /// ordinals and rebuilds the positions on the spot — we carry no ledger around, so
    /// **when the typhoon goes, not one patch is left behind** (the failure mode of
    /// forgetting to manage a lifetime and leaving something behind cannot arise
    /// structurally).
    ///
    /// ── They favour the dangerous semicircle ────────────────────────────────
    ///
    /// Patches favour the dangerous semicircle relative to the direction of travel (the
    /// right in the northern hemisphere; see <see cref="TrackBias"/>). Some appear on the
    /// other side, but **the majority are in the dangerous semicircle** (tests pin the
    /// distribution down).
    ///
    /// The only randomness is <see cref="DeterministicRandom"/>. We do not use
    /// <c>VanillaRandomizer</c> — what is decided here is not a value vanilla draws but a
    /// judgement ④ invented.
    /// **Do not mix in the frame** (mix it in and the patches change position every tick and
    /// teleport).
    /// </summary>
    public static class GustPatchPlan
    {
        /// <summary>The interval at which a new patch is born (in the typhoon's elapsed
        /// frames).</summary>
        public const uint SpawnIntervalFrames = 128u;

        /// <summary>How many frames one patch lives. It must be **short-lived**.</summary>
        public const uint LifetimeFrames = 320u;

        /// <summary>The cap on patches alive at once. <see cref="AliveRange"/> never exceeds
        /// it.</summary>
        public const int MaxActivePatches = 4;

        /// <summary>The lower and upper bounds on a patch's radius (m). **Keep them narrow**
        /// — this is the damage width of a tornado.</summary>
        public const float MinRadiusMetres = 45f;

        public const float MaxRadiusMetres = 95f;

        /// <summary>The lower and upper bounds on the radius a patch is placed at, ÷ the
        /// gale radius. They are scattered between just outside the eye and the edge of the
        /// gale zone.</summary>
        public const float MinOrbitFraction = 0.25f;

        public const float MaxOrbitFraction = 0.95f;

        /// <summary>How strongly they favour the dangerous semicircle. 1 is uniform, 2 and
        /// above favours it. We use **squaring**.</summary>
        private const float BiasExponent = 2f;

        private const float Pi = 3.14159265f;
        private const float HalfPi = 1.57079633f;

        /// <summary>The salts mixed in when making a random seed from an ordinal. **Fixed
        /// values.**</summary>
        private const uint AngleSalt = 0x47555331u;    // "GUS1"
        private const uint SpreadSalt = 0x47555332u;
        private const uint OrbitSalt = 0x47555333u;
        private const uint RadiusSalt = 0x47555334u;
        private const uint StrengthSalt = 0x47555335u;

        /// <summary>The elapsed frame at which the patch with ordinal
        /// <paramref name="ordinal"/> is born.</summary>
        public static uint BirthFrameOf(uint ordinal)
        {
            return ordinal * SpawnIntervalFrames;
        }

        /// <summary>
        /// The range of ordinals alive at <paramref name="elapsedFrames"/>,
        /// <c>[first, last]</c> (inclusive at both ends). False if none is alive.
        ///
        /// **The count returned is always at most <see cref="MaxActivePatches"/>** (pinned
        /// down by tests).
        /// </summary>
        public static bool AliveRange(uint elapsedFrames, out uint first, out uint last)
        {
            first = 0u;
            last = 0u;

            // The last ordinal born so far.
            last = elapsedFrames / SpawnIntervalFrames;

            // Only those born less than LifetimeFrames ago are alive.
            uint oldestBirth = elapsedFrames >= LifetimeFrames
                ? elapsedFrames - LifetimeFrames + 1u
                : 0u;

            // Ceiling division (the smallest ordinal born at or after that frame).
            first = (oldestBirth + SpawnIntervalFrames - 1u) / SpawnIntervalFrames;

            if (first > last) return false;

            // Trim at the cap. **Keep the newer ones** (the older ones are already fading).
            if (last - first + 1u > (uint)MaxActivePatches)
            {
                first = last - (uint)MaxActivePatches + 1u;
            }
            return true;
        }

        /// <summary>
        /// Where we are in a patch's life, in [0, 1]. 0 the instant it is born, 1 the instant
        /// it dies. For an ordinal that is not alive it returns 1 (= it is already over).
        /// </summary>
        public static float LifePhase(uint ordinal, uint elapsedFrames)
        {
            uint birth = BirthFrameOf(ordinal);
            if (elapsedFrames <= birth) return 0f;

            uint age = elapsedFrames - birth;
            if (age >= LifetimeFrames) return 1f;
            return (float)age / LifetimeFrames;
        }

        /// <summary>
        /// Where one patch sits and how big it is. **A function of the ordinal alone**, so
        /// the same answer comes back every tick without the caller keeping a ledger.
        ///
        /// <paramref name="relativeAngleRadians"/> is <b>the angle relative to the heading</b>
        /// (not a world angle). The caller turns it into a world angle with
        /// <c>heading + relativeAngle</c> — done this way, **when the track bends the
        /// scatter of patches turns with it**.
        ///
        /// <paramref name="orbitFraction"/> is a ratio of the gale radius,
        /// <paramref name="radiusMetres"/> is the patch's own radius (m), and
        /// <paramref name="strengthFraction"/> is the destructive-power ratio in [0, 1].
        /// </summary>
        public static void Patch(ushort typhoonId, uint ordinal, bool southernHemisphere,
                                 out float relativeAngleRadians, out float orbitFraction,
                                 out float radiusMetres, out float strengthFraction)
        {
            uint id = typhoonId;

            // The direction to the centre of the dangerous semicircle (relative to the
            // heading). TrackBias's right = (sin φ, -cos φ) points along the angle φ - 90°,
            // so the northern hemisphere's dangerous semicircle is at a relative angle of
            // -90° and the southern hemisphere's at +90°.
            float centre = southernHemisphere ? HalfPi : -HalfPi;

            // The deviation from the dangerous semicircle. Squaring pulls it towards 0
            // (i.e. towards the dangerous semicircle).
            float u = DeterministicRandom.Unit(id ^ SpreadSalt, ordinal);
            float spread = Pi * Power(u, BiasExponent);

            float side = DeterministicRandom.Unit(id ^ AngleSalt, ordinal) < 0.5f ? -1f : 1f;
            relativeAngleRadians = centre + side * spread;

            orbitFraction = MinOrbitFraction
                + (MaxOrbitFraction - MinOrbitFraction)
                  * DeterministicRandom.Unit(id ^ OrbitSalt, ordinal);

            radiusMetres = MinRadiusMetres
                + (MaxRadiusMetres - MinRadiusMetres)
                  * DeterministicRandom.Unit(id ^ RadiusSalt, ordinal);

            // 0.6-1.0. **Never 0** — a patch that destroys nothing is indistinguishable from
            // one that never appeared, which makes the diagnostics unreadable.
            strengthFraction = 0.6f
                + 0.4f * DeterministicRandom.Unit(id ^ StrengthSalt, ordinal);
        }

        /// <summary>
        /// Squaring without <c>System.Math.Pow</c> (<see cref="BiasExponent"/> is fixed at
        /// 2). If you change the exponent, fix this too.
        /// </summary>
        private static float Power(float value, float exponent)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            return exponent == 2f ? value * value : value;
        }
    }
}
