using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>One puff of the crowd. Its position is **a ratio, with the vortex centre as
    /// the origin**.</summary>
    public struct CrowdPuff
    {
        /// <summary>The angle from the vortex centre (radians, before the rotation is
        /// added).</summary>
        public readonly float AngleRadians;

        /// <summary>As a fraction of the vortex radius.</summary>
        public readonly float RadiusFraction;

        /// <summary>As a fraction of the cloud's thickness.</summary>
        public readonly float HeightFraction;

        /// <summary>The puff's **radius**, as a fraction of the vortex radius.</summary>
        public readonly float SizeFraction;

        /// <summary>Density <c>[0,1]</c>. Thinner further out along the arms.</summary>
        public readonly float DensityFraction;

        /// <summary>The tier it came from (deck, tower, umbrella). Used for colour and draw
        /// order.</summary>
        public readonly VortexCloudLayer Layer;

        public CrowdPuff(float angleRadians, float radiusFraction, float heightFraction,
                         float sizeFraction, float densityFraction, VortexCloudLayer layer)
        {
            AngleRadians = angleRadians;
            RadiusFraction = radiusFraction;
            HeightFraction = heightFraction;
            SizeFraction = sizeFraction;
            DensityFraction = densityFraction;
            Layer = layer;
        }
    }

    /// <summary>
    /// Expands <see cref="VortexPuffLayout"/>'s <b>88 discs</b> into **a crowd of cloud
    /// puffs that fill them in**.
    /// <b>This is Core, so it touches the engine not at all</b> (and holds no state).
    ///
    /// ── Why it is needed (2026-08-22, found by measuring offline) ───────────────
    ///
    /// The first version of the rebuild that places white cloud puffs itself
    /// (<c>Game/Typhoon/TyphoonVortexPuffFx</c>) placed one puff per disc for
    /// <see cref="VortexPuffLayout"/>'s 88. Drawn and measured in
    /// <c>tools/TyphoonPreview</c>:
    ///
    /// <code>
    /// puff area ÷ vortex area = 0.41   ← below 1.0 and holes open up
    /// </code>
    ///
    /// The picture matched the number: **it was a scatter of dots, not a vortex**. The
    /// cause is clear — those 88 are <b>discs</b>, not puffs. The old implementation
    /// <b>scattered</b> thousands of vanilla particles inside those discs, which is why
    /// they were filled. If we stop scattering and start placing, **we have to build the
    /// discs' insides ourselves too.**
    ///
    /// ── How many to place ────────────────────────────────────
    ///
    /// They are handed out in proportion to each disc's area, with the total kept to
    /// <see cref="TotalCount"/>. Proportional to area because otherwise **only the large
    /// discs on the outside come out sparse** (the same reason the old implementation
    /// re-solved for density in <c>MagnitudeFor</c>).
    ///
    /// <see cref="TotalCount"/> is the same order as the missile mod's mushroom cloud (620).
    /// That one is one per city, and a typhoon is also one at a time, so it balances out.
    ///
    /// ── ★★ Do not mix the frame number into the seed ─────────────────────────
    ///
    /// A puff's position is decided by <b>its indices alone</b> (see
    /// <see cref="DeterministicRandom"/>). Mix it in and everything jumps somewhere else
    /// every frame, giving a sandstorm rather than a cloud.
    /// The only movement is the rotation the caller adds; **the crowd's shape does not
    /// move**.
    /// </summary>
    public static class VortexPuffCrowd
    {
        /// <summary>The total number of puffs placed.</summary>
        public const int TotalCount = 640;

        /// <summary>
        /// The floor on how many puffs go to a single disc. **Not 0** — at 0 the innermost
        /// thin discs (the lower tier of the eyewall) vanish and the eye looks wider than
        /// it is.
        /// </summary>
        public const int MinPerDisc = 2;

        /// <summary>
        /// The multiplier applied to the baseline for one puff's radius (as a fraction of
        /// **the vortex radius**).
        ///
        /// ★★ <b>Do not make it proportional to the disc's size.</b> (2026-08-22, found by
        ///   drawing it.) It started out as "0.62 times that disc's radius", but the discs
        ///   round the rim are several times larger than the ones near the centre, so
        ///   **only the outside became 1.8 km blobs** and it turned into a single sheet of
        ///   cotton wool with no readable arms (confirmed in <c>docs/images/typhoon</c>).
        ///
        ///   Real cloud is a gathering of <b>similarly sized towers</b> wherever in the
        ///   vortex it sits. So the baseline is the per-tier fraction
        ///   (<c>VortexPuffLayout.SizeFractionOf</c> — deck 0.055 / tower 0.040 /
        ///   umbrella 0.070), and this multiplier is applied to that.
        /// </summary>
        public const float PuffSizeGain = 1.0f;

        /// <summary>The spread of sizes between puffs (± this fraction). **Uniform sizes look
        /// artificial.**</summary>
        public const float SizeJitter = 0.30f;

        /// <summary>
        /// How far to scatter **radially** (as a fraction of the disc's radius). This
        /// becomes the thickness of the arms.
        /// </summary>
        public const float RadialScatterRatio = 1.15f;

        /// <summary>
        /// How far to scatter **along the arm** (as a fraction of the vortex radius, one side).
        ///
        /// ★★ <b>Without this the arms do not join up.</b> (2026-08-22, found by drawing it.)
        ///   While the scattering was confined inside the discs, the 88 discs read as
        ///   **88 blobs of cotton wool** and the arms were "dotted lines"
        ///   (the second version of <c>docs/images/typhoon/vortex-owned-plan.png</c>).
        ///
        ///   The spacing to the next disc along an arm is roughly 0.2-0.3 of the vortex
        ///   radius (three arms × five columns spread over <c>SpiralTurns</c> = 0.45 of a
        ///   turn). Scatter wider than half of that and neighbours overlap into a single
        ///   band.
        /// </summary>
        public const float AlongArmScatterRatio = 0.16f;

        private const uint AngleSalt = 0x43524F57u;
        private const uint RadiusSalt = 0x43524F58u;
        private const uint HeightSalt = 0x43524F59u;
        private const uint SizeSalt = 0x43524F5Au;

        /// <summary>
        /// Writes the crowd into <paramref name="into"/>. Returns how many were written
        /// (0 if <paramref name="into"/> is shorter than <see cref="TotalCount"/>).
        ///
        /// **It may be called every frame** (zero bytes allocated, branches only).
        /// </summary>
        public static int Build(CrowdPuff[] into)
        {
            if (into == null || into.Length < TotalCount) return 0;

            // 1) The area of each disc. **This is the denominator of the proportional share.**
            float totalArea = 0f;
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float disc = VortexPuffLayout.PuffAt(i).DiscFraction;
                if (disc > 0f) totalArea += disc * disc;
            }
            if (!(totalArea > 0f)) return 0;

            // 2) Hand them out in proportion to area. The rounding remainder and the floor
            //    are made up in 3).
            int written = 0;

            for (int i = 0; i < VortexPuffLayout.PuffCount && written < TotalCount; i++)
            {
                VortexPuff disc = VortexPuffLayout.PuffAt(i);
                if (!(disc.DiscFraction > 0f)) continue;

                float share = disc.DiscFraction * disc.DiscFraction / totalArea;
                int count = (int)(TotalCount * share + 0.5f);
                if (count < MinPerDisc) count = MinPerDisc;

                for (int k = 0; k < count && written < TotalCount; k++)
                {
                    into[written] = Scatter(disc, i, k);
                    written++;
                }
            }

            // 3) If there is still slack, fill it by adding **starting from the large discs**.
            //    Throw the remainder away and the number placed appears to change from one
            //    run to the next.
            for (int pass = 0; written < TotalCount; pass++)
            {
                bool grew = false;

                for (int i = 0; i < VortexPuffLayout.PuffCount && written < TotalCount; i++)
                {
                    VortexPuff disc = VortexPuffLayout.PuffAt(i);
                    if (!(disc.DiscFraction > 0f)) continue;

                    into[written] = Scatter(disc, i, 1000 + pass * 64 + i);
                    written++;
                    grew = true;
                }

                if (!grew) break;   // not a single disc (cannot happen, but do not loop for ever)
            }

            return written;
        }

        /// <summary>
        /// The <paramref name="k"/>th puff inside disc <paramref name="discIndex"/>.
        /// **Decided by the indices alone** (see the class doc).
        /// </summary>
        private static CrowdPuff Scatter(VortexPuff disc, int discIndex, int k)
        {
            uint seed = unchecked((uint)(discIndex * 7919 + k * 104729));

            // ★★ **Scatter in the disc's local coordinates** — radially it becomes the
            //    thickness of the arm, and along the arm it becomes the join to the next
            //    disc (<c>AlongArmScatterRatio</c>).
            //    We stopped scattering uniformly inside a circle because that made the arms
            //    into dotted lines.
            float radial = (2f * DeterministicRandom.Unit(seed, RadiusSalt) - 1f)
                           * disc.DiscFraction * RadialScatterRatio;
            float along = (2f * DeterministicRandom.Unit(seed, AngleSalt) - 1f)
                          * AlongArmScatterRatio;

            float radius = disc.RadiusFraction + radial;
            if (radius < 0.001f) radius = 0.001f;

            // Arc length → angle. **Dividing by the radius** means the scatter is larger in
            // angle further in (which is the same way round as the real shape, where the
            // inside of an arm is packed tighter).
            float angle = disc.AngleRadians + along / radius;

            // Height within the band. The disc is the bottom of the tier, so scatter upwards
            // only (the same promise as the old implementation).
            float height = disc.HeightFraction
                           + disc.BandFraction * DeterministicRandom.Unit(seed, HeightSalt);

            // ★ The size is decided by the **tier** (see the class doc). It is not made
            //   proportional to the disc's size.
            float size = VortexPuffLayout.SizeFractionOf(disc.Layer) * PuffSizeGain
                         * (1f + SizeJitter * (2f * DeterministicRandom.Unit(seed, SizeSalt) - 1f));
            if (size < 0.001f) size = 0.001f;

            return new CrowdPuff(angle, radius, height, size, disc.DensityFraction, disc.Layer);
        }
    }
}
