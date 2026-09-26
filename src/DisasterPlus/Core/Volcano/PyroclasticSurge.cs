using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// The geometry of the <b>fan of dust</b> that stands in for a "pyroclastic flow".
    /// **Pure, engine-free functions only.**
    ///
    /// ── ★★ This is not a pyroclastic flow. Do not misdescribe it ─────────────
    ///
    /// Vanilla has no pyroclastic flow. We checked every single <c>EffectInfo</c> in the
    /// shipped assets (277 of them) and nothing corresponding to "a dense cloud hugging the
    /// ground and racing downhill" exists in either the base game or the DLC. What ⑤
    /// produces is <b>the dust raised when a building collapses</b>
    /// (<c>Collapse Particles</c>), spawned into a Bézier band down the slope and pushed
    /// downhill by the <c>velocity</c> argument. It looks like "grey dust running down a
    /// slope"; it is not a reproduction of a pyroclastic flow. **This type touches neither
    /// buildings, nor trees, nor the ground** (setting things alight is the lava's job). The
    /// panel and the design document <b>say so</b>.
    ///
    /// ── ★★ It is a fan, not a ribbon on top of the lava (2026-08-22, on-hardware
    ///    observation ⑤) ───────
    ///
    /// > As for the pyroclastic flow, at the moment it only runs down on top of the lava
    /// > flows, but in reality it ought to spread much further across the foot of the
    /// > mountain.
    ///
    /// The path used to be <b>the lava's own track</b> from the snapshot. Running down the
    /// same valleys as the lava is correct, but **a pyroclastic flow does not run at the
    /// lava's width** — a pyroclastic flow (properly a pyroclastic surge, a density current)
    /// is a heavy cloud, not a thread of fluid, so as it descends it <b>spreads sideways and
    /// makes a fan across the whole foot</b>. It does not follow the terrain entirely, and
    /// **near the source it rides straight over ridges**.
    ///
    /// Now this type builds the fan itself:
    ///
    /// <code>
    /// Lay out <see cref="LobeCount"/> lobes around the crater, evenly spaced plus jitter
    /// Each lobe runs radially from the crater out to <see cref="ReachMetres"/>
    /// A lobe widens as it descends (<see cref="HalfWidthMetres"/>; up to 300 m at the foot = 600 m across)
    /// It is pulled towards the valleys (the directions the lava ran) **more and more towards the foot** (<see cref="ChannelPullAt"/>)
    ///   → near the source it rides over ridges, and at the foot it gathers into the valleys. That is "partly following the terrain"
    /// </code>
    ///
    /// The lava's track is used solely as <b>the one clue to where the valleys are</b> (we
    /// extract one bearing per flow). It is not used as the path itself.
    ///
    /// ── The density is normalised by area (**get this wrong and the particles flood**) ────
    ///
    /// A Bézier band's particle count is <c>2 × halfWidth × path length × pps</c> (measured
    /// from the IL, §B-5). Widen the half-width from 90 m to
    /// <see cref="HalfWidthMaxMetres"/> and raise the count from 2 to
    /// <see cref="LobeCount"/>, and naively you would be firing **more than ten times** as
    /// many particles. <see cref="Magnitude"/> divides by the band's area, so
    /// <b>the whole fan stays within the same budget as the old two bands</b>
    /// (the same thinking as <see cref="EruptionColumn"/>'s plume column).
    ///
    /// ── How the surge is made ───────────────────────────────
    ///
    /// The band's head runs at a constant speed from the crater to the end of the lobe, and
    /// once it has passed all the way through it returns to the crater and starts again.
    /// One cycle lasts <c>(path length + band length) / speed</c> seconds. Whatever part of
    /// the band overhangs the path is thinned out by <see cref="Magnitude"/>, so it does not
    /// vanish abruptly at the end.
    /// **Each lobe's phase is offset** (<see cref="LobePhaseSeconds"/>), so the lobes never
    /// run in formation together.
    ///
    /// The caller keeps the clock. **Do not mix in the frame number** (this type holds no
    /// clock).
    /// </summary>
    public static class PyroclasticSurge
    {
        /// <summary>The number of lobes making up the fan. **This is the cost cap itself**
        /// (one lobe = one <c>RenderEffect</c> call).</summary>
        public const int LobeCount = 6;

        /// <summary>The band's length (m), from head to tail.</summary>
        public const float BandLengthMetres = 260f;

        /// <summary>The speed the band's head travels at (m/s). **A presentation value ⑤
        /// chose.**</summary>
        public const float HeadSpeedMetresPerSecond = 95f;

        /// <summary>
        /// The speed the particles themselves are pushed downhill at (m/s).
        /// <b>A different thing from <see cref="HeadSpeedMetresPerSecond"/>.</b>
        /// That is the speed at which the band (the spawn region) travels down the path;
        /// this is the speed at which one spawned particle flies. Make them the same and the
        /// particles get left behind outside the band and trail off.
        /// </summary>
        public const float PushMetresPerSecond = 30f;

        /// <summary>The floor on a lobe's reach (as a ratio of the mountain's radius), i.e.
        /// when the eruption is weak.</summary>
        public const float ReachBaseFraction = 0.55f;

        /// <summary>The reach added by the strength (same units). At 1 it reaches the
        /// foot.</summary>
        public const float ReachGainFraction = 0.45f;

        /// <summary>The floor on the band's half-width (m). Right beside the crater.</summary>
        public const float HalfWidthBaseMetres = 45f;

        /// <summary>The half-width added per kilometre descended (m). **Raised from 40 to 230
        /// after observation ⑤.**</summary>
        public const float HalfWidthPerKilometre = 230f;

        /// <summary>The cap on the band's half-width (m). **Raised from 90 to 300 after
        /// observation ⑤.**</summary>
        public const float HalfWidthMaxMetres = 300f;

        /// <summary>The floor on the band's density (at strength 0).</summary>
        public const float MagnitudeMin = 10f;

        /// <summary>The ceiling on the band's density (at strength 1).</summary>
        public const float MagnitudeMax = 46f;

        /// <summary>
        /// The reference area (m²) the density is normalised against. **The old two bands'
        /// worth** (<c>2 × a half-width of 90 m</c> (= the full width) × a band length of
        /// 260 m × 2 bands).
        /// The leading 2 is not "front and back" but the same **half-width to full-width
        /// conversion** as the <c>2f * half</c> in <see cref="Magnitude"/>.
        /// We thin the density by however much the fan's band area exceeds this — otherwise
        /// the moment we raise the count and the width, the particles go up more than
        /// tenfold.
        /// </summary>
        public const float ReferenceAreaSquareMetres = 2f * 90f * 260f * 2f;

        /// <summary>
        /// The jitter on a lobe's bearing (as a ratio of the even spacing). At 0 it looks
        /// like **the blades of a windmill**.
        /// Keep it below ±0.5 so a lobe cannot swap places with its neighbour.
        /// </summary>
        public const float AzimuthJitter = 0.34f;

        /// <summary>
        /// The maximum fraction by which it is pulled towards the valleys (the directions the
        /// lava ran). **This is the value at the foot.**
        /// At 1 it "only flows on top of the lava" — which is exactly the shape observation ⑤
        /// made us fix, so **never raise it above a half.**
        /// </summary>
        public const float ChannelPullMax = 0.5f;

        /// <summary>
        /// The exponent by which the pull towards the valleys grows with distance. The larger
        /// it is, the more pronounced "rides over ridges near the source, gathers into
        /// valleys at the foot" becomes.
        /// </summary>
        public const float ChannelPullPower = 1.6f;

        /// <summary>
        /// How far a lobe bows sideways (as a ratio of the half-width). **A dead straight ray
        /// looks artificial.**
        /// </summary>
        public const float BowFactor = 0.55f;

        /// <summary>
        /// Lobes shorter than this get no band (m).
        /// Wrap a band around a stub right beside the crater and you get a ball of ash
        /// sitting on the summit.
        /// </summary>
        public const float MinPathMetres = 80f;

        /// <summary>
        /// The reach (m) of lobe number <paramref name="index"/>.
        /// **Slightly different per lobe** (with all of them the same, the fan's edge is a
        /// perfect circle).
        /// </summary>
        public static float ReachMetres(float radiusMetres, float intensityUnit,
                                        uint seed, int index)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return 0f;

            float unit = Clamp01(intensityUnit);
            float reach = radiusMetres * (ReachBaseFraction + ReachGainFraction * unit);

            // ±15% of variation, determined by the seed and index alone (we never mix in the
            // frame number).
            float jitter = 0.85f + 0.3f * DeterministicRandom.Unit(seed, (uint)(0x5A00 + index));
            float value = reach * jitter;
            return IsBad(value) || value < 0f ? 0f : value;
        }

        /// <summary>
        /// **The bearing at which lobe number <paramref name="index"/> leaves the crater**
        /// (radians).
        /// Evenly spaced plus <see cref="DeterministicRandom"/> jitter, looking at no terrain
        /// at all — because <b>riding over ridges near the source</b> is this type's whole
        /// claim.
        /// </summary>
        public static float LobeAzimuth(uint seed, int index, int count)
        {
            int n = count <= 0 ? 1 : count;
            int i = ((index % n) + n) % n;

            double even = 2.0 * Math.PI * i / n;
            float jitter = DeterministicRandom.Unit(seed, (uint)(0x6B00 + i)) - 0.5f;
            return (float)(even + jitter * AzimuthJitter * 2.0 * Math.PI / n);
        }

        /// <summary>
        /// How strongly it is pulled towards the valleys at a fraction <paramref name="t"/>
        /// of the distance from the crater, in <c>[0,1]</c>.
        /// **0 at the source (it rides over ridges), <see cref="ChannelPullMax"/> at the
        /// foot.**
        /// </summary>
        public static float ChannelPullAt(float t)
        {
            if (IsBad(t) || t <= 0f) return 0f;
            float u = t > 1f ? 1f : t;
            return ChannelPullMax * (float)Math.Pow(u, ChannelPullPower);
        }

        /// <summary>
        /// The bearing among <paramref name="bearings"/> closest to
        /// <paramref name="azimuth"/> (radians). If there are none,
        /// <paramref name="found"/> comes back false and the result is
        /// <paramref name="azimuth"/> itself —
        /// **the fan still appears on a mountain with no lava flows at all** (it just is not
        /// pulled towards any valley).
        /// </summary>
        public static float NearestChannel(float azimuth, float[] bearings, int count,
                                           out bool found)
        {
            found = false;
            if (bearings == null || count <= 0) return azimuth;

            int limit = count > bearings.Length ? bearings.Length : count;
            float best = azimuth;
            float bestDelta = float.MaxValue;

            for (int i = 0; i < limit; i++)
            {
                float b = bearings[i];
                if (IsBad(b)) continue;

                float delta = SignedDelta(azimuth, b);
                float abs = delta < 0f ? -delta : delta;
                if (abs < bestDelta)
                {
                    bestDelta = abs;
                    best = azimuth + delta;
                    found = true;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns one lobe's band as 4 points (Bézier control points).
        /// <paramref name="a"/> is the tail and <paramref name="d"/> the head.
        ///
        /// When the band has passed entirely off the lobe, or the lobe is too short, it
        /// returns <c>false</c> and every output is the crater (**we do not fabricate
        /// plausible-looking coordinates**).
        /// </summary>
        public static bool TryLobe(Vec2 vent, float baseAzimuth, float channelAzimuth,
                                   float reachMetres, float headMetres,
                                   out Vec2 a, out Vec2 b, out Vec2 c, out Vec2 d)
        {
            a = b = c = d = vent;

            if (IsBad(vent.X) || IsBad(vent.Z)) return false;
            if (IsBad(reachMetres) || reachMetres < MinPathMetres) return false;
            if (OverlapMetres(headMetres, reachMetres) <= 0f) return false;

            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            if (head > reachMetres) head = reachMetres;

            float tail = head - BandLengthMetres;
            if (tail < 0f) tail = 0f;

            float span = head - tail;
            float delta = SignedDelta(baseAzimuth, channelAzimuth);

            a = PointAt(vent, baseAzimuth, delta, reachMetres, tail);
            b = PointAt(vent, baseAzimuth, delta, reachMetres, tail + span / 3f);
            c = PointAt(vent, baseAzimuth, delta, reachMetres, tail + span * 2f / 3f);
            d = PointAt(vent, baseAzimuth, delta, reachMetres, head);
            return true;
        }

        /// <summary>
        /// The point on the lobe <paramref name="distanceMetres"/> from the crater.
        /// **The further towards the foot, the more it swings round towards the valley's
        /// direction** (<see cref="ChannelPullAt"/>).
        /// </summary>
        public static Vec2 PointAt(Vec2 vent, float baseAzimuth, float channelDeltaRadians,
                                   float reachMetres, float distanceMetres)
        {
            if (IsBad(distanceMetres) || distanceMetres < 0f) return vent;
            if (IsBad(reachMetres) || reachMetres <= 0f) return vent;

            float t = distanceMetres / reachMetres;
            if (t > 1f) t = 1f;

            float delta = IsBad(channelDeltaRadians) ? 0f : channelDeltaRadians;
            double angle = baseAzimuth + delta * ChannelPullAt(t);

            // The sideways bow. **A dead straight ray looks artificial.**
            float bow = BowFactor * HalfWidthMetres(distanceMetres)
                        * (float)Math.Sin(Math.PI * t);

            double dirX = Math.Cos(angle);
            double dirZ = Math.Sin(angle);

            float x = vent.X + (float)(dirX * distanceMetres - dirZ * bow);
            float z = vent.Z + (float)(dirZ * distanceMetres + dirX * bow);
            if (IsBad(x) || IsBad(z)) return vent;
            return new Vec2(x, z);
        }

        /// <summary>The length of one cycle in seconds. It never returns 0 even for a short
        /// path (we do not let a division by zero out).</summary>
        public static float CycleSeconds(float pathLengthMetres)
        {
            float path = IsBad(pathLengthMetres) || pathLengthMetres < 0f ? 0f : pathLengthMetres;
            float seconds = (path + BandLengthMetres) / HeadSpeedMetresPerSecond;
            return seconds < 0.1f ? 0.1f : seconds;
        }

        /// <summary>
        /// The per-lobe phase offset (seconds). It exists **so that the lobes do not run in
        /// formation**.
        /// </summary>
        public static float LobePhaseSeconds(int index, int count, float pathLengthMetres)
        {
            int n = count <= 0 ? 1 : count;
            int i = ((index % n) + n) % n;
            return CycleSeconds(pathLengthMetres) * i / n;
        }

        /// <summary>
        /// How far the band's head has got (m from the crater).
        /// <paramref name="clockSeconds"/> is the clock the caller keeps.
        /// </summary>
        public static float HeadMetres(float clockSeconds, float pathLengthMetres)
        {
            if (IsBad(clockSeconds) || clockSeconds < 0f) return 0f;

            float cycle = CycleSeconds(pathLengthMetres);
            float t = clockSeconds - (float)Math.Floor(clockSeconds / cycle) * cycle;
            if (IsBad(t) || t < 0f) t = 0f;

            float head = t * HeadSpeedMetresPerSecond;
            return IsBad(head) || head < 0f ? 0f : head;
        }

        /// <summary>
        /// The band's density. **Thinned by the fraction of it that lies on the path**, so it
        /// does not appear or vanish abruptly as it starts out and passes off the end.
        /// If the path is shorter than <see cref="MinPathMetres"/> it is 0 (i.e. nothing is
        /// emitted).
        ///
        /// ★★ **Normalise by the band's area.** A Bézier band's particle count is
        /// <c>2 × halfWidth × path length × pps</c> (measured from the IL, §B-5), so unless we
        /// divide the increased width and count back out here, the particles go up more than
        /// tenfold.
        /// </summary>
        public static float Magnitude(float intensityUnit, float headMetres,
                                      float pathLengthMetres, float halfWidthMetres)
        {
            if (IsBad(pathLengthMetres) || pathLengthMetres < MinPathMetres) return 0f;

            float overlap = OverlapMetres(headMetres, pathLengthMetres);
            if (overlap <= 0f) return 0f;

            float share = overlap / BandLengthMetres;
            if (share > 1f) share = 1f;

            float half = IsBad(halfWidthMetres) || halfWidthMetres <= 0f
                ? HalfWidthBaseMetres : halfWidthMetres;
            float area = 2f * half * BandLengthMetres * LobeCount;
            if (!(area > 0f)) return 0f;

            float scale = ReferenceAreaSquareMetres / area;
            if (scale > 1f) scale = 1f;   // an area below the reference never makes it denser

            float u = Clamp01(intensityUnit);
            float m = (MagnitudeMin + (MagnitudeMax - MagnitudeMin) * u) * share * scale;
            return IsBad(m) || m < 0f ? 0f : m;
        }

        /// <summary>The band's half-width (m). **It widens as it descends**, but always levels
        /// off.</summary>
        public static float HalfWidthMetres(float headMetres)
        {
            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            float w = HalfWidthBaseMetres + head / 1000f * HalfWidthPerKilometre;
            if (IsBad(w)) return HalfWidthBaseMetres;
            return w > HalfWidthMaxMetres ? HalfWidthMaxMetres : w;
        }

        /// <summary>
        /// The **overall bearing** (radians) of a polyline (i.e. a lava track). Used as the
        /// clue to where the valleys are.
        /// <c>false</c> when there are not enough points or the coordinates are broken.
        /// </summary>
        public static bool TryBearing(Vec2[] points, int start, int count, Vec2 vent,
                                      out float bearing)
        {
            bearing = 0f;
            if (points == null || count <= 0 || start < 0 || start >= points.Length) return false;

            int limit = count;
            if (start + limit > points.Length) limit = points.Length - start;
            if (limit < 2) return false;

            // Take the bearing from the furthest point reached (so the meandering along the
            // way does not drag it about).
            float bestX = 0f, bestZ = 0f, best = 0f;
            for (int i = 0; i < limit; i++)
            {
                Vec2 p = points[start + i];
                if (IsBad(p.X) || IsBad(p.Z)) break;

                float dx = p.X - vent.X;
                float dz = p.Z - vent.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > best) { best = d2; bestX = dx; bestZ = dz; }
            }

            if (!(best > 1f)) return false;
            bearing = (float)Math.Atan2(bestZ, bestX);
            return true;
        }

        /// <summary>The length of the band (m) that lies on the path.</summary>
        private static float OverlapMetres(float headMetres, float pathLengthMetres)
        {
            float head = IsBad(headMetres) || headMetres < 0f ? 0f : headMetres;
            float path = IsBad(pathLengthMetres) || pathLengthMetres < 0f ? 0f : pathLengthMetres;

            float hi = head < path ? head : path;
            float lo = head - BandLengthMetres;
            if (lo < 0f) lo = 0f;

            float overlap = hi - lo;
            return IsBad(overlap) || overlap < 0f ? 0f : overlap;
        }

        /// <summary>The difference between two bearings, in <c>[-π, π]</c> (**the nearer way
        /// round, even across the wrap**).</summary>
        private static float SignedDelta(float from, float to)
        {
            if (IsBad(from) || IsBad(to)) return 0f;

            double d = to - from;
            double twoPi = 2.0 * Math.PI;
            d -= twoPi * Math.Floor((d + Math.PI) / twoPi);
            return (float)d;
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
