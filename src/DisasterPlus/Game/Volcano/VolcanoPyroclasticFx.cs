using System;
using ColossalFramework.Math;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The bands of grey dust racing down the flanks. **Main thread only, every frame.**
    ///
    /// ── ★★ this is not a pyroclastic flow. Do not describe it wrongly ─────────────────────
    ///
    /// <b>Vanilla has no pyroclastic flow.</b> Going through every <c>EffectInfo</c> in the
    /// shipped assets (277 of them; 234 base + 43 DLC), not one in the base game or the DLC
    /// corresponds to "a dense cloud hugging the ground and racing downhill".
    ///
    /// What ⑤ emits is <b><c>Collapse Particles</c> (the dust of a building collapsing)</b> welled
    /// up in a bezier band following the lava's path, pushed downhill via <c>RenderEffect</c>'s
    /// <c>velocity</c> argument. What you see is
    /// **"a 50–180 m wide band of grey dust going down the valleys"** —
    /// not a reproduction of a pyroclastic flow but a stand-in that looks the part.
    /// The panel's note (<c>Strings.VolcanoPyroclasticNote</c>) and design doc §4.4 **say so too**.
    ///
    /// ── ★ it destroys nothing ─────────────────────────────────────────────────────────────
    ///
    /// This type <b>changes not one byte of game state</b>. It touches no building, tree or
    /// ground (doing so would mean a path calling <c>BuildingAI.BurnBuilding</c> /
    /// <c>CollapseBuilding</c> directly, but **burning is the lava's job**, and having two paths
    /// for the same thing would make it impossible for anyone to tell which one did the burning).
    /// <c>Building.m_fireIntensity</c> is **never written directly**.
    ///
    /// ── ★★ it is a fan, not a ribbon on top of the lava (2026-08-22, live report ⑤) ───────
    ///
    /// > The pyroclastic flow currently only runs down on top of the lava flow, but really it
    /// > ought to spread out much more over the lower slopes.
    ///
    /// The path used to be **the lava's trail itself**. Going down the same valley as the lava is
    /// right, but **a pyroclastic flow does not run at the lava's width** — it is a heavy cloud,
    /// not a thread of fluid, so it widens sideways as it descends and fans out across the whole
    /// lower slope. It does not follow the terrain completely, and near the source it goes over
    /// ridges.
    ///
    /// Now <see cref="PyroclasticSurge"/> (Core, with tests) builds the fan itself — it
    /// distributes <c>LobeCount</c> lobes around the crater, widens them towards the foot (up to
    /// 260 m), and pulls them towards the valley (i.e. the direction the lava went) **only as
    /// they approach the foot**.
    /// The lava's trail is used here purely as a clue to "where the valleys are": the bearings are
    /// extracted one by one and handed to Core (<see cref="_bearings"/>).
    ///
    /// ── the trap in the bezier band (measured in IL) ───────────────────────────────────────
    ///
    /// <code>
    /// SpawnArea(Bezier3 bezier, float halfWidth, float halfHeight)
    ///   → EmitParticles **uses halfHeight (the 4th argument) as the band's half-width** and
    ///     never reads halfWidth (the 3rd argument). The parameter names and the implementation
    ///     disagree.
    ///   → The initial velocity **only goes into the upward component**. Pushing sideways is what
    ///     the velocity argument is for.
    /// </code>
    ///
    /// So put **the same value in both** for the half-width, and do the pushing with
    /// <c>velocity</c>.
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// There are <c>PyroclasticSurge.LobeCount</c> bands (2 → 6). Each costs four
    /// <c>SampleDetailHeight</c> calls (reads; see the doc of <c>TerrainHeightSampler</c>) and one
    /// <c>RenderEffect</c>. <c>Bezier3</c> / <c>SpawnArea</c> / <c>Vector3</c> are all structs,
    /// and the bearings array is **allocated once at start and reused**, so
    /// **heap allocation is zero bytes**.
    ///
    /// ★ The total number of particles does not go up. <c>PyroclasticSurge.Magnitude</c>
    ///   normalises by the band's area, so **the whole fan comes to the same amount as the old two
    ///   bands**.
    ///
    /// ── this type is never called from the sim thread ──────────────────────────────────────
    /// </summary>
    public static class VolcanoPyroclasticFx
    {
        /// <summary>How far above the ground the band floats (m).</summary>
        private const float LiftMetres = 5f;

        /// <summary>
        /// The directions the lava went (radians). **A clue to where the valleys are**, not the
        /// path itself (class doc).
        ///
        /// ★ The array is allocated once and reused. Build it every frame and at 60 fps that is
        ///   60 pieces of garbage a second (this type runs every frame).
        ///   It is a <c>float[]</c>, so Unity's fake-null is irrelevant
        ///   (**that trap is about arrays of <c>UnityEngine.Object</c>**).
        /// </summary>
        private static readonly float[] _bearings = new float[VolcanoLava.MaxFlows];

        /// <summary>
        /// The vanilla effect clock (seconds). <c>EffectManager</c> itself adds
        /// <c>m_simulationTimeDelta</c> once per rendered frame, so ⑤ adds it the same way.
        /// **It stops when paused and follows the game speed.**
        /// </summary>
        private static float _clockSeconds;

        private static bool _renderErrorLogged;
        private static int _bandsDrawn;

        /// <summary>
        /// The strength <c>[0,1]</c> when it was last erupting. **It is the ceiling on the density
        /// after the eruption has finished.**
        ///
        /// ★★ <c>LavaCoolUnit</c> stays at **1** for as long as even one flow is still moving, so
        ///   without multiplying by this, <b>even a weak eruption (strength 0.2) would emit dust
        ///   at full density for the whole time the lava flows</b>. And the lava's advance lasts
        ///   longer than the eruption.
        /// </summary>
        private static float _lastIntensity;

        /// <summary>The number of bands emitted this frame (for diagnostics).</summary>
        public static int BandsDrawn { get { return _bandsDrawn; } }

        /// <summary>
        /// Whether the dust effect was looked up most recently. **A plain read for diagnostics
        /// only.**
        ///
        /// ★★ Do not run a resolution from here. The diagnostics are assembled from the sim thread
        ///   (<c>FeatureHost.BuildReport</c>), so Unity objects must not be touched — not even
        ///   with a reference comparison. All it does is read the result <see cref="Step"/> looked
        ///   up on the main thread.
        /// </summary>
        public static bool DustResolved { get { return VolcanoVanillaFx.DustResolvedCached; } }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                _bandsDrawn = 0;
                if (!_renderErrorLogged)
                {
                    _renderErrorLogged = true;
                    Log.Error("volcano pyroclastic effects failed", e);
                }
            }
        }

        /// <summary>
        /// **Main thread.** Call when turned off in the settings and on level unload.
        /// The borrowed effects are held by <see cref="VolcanoVanillaFx"/>, so all that is folded
        /// away here is ⑤'s own clock. Idempotent.
        /// </summary>
        public static void Destroy()
        {
            _clockSeconds = 0f;
            _bandsDrawn = 0;
            _lastIntensity = 0f;
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _bandsDrawn = 0;

            if (snapshot == null || !snapshot.Valid || !snapshot.Footprint.Valid)
            {
                _clockSeconds = 0f;
                return;
            }

            // ★★ **Gate on the phase.** The path was taken off the lava's trail (it became a
            //    fan), so the old gate of "do not draw if the trail is empty" no longer works.
            //    <c>VolcanoLava.CoolUnit</c> **returns 1** when there is no volcano at all
            //    (AllStopped is false = "it has not stopped yet"), so without checking the phase
            //    **dust would pour out at full strength where there is no mountain**.
            if (!snapshot.EruptionActive
                && snapshot.Phase != VolcanoPhase.Flowing
                && snapshot.Phase != VolcanoPhase.Cooling)
            {
                _clockSeconds = 0f;
                _lastIntensity = 0f;
                return;
            }

            // While the eruption continues, use its strength; after it ends, fade with the lava's
            // cooling, **capped at that eruption's strength**.
            // ★ Decide it from the cooling alone and, because CoolUnit is 1 for as long as even
            //   one flow is moving, a weak eruption would keep emitting dust at full density
            //   (the doc of _lastIntensity).
            // **When both reach 0, emit not one particle** (so ash does not linger in a stopped
            // valley).
            float unit;
            if (snapshot.EruptionActive)
            {
                unit = Clamp01(snapshot.EruptionIntensityUnit);
                if (unit > _lastIntensity) _lastIntensity = unit;
            }
            else
            {
                float cool = Clamp01(snapshot.LavaCoolUnit);
                unit = cool < _lastIntensity ? cool : _lastIntensity;
            }

            if (unit <= 0f) return;

            RenderManager.CameraInfo camera = VolcanoVanillaFx.CameraInfo();
            if (camera == null) return;

            ParticleEffect dust = VolcanoVanillaFx.PyroclasticDust();
            if (dust == null) return;

            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt <= 0f) return;
            _clockSeconds += dt;

            // ★ The fan comes out of the crater. Use the vent's (the crater floor's) horizontal
            //   position as is.
            var vent = new Vec2(snapshot.VentWorld.X, snapshot.VentWorld.Z);

            int channels = ReadBearings(snapshot, vent);

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(snapshot.Footprint.Centre.X)),
                unchecked((uint)Mathf.RoundToInt(snapshot.Footprint.Centre.Z)));

            // ★ The fan can only descend as far as **the mountain that is actually there right
            //   now**. Hand it the finished radius partway through the uplift and the dust runs
            //   across ground that is still flat.
            //   A lobe that ends up too short disappears naturally at
            //   PyroclasticSurge.MinPathMetres.
            float reachRadius = snapshot.UpliftComplete
                ? snapshot.Footprint.RadiusMetres
                : snapshot.Footprint.RadiusMetres * Clamp01(snapshot.ProgressUnit);

            for (int i = 0; i < PyroclasticSurge.LobeCount; i++)
            {
                if (RenderLobe(dust, camera, vent, seed, i, channels, reachRadius, unit, dt))
                {
                    _bandsDrawn++;
                }
            }
        }

        /// <summary>
        /// Extract "the direction of the valley" from the lava's trail into
        /// <see cref="_bearings"/>.
        /// **It is fine for no lava to be flowing at all** (it returns 0, and the fan simply is
        /// not pulled towards a valley).
        /// </summary>
        private static int ReadBearings(VolcanoSnapshot snapshot, Vec2 vent)
        {
            Vec2[] points = snapshot.LavaTrailPoints;
            int[] counts = snapshot.LavaTrailCounts;
            if (points == null || counts == null) return 0;

            int found = 0;
            int cursor = 0;
            for (int i = 0; i < counts.Length && found < _bearings.Length; i++)
            {
                int declared = counts[i] > 0 ? counts[i] : 0;
                int available = declared;
                if (cursor + available > points.Length) available = points.Length - cursor;

                float bearing;
                if (available >= 2
                    && PyroclasticSurge.TryBearing(points, cursor, available, vent, out bearing))
                {
                    _bearings[found++] = bearing;
                }

                cursor += declared;
                if (cursor >= points.Length) break;
            }

            return found;
        }

        /// <summary>
        /// The band for one lobe. If it cannot be emitted it merely returns <c>false</c>; no
        /// exception is thrown.
        /// </summary>
        private static bool RenderLobe(ParticleEffect effect, RenderManager.CameraInfo camera,
                                       Vec2 vent, uint seed, int index, int channels,
                                       float radiusMetres, float unit, float dt)
        {
            float reach = PyroclasticSurge.ReachMetres(radiusMetres, unit, seed, index);
            if (reach < PyroclasticSurge.MinPathMetres) return false;

            float azimuth = PyroclasticSurge.LobeAzimuth(seed, index, PyroclasticSurge.LobeCount);

            // ★ The valley clue. If there is not one, it simply is not pulled; the fan itself
            //   still appears.
            bool found;
            float channel = PyroclasticSurge.NearestChannel(azimuth, _bearings, channels,
                                                            out found);
            if (!found) channel = azimuth;

            // ★ Offset the phase per lobe (so the lobes do not run in formation).
            float clock = _clockSeconds
                          + PyroclasticSurge.LobePhaseSeconds(index, PyroclasticSurge.LobeCount,
                                                              reach);
            float head = PyroclasticSurge.HeadMetres(clock, reach);
            float halfWidth = PyroclasticSurge.HalfWidthMetres(head);

            float magnitude = PyroclasticSurge.Magnitude(unit, head, reach, halfWidth);
            if (magnitude <= 0f) return false;

            Vec2 a, b, c, d;
            if (!PyroclasticSurge.TryLobe(vent, azimuth, channel, reach, head,
                                          out a, out b, out c, out d))
            {
                return false;
            }

            Vector3 pa = OnGround(a);
            Vector3 pb = OnGround(b);
            Vector3 pc = OnGround(c);
            Vector3 pd = OnGround(d);

            // ★ The third argument is never read (measured in IL). Put the same value in both.
            var area = new EffectInfo.SpawnArea(new Bezier3(pa, pb, pc, pd),
                                                halfWidth, halfWidth);

            // ★ This velocity is what makes it hug the ground (on a bezier path the initial
            //   velocity only goes into the upward component). Push from tail towards head.
            Vector3 push = pd - pa;
            push.y = 0f;
            float length = push.magnitude;
            if (length > 0.001f)
            {
                push *= PyroclasticSurge.PushMetresPerSecond / length;
            }
            else
            {
                push = Vector3.zero;
            }

            effect.RenderEffect(default(InstanceID), area, push, 0f,
                                magnitude,
                                -1f,   // ★ continuous mode
                                dt, camera);
            return true;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || v < 0f) return 0f;
            return v > 1f ? 1f : v;
        }

        /// <summary>
        /// A point placed at the ground's height. If it cannot be read, use 0 (**better than the
        /// band disappearing**).
        /// <c>SampleDetailHeight</c> is a read, so it is safe from either thread
        /// (the doc of <see cref="TerrainHeightSampler"/>).
        /// </summary>
        private static Vector3 OnGround(Vec2 point)
        {
            float y;
            try
            {
                y = TerrainHeightSampler.Instance.SampleHeight(point.X, point.Z);
            }
            catch
            {
                y = 0f;
            }

            if (float.IsNaN(y) || float.IsInfinity(y)) y = 0f;
            return new Vector3(point.X, y + LiftMetres, point.Z);
        }
    }
}
