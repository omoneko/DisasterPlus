using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// The probability model for scattering embers around the whirl.
    /// A real fire whirl accelerates the spread of fire through its embers, so this is the
    /// heart of the phenomenon.
    ///
    /// It deliberately does not go through DisasterHelpers. Even if a conflicting mod has
    /// replaced DisasterHelpers.DestroyBuildings, this layer alone is guaranteed to still
    /// work (design document 3.3(b)).
    /// </summary>
    public static class IgnitionSpread
    {
        /// <summary>The maximum value of SpreadStrength. Keep it in step with the slider's
        /// upper limit in ModSettings.</summary>
        public const int MaxStrength = 10;

        /// <summary>The per-tick ignition chance at strength 10 and zero distance.</summary>
        private const float BaseProbabilityAtMaxStrength = 0.02f;

        public static void Select(
            Vec2 center,
            float radius,
            int spreadStrength,
            IList<IgnitionCandidate> nearby,
            uint tick,
            List<ushort> into)
        {
            into.Clear();
            if (spreadStrength <= 0 || nearby == null || nearby.Count == 0) return;
            if (radius <= 0f) return;

            int strength = spreadStrength > MaxStrength ? MaxStrength : spreadStrength;
            float strengthScale = strength / (float)MaxStrength;
            float r2 = radius * radius;

            for (int i = 0; i < nearby.Count; i++)
            {
                var c = nearby[i];
                if (c.AlreadyBurning) continue;

                float d2 = center.DistanceSquaredTo(c.Position);
                if (d2 > r2) continue;

                // Distance falloff. Linear, 1 at the centre and 0 at the rim.
                float falloff = 1f - (float)System.Math.Sqrt(d2 / r2);
                float p = BaseProbabilityAtMaxStrength * strengthScale * falloff;
                if (p <= 0f) continue;

                // We do not use System.Random. The draw comes from (tick, buildingId), so
                // the result is the same across a save/load and however many times the
                // tests are run.
                if (DeterministicRandom.Unit(tick, c.Id) < p) into.Add(c.Id);
            }
        }
    }
}
