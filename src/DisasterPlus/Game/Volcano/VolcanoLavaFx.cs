using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// What we managed to get hold of in order to draw the lava surface (either
    /// <c>Shader.Find</c>, or borrowing <b>the shader</b> from an already-loaded
    /// <c>Material</c>).
    ///
    /// ★ <b>Do not fold <c>Shader.Find("Standard")</c> into the "did it resolve" test.</b>
    /// <c>Standard</c> is built into Unity and is **never null**, so a check like
    /// <c>… || Shader.Find("Standard") != null</c> **can structurally never fail**
    /// (④'s review found exactly this).
    /// Here the two facts are kept apart:
    ///
    /// <code>
    /// ResolvedShaderName      the name of the shader actually used (null = nothing resolved)
    /// ParticleShaderResolved  whether a particle shader (additive / alpha-blended) resolved  ← the check uses this
    /// </code>
    /// </summary>
    public struct VolcanoLavaShaderFacts
    {
        /// <summary>The name of the shader actually used. **If null, nothing is drawn.**</summary>
        public readonly string ResolvedShaderName;

        /// <summary>
        /// Whether a particle shader (additive or alpha-blended) resolved.
        /// **Even when this is false we still draw, with <c>Standard</c> put into transparent
        /// mode**, but it will not look like it is glowing. This is the predicate
        /// <c>Assumptions</c> uses.
        /// </summary>
        public readonly bool ParticleShaderResolved;

        /// <summary>The one line reported in the diagnostics (**English**). null if nothing resolved.</summary>
        public readonly string Detail;

        public VolcanoLavaShaderFacts(string resolvedShaderName, bool particleShaderResolved,
                                      string detail)
        {
            ResolvedShaderName = resolvedShaderName;
            ParticleShaderResolved = particleShaderResolved;
            Detail = detail;
        }
    }

    /// <summary>
    /// The glowing lava surface. **Main thread only.**
    ///
    /// ── why it is built from scratch ───────────────────────────────────────────────────────
    ///
    /// There is not one prefab, material or shader for lava, magma or melt **anywhere in the
    /// DLL's string heap** (§B-5). There is no off-the-shelf thing to borrow, so ⑤ builds both
    /// the shape (<c>LavaRibbon</c>) and the surface (material and texture) itself.
    ///
    /// ── trap 1: do not borrow a Cities material (③ settled this) ───────────────────────────
    ///
    /// The Cities shaders demand per-instance data supplied by the engine
    /// (<c>VortexAI.RenderExtraStuff</c> fills <c>m_materialBlock</c> before calling
    /// <c>DrawMesh</c>). Put one on your own <c>DrawMesh</c> and
    /// **nothing is drawn, or it comes out pitch black** (firestorm §4.9).
    /// Build the material yourself. **<see cref="ShaderPool"/> fetches the shader for it** —
    /// what is borrowed is <b>the shader only</b>, never the <c>Material</c> instance
    /// (the difference between the two is in that class's doc).
    ///
    /// **Which shader it resolved with goes into the diagnostics** (<see cref="ShaderDetail"/>) —
    /// so that if a future game update makes it silently invisible, we can say so.
    ///
    /// > ★★ **This used to say "the name that actually resolves in this build is
    /// > <c>Particles/Additive</c> (§H-22)". That was wrong** (the 13th false "verified" in this
    /// > project). The evidence was that a byte scan of the shipped assets found the string in
    /// > <c>globalgamemanagers</c>, but **a name existing in an asset and <c>Shader.Find</c>
    /// > resolving it are two different things**.
    /// > In the live game, <c>Shader.Find</c> **returned null for every name, including the
    /// > built-in <c>"Standard"</c>**. Now, when a name cannot be looked up, we borrow the shader
    /// > from an already-loaded <c>Material</c> (<see cref="ShaderPool"/>).
    /// There is one entry in <c>Assumptions</c> too, but its predicate is
    /// <b>"did a particle shader resolve"</b>, not "did anything resolve"
    /// (the class doc of <see cref="VolcanoLavaShaderFacts"/>).
    ///
    /// ── trap 2: do not make the static cache an array (a bug ③ shipped) ────────────────────
    ///
    /// <c>UnityEngine.Object</c> overloads <c>==</c> so that a destroyed object compares equal to
    /// null, but **that does not apply to references held in array elements**.
    /// A <c>static Mesh[]</c> / <c>static Material[]</c> keeps holding a destroyed object while
    /// still being non-null, and **in the second city it goes invisible with no message and no
    /// log** (firestorm §4.8).
    /// Here the <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> are held as **one reference
    /// each**, and every frame we test those references themselves with <c>== null</c>.
    ///
    /// **None of the three is a <c>Component</c>**, so they do not go down with the
    /// <c>GameObject</c>. <see cref="Destroy"/> calls <c>Object.Destroy</c> on them itself.
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// **One** <c>Graphics.DrawMesh</c> (at most 8 flows × 128 points × 2 = 2048 vertices and at
    /// most 6096 triangle indices. It neither casts nor receives shadows).
    /// **Zero bytes of heap allocation** (<c>Matrix4x4</c> is a struct).
    /// The colour is rewritten only when the cooling has moved by <see cref="TintStep"/>.
    ///
    /// The mesh is rebuilt only <b>on frames where the snapshot's trail array was swapped</b>.
    /// <c>VolcanoLava</c> only rebuilds the array on ticks where it advanced, so if the reference
    /// is the same there is no reason to rebuild (one <c>ReferenceEquals</c> decides it).
    /// An advance happens at most once per 8 sim frames' worth of in-game time, so
    /// **rebuilds never exceed 6 per second.**
    ///
    /// ── ★★ nothing moves with time any more (2026-08-22, live report ④) ───────────────────
    ///
    /// > The way the lava flow glows, flickering, is not realistic.
    /// > Please fix it still glowing after the eruption has finished.
    ///
    /// This used to scroll the UVs at 0.35 per second. It was scrolling them across a band whose
    /// whole length holds only three light-and-dark stripes, so **a given point on the ground
    /// cycled bright → dark → bright in under a second**. That is the "flicker". Now:
    ///
    /// <list type="bullet">
    /// <item><b>The glow is a function of position</b> (<c>Core/Volcano/LavaGlow</c>) — the
    ///   cooled crust, the glowing cracks between the plates, and the hot bands at the crater
    ///   and the advancing front</item>
    /// <item><b>It cools with age</b> — as the lava advances, points are added to the trail and
    ///   the <c>v</c> of an already-laid place moves out of the advancing-front band and into
    ///   the crust. Time is never consulted</item>
    /// <item><b>Once it has cooled out, the whole surface is folded away</b>
    ///   (<c>LavaGlow.Visible</c>) — the trail arrays survive the end of the volcano, so without
    ///   stopping here the bands would stay on the ground</item>
    /// </list>
    ///
    /// So <b>there is no longer a single thing that moves while paused</b> (the defect ④'s review
    /// raised, "only the clouds turn above a stopped city", has become structurally impossible).
    ///
    /// ── this type is never called from the sim thread ──────────────────────────────────────
    ///
    /// **That is the substance of what makes T9 independent of the rest of ⑤.**
    /// All it reads is the immutable arrays in <c>VolcanoHub.Latest</c>; it never touches
    /// <c>VolcanoLava</c>'s internal arrays.
    ///
    /// **Review grep (actually run, with the counts made to match)**:
    /// <code>
    /// grep -rl "VolcanoLavaFx" src/DisasterPlus --include=*.cs
    /// # -> exactly 4 files:
    /// #      Game/Volcano/VolcanoLavaFx.cs        (this file)
    /// #      Game/Volcano/VolcanoFeature.cs       (only 3 places: OnMainThreadUpdate /
    /// #                                            OnLevelUnloading / WriteDiagnostics)
    /// #      Game/Volcano/VolcanoEffectRows.cs    (the point count while drawing, and the note
    /// #                                            for when there is no material)
    /// #      Game/Diagnostics/Assumptions.Volcano.cs (one shader assumption)
    ///
    /// grep -l "VolcanoLavaFx" src/DisasterPlus/Game/Volcano/VolcanoState.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoSurvey.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoClearing.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoUplift.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoLava.cs
    /// # -> not a single hit (proof that it is not called from the sim-thread side)
    /// </code>
    /// </summary>
    public static class VolcanoLavaFx
    {
        /// <summary>How far to lift it off the ground (m). Kept far larger than the terrain quantum of 1/64 m.</summary>
        private const float HeightOffsetMetres = 1.5f;

        // ★★ ScrollPerSecond (scrolling the UVs at 0.35 per second) used to live here.
        //    **That was what the "flicker" really was** (2026-08-22, live report ④).
        //    It was scrolling the UVs across a band whose whole length holds only three
        //    light-and-dark stripes, so a given point on the ground cycled
        //    bright → dark → bright in under a second.
        //    **Make the glow a function of position, not of time** (Core/Volcano/LavaGlow).
        //    Do not put it back.

        /// <summary>The layer used for drawing. 0 = Default, which is in every camera's culling mask.</summary>
        private const int LavaLayer = 0;

        /// <summary>The cooling step at which the colour is rewritten (so it is not written every frame).</summary>
        private const float TintStep = 0.02f;

        /// <summary>
        /// One side of the generated texture (across the band × along the band).
        /// **Core holds it** (so that tools/VolcanoPreview bakes the same picture.
        /// It was raised from 32 to 128 because at 32 the crack lines come out as stair steps).
        /// </summary>
        private const int TextureSize = LavaGlow.TextureSize;

        /// <summary>Frames to leave before looking for the shader again (the same throttling as ④'s <c>TyphoonCloud</c>).</summary>
        private const int ShaderRetryFrames = 300;

        // ★ Do not use an array (trap 2). Hold one reference each so the fake-null self-repair
        //   works.
        private static Mesh _mesh;
        private static Material _material;
        private static Texture2D _texture;

        /// <summary>The trail array the mesh was last built from (**only its reference identity is examined**).</summary>
        private static Vec2[] _builtPoints;

        /// <summary>
        /// Whether a mesh could not be built from <see cref="_builtPoints"/>
        /// (e.g. no flow has as many as two points). It exists **to stop rebuilding every frame**.
        /// </summary>
        private static bool _buildFailed;

        private static string _shaderName;
        private static string _shaderDetail;
        private static bool _hasMainTex;
        private static int _shaderMissCount;
        private static bool _shaderWarned;
        private static bool _errorLogged;

        private static float _tintedCool = -1f;
        private static int _drawCalls;
        private static int _pointsDrawn;

        /// <summary>Whether the lava surface was drawn this frame.</summary>
        public static bool Drawing { get { return _drawCalls > 0; } }

        /// <summary>Whether the material could be built.</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>
        /// The one line reported in the diagnostics (**English**). It is **T9's only diagnostic
        /// output about how it looks**.
        /// <c>Assumptions</c> gets the same answer from <see cref="ScanShaderFacts"/>, so do not
        /// grow a separate "is it a particle shader" entry point here
        /// (with two entry points for the same fact, one of them always goes stale).
        /// </summary>
        public static string ShaderDetail
        {
            get
            {
                return string.IsNullOrEmpty(_shaderName)
                    ? "NONE (no shader resolved; the lava is invisible but still flows and burns)"
                    : _shaderDetail;
            }
        }

        /// <summary>How many <c>DrawMesh</c> calls were issued in the last frame (0 or 1).</summary>
        public static int DrawCalls { get { return _drawCalls; } }

        /// <summary>The number of trail points currently being drawn (for diagnostics).</summary>
        public static int PointsDrawn { get { return _pointsDrawn; } }

        /// <summary>
        /// **Main thread only.** A pure probe that only checks which shader can be obtained.
        /// Both <c>Assumptions</c> and <see cref="BuildMaterial"/> use it.
        /// </summary>
        public static VolcanoLavaShaderFacts ScanShaderFacts()
        {
            try
            {
                // ★ The order and the means of resolution live in exactly one place, ShaderPool.
                //   Write a different order here and the name the check reports drifts from the
                //   shader actually used (④'s Assumptions carries the same note for the same
                //   reason).
                //   Standard does not count as "a particle shader resolved" (class doc).
                ShaderPick pick = ShaderPool.Resolve(ShaderPreference.Additive);
                return new VolcanoLavaShaderFacts(pick.Name, pick.Particle, pick.Describe());
            }
            catch
            {
                return new VolcanoLavaShaderFacts(null, false, "NONE (the scan threw)");
            }
        }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                _drawCalls = 0;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano lava render failed", e);
                }
                Destroy();
            }
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            _drawCalls = 0;

            if (snapshot == null || !snapshot.Valid
                || snapshot.LavaTrailPoints == null || snapshot.LavaTrailCounts == null
                || snapshot.LavaTrailPoints.Length < 2)
            {
                // Clean up **ourselves** on the frame the lava disappears. Never called from the
                // sim side.
                Destroy();
                return;
            }

            // ★★ **Once it has cooled out, fold the whole surface away** (2026-08-22, live
            //    report ④: "please fix it still glowing after the eruption has finished").
            //    The trail arrays survive the end of the volcano (they are not discarded until
            //    the next mountain), so without stopping here the bands would stay on the ground
            //    for ever. While it is cooling, CoolFade darkens it towards 0.
            if (!LavaGlow.Visible(snapshot.LavaCoolUnit))
            {
                Destroy();
                return;
            }

            // ★ Rebuild only when the reference changed (the cost table in the class doc).
            //   VolcanoLava only rebuilds the array on ticks where it advanced.
            //
            // ★ The second condition is the fake-null self-repair (recovering from a state where
            //   only the mesh was destroyed while the reference stayed the same). **It is guarded
            //   by _buildFailed** — without that guard we would try to rebuild "a trail that
            //   could not be built" every frame, and the allocation alone would run every frame.
            if (!ReferenceEquals(_builtPoints, snapshot.LavaTrailPoints)
                || (_mesh == null && !_buildFailed))
            {
                RebuildMesh(snapshot);
            }

            if (_material == null) _material = BuildMaterial();
            if (_mesh == null || _material == null) return;

            // ★★ The UVs do not move by a millimetre. **The glow is a function of position**
            //    (LavaGlow). What changes slowly does so because "the lava advances and the v of
            //    an already-laid place moves out of the advancing-front band", not as a result of
            //    consulting time.
            ApplyTint(snapshot.LavaCoolUnit);

            // ★ Neither cast nor receive shadows (④'s review made the same point).
            //   Put a translucent glowing surface into the shadow pass and the band casts a
            //   shadow over the city.
            //   camera is null (= all cameras) — narrow it to one and the lava disappears there
            //   alone.
            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, LavaLayer,
                              null,     // camera: all cameras
                              0,        // submeshIndex
                              null,     // MaterialPropertyBlock
                              false,    // castShadows
                              false);   // receiveShadows

            _drawCalls = 1;
        }

        /// <summary>
        /// Pack the ribbons of all the flows into a single mesh. **Call
        /// <c>LavaRibbon.Build</c> once per flow** — hand the points of different flows over as
        /// one polyline and you get a band flying back and forth between the flows
        /// (the class doc of <c>LavaRibbon</c>).
        ///
        /// The width is decided from the cumulative distance measured off the trail itself
        /// (<c>LavaPath.SpreadRadiusFor</c>). That avoids having to add each flow's travelled
        /// distance to the snapshot, and it still gives the right value for a decimated trail.
        ///
        /// The height is <c>SampleDetailHeight</c> (a read, so safe from either thread; see the
        /// doc of <c>TerrainHeightSampler</c>) plus <see cref="HeightOffsetMetres"/>.
        /// </summary>
        private static void RebuildMesh(VolcanoSnapshot snapshot)
        {
            DestroyMesh();

            Vec2[] points = snapshot.LavaTrailPoints;
            int[] counts = snapshot.LavaTrailCounts;

            // ★ The width factor is **a pure function, so it comes out the same here**.
            //   It looks at the same <c>LavaVolume.WidthFactor</c> as the sim side (the
            //   ignition), so there is no path by which the band drawn and the range set alight
            //   can drift apart.
            float widthFactor = LavaVolume.WidthFactor(snapshot.Footprint.RadiusMetres);

            int totalVertices = 0;
            int totalIndices = 0;
            int cursor = 0;

            for (int i = 0; i < counts.Length; i++)
            {
                int c = Clamp(counts[i], points.Length - cursor);
                totalVertices += LavaRibbon.VertexCountFor(Min(c, LavaRibbon.MaxPoints));
                totalIndices += LavaRibbon.TriangleIndexCountFor(Min(c, LavaRibbon.MaxPoints));
                cursor += counts[i] > 0 ? counts[i] : 0;
            }

            if (totalVertices < 3 || totalIndices < 3)
            {
                _builtPoints = points;
                _buildFailed = true;
                return;
            }

            var vertices = new Vector3[totalVertices];
            var uvs = new Vector2[totalVertices];
            var triangles = new int[totalIndices];

            int vOut = 0;
            int tOut = 0;
            cursor = 0;
            _pointsDrawn = 0;

            for (int i = 0; i < counts.Length; i++)
            {
                int c = counts[i] > 0 ? counts[i] : 0;
                if (cursor + c > points.Length) c = points.Length - cursor;

                if (c >= 2)
                {
                    AppendRibbon(points, cursor, c, widthFactor,
                                 vertices, uvs, triangles, ref vOut, ref tOut);
                    _pointsDrawn += c;
                }

                cursor += counts[i] > 0 ? counts[i] : 0;
            }

            if (vOut < 3 || tOut < 3)
            {
                _builtPoints = points;
                _buildFailed = true;
                return;
            }

            // ★ Fold the unused vertices onto the first one. If <c>LavaRibbon.Build</c> refuses
            //   even one flow, that flow's vertices are left at (0,0,0). No triangle points at
            //   them so nothing is drawn, but <c>RecalculateBounds</c>
            //   **stretches the bounds all the way to the map origin and kills frustum culling**.
            for (int i = vOut; i < vertices.Length; i++) vertices[i] = vertices[0];

            var mesh = new Mesh();
            mesh.name = "DisasterPlus_VolcanoLava";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _mesh = mesh;
            _builtPoints = points;
            _buildFailed = false;
        }

        /// <summary>Add one flow's ribbon to the packed destination.</summary>
        private static void AppendRibbon(Vec2[] points, int start, int count, float widthFactor,
                                         Vector3[] vertices, Vector2[] uvs, int[] triangles,
                                         ref int vOut, ref int tOut)
        {
            int n = Min(count, LavaRibbon.MaxPoints);

            var slice = new Vec2[n];
            var widths = new float[n];

            float travelled = 0f;
            for (int i = 0; i < n; i++)
            {
                slice[i] = points[start + i];
                if (i > 0)
                {
                    float dx = slice[i].X - slice[i - 1].X;
                    float dz = slice[i].Z - slice[i - 1].Z;
                    travelled += (float)Math.Sqrt(dx * dx + dz * dz);
                }
                // The width is twice the radius.
                widths[i] = LavaPath.SpreadRadiusFor(travelled, widthFactor) * 2f;
            }

            Vec3[] built;
            float[] uv;
            int[] tri;
            if (!LavaRibbon.Build(slice, n, widths, HeightOffsetMetres, out built, out uv, out tri))
            {
                return;
            }

            int baseVertex = vOut;

            for (int i = 0; i < built.Length; i++)
            {
                float ground = SampleGround(built[i].X, built[i].Z);
                vertices[vOut] = new Vector3(built[i].X, ground + built[i].Y, built[i].Z);
                uvs[vOut] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                vOut++;
            }

            for (int i = 0; i < tri.Length; i++)
            {
                triangles[tOut++] = baseVertex + tri[i];
            }
        }

        /// <summary>
        /// The ground height (m). It is a read only, so it is safe from the main thread
        /// (the class doc of <c>TerrainHeightSampler</c>). **Called only when rebuilding.**
        /// </summary>
        private static float SampleGround(float x, float z)
        {
            try
            {
                float h = TerrainHeightSampler.Instance.SampleHeight(x, z);
                return float.IsNaN(h) ? 0f : h;
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// **Do not borrow a Cities material** (trap 1). Build it ourselves on the <b>shader</b>
        /// <see cref="ShaderPool"/> picked, and add transparency only when we fell all the way
        /// back to <c>Standard</c> (<c>Standard</c> is opaque by default, which would put an
        /// opaque grey band on the ground).
        ///
        /// ★ **Use the reference grabbed here for the resolved shader as it is.**
        ///   Never look it up again by name — a borrowed shader was borrowed precisely because
        ///   <c>Shader.Find</c> cannot find it, so looking it up by name always returns null and
        ///   the lava would never be drawn again.
        /// </summary>
        private static Material BuildMaterial()
        {
            // ★ Do not go looking every frame (the same throttling as ④'s TyphoonCloud).
            if (_shaderMissCount > 0)
            {
                _shaderMissCount--;
                return null;
            }

            ShaderPick pick = ShaderPool.Resolve(ShaderPreference.Additive);
            _shaderName = pick.Name;
            _shaderDetail = pick.Describe();

            if (!pick.Usable)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    // ★ Log.Warn is not throttled. This is on the per-frame path, so sound it
                    //   once and keep quiet afterwards.
                    Log.Warn("volcano lava: no usable shader resolved; the lava surface is not "
                             + "drawn (Disaster + does not borrow a Cities material - that "
                             + "renders invisible or black in a hand-rolled DrawMesh). The lava "
                             + "still flows, scorches the ground and sets buildings on fire");
                }
                _shaderMissCount = ShaderRetryFrames;
                return null;
            }

            var m = new Material(pick.Shader);
            m.name = "DisasterPlus_VolcanoLava";

            if (_texture == null) _texture = BuildTexture();
            _hasMainTex = _texture != null && m.HasProperty("_MainTex");
            if (_hasMainTex) m.SetTexture("_MainTex", _texture);

            // ★ Do not apply it to some other borrowed shader (_Mode / _SrcBlend are Standard's
            //   contract).
            if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            m.renderQueue = 3000;   // Transparent
            _tintedCool = -1f;
            return m;
        }

        /// <summary>
        /// One texture for the band. <c>u</c> runs across the band and <c>v</c> along it
        /// (<b>0 is the crater, 1 is the advancing front</b>).
        /// **The pattern is decided by <c>Core/Volcano/LavaGlow</c>** — the cooled crust, the
        /// glowing cracks between the plates, and the hot bands at both ends (the crater and the
        /// advancing front).
        ///
        /// ★★ **The UVs do not move.** This used to bake light-and-dark stripes here and scroll
        ///   them every frame, and that was the "flicker" of report ④ (that class's doc).
        ///
        /// If it cannot be built we simply do not assign it, and the band comes out flat-coloured.
        /// </summary>
        private static Texture2D BuildTexture()
        {
            try
            {
                var pixels = new Color32[TextureSize * TextureSize];

                for (int y = 0; y < TextureSize; y++)
                {
                    float v = y / (float)(TextureSize - 1);

                    for (int x = 0; x < TextureSize; x++)
                    {
                        float u = x / (float)(TextureSize - 1);

                        float glow = LavaGlow.GlowUnit(u, v);
                        float alpha = LavaGlow.AcrossFalloff(u) * (0.30f + 0.70f * glow);

                        // Hot places go yellow-white; cooled ones fall to a dark reddish brown.
                        float g2 = glow * glow;
                        pixels[y * TextureSize + x] = new Color32(
                            Byte(0.10f + 0.90f * glow),
                            Byte(0.02f + 0.62f * g2),
                            Byte(0.01f + 0.22f * g2 * glow),
                            Byte(alpha));
                    }
                }

                var t = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                t.name = "DisasterPlus_VolcanoLavaBand";
                // ★ Both axes cover [0,1] with a single tile. **It does not repeat** —
                //   v = 0 means the crater and v = 1 means the advancing front; the axis carries
                //   meaning.
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                t.SetPixels32(pixels);
                t.Apply(false, false);
                return t;
            }
            catch
            {
                // The lava still appears if this cannot be built (it just loses its pattern).
                // Nothing is logged.
                return null;
            }
        }

        /// <summary>[0,1] to a byte in 0-255.</summary>
        private static byte Byte(float v)
        {
            int i = Mathf.RoundToInt(v * 255f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }

        /// <summary>
        /// Fade the colour with the cooling. The requirement is that **stopped lava does not glow
        /// for ever**: it darkens as <c>VolcanoLava.CoolUnit</c> falls from 1 to 0 and
        /// <b>disappears exactly at 0</b> (<c>LavaGlow.CoolFade</c>).
        ///
        /// ★★ This used to be <c>k = 0.15 + 0.85 × cool</c> /
        ///   <c>α = 0.55 + 0.45 × cool</c>. **A formula that leaves k = 0.15 / α = 0.55 even once
        ///   it has cooled out**, so it never disappeared however long you waited (exactly the
        ///   second half of report ④).
        ///
        /// **Do not write it every frame** (<see cref="TintStep"/>).
        /// </summary>
        private static void ApplyTint(float coolUnit)
        {
            float cool = coolUnit;
            if (float.IsNaN(cool)) cool = 0f;
            if (cool < 0f) cool = 0f;
            if (cool > 1f) cool = 1f;

            if (_tintedCool >= 0f && Mathf.Abs(cool - _tintedCool) < TintStep) return;
            _tintedCool = cool;

            // The cooler it gets, the darker the red. **At 0 it disappears completely.**
            float k = LavaGlow.CoolFade(cool);
            var tint = new Color(k, k * 0.55f, k * 0.2f, k);

            // Particle shaders tint through _TintColor, Standard through _Color (the distinction
            // ③ settled). **Do not settle for writing the one that has no effect**, so write only
            // to properties that actually exist.
            if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", tint);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", tint);
        }

        // ★ SimulationIsPaused (to stop the UV scrolling while paused) used to live here.
        //   **It was deleted because there is no longer anything that moves** (LavaGlow).

        private static void DestroyMesh()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _mesh = null;
            _builtPoints = null;
            _buildFailed = false;
            _pointsDrawn = 0;
        }

        /// <summary>
        /// **Call on level unload, and when the lava rendering is turned off in the settings.**
        /// Main thread only.
        ///
        /// None of <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> is a <c>Component</c>, so
        /// destroying the <c>GameObject</c> does not take them with it.
        /// **Call <c>Object.Destroy</c> on them ourselves.** Idempotent.
        /// </summary>
        public static void Destroy()
        {
            DestroyMesh();

            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;

            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            _texture = null;

            _hasMainTex = false;
            _tintedCool = -1f;
            _drawCalls = 0;
            _shaderMissCount = 0;
            // ★ _shaderName / _shaderDetail are the facts the diagnostics use to state "what it
            //   resolved with", so they are not cleared when leaving a city. _shaderWarned /
            //   _errorLogged are not reset for the same reason (they are facts about the build of
            //   the game, not per-city state).
        }

        private static int Min(int a, int b)
        {
            return a < b ? a : b;
        }

        private static int Clamp(int value, int max)
        {
            if (value < 0) return 0;
            if (value > max) return max < 0 ? 0 : max;
            return value;
        }
    }
}
