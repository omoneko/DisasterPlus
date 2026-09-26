using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The crater's magma pool, the light on the plume, and the lightning inside the plume.
    /// Main thread only, every frame.**
    ///
    /// ── the request (2026-08-22) ───────────────────────────────────────────────────────────
    ///
    /// > I'd like you to reproduce the magma pool in the crater (glowing like the lava), the
    /// > light it casts onto the plume, and the lightning that occurs inside the plume (the kind
    /// > caused by ejecta colliding with each other)
    ///
    /// ── ★★ this is our own <c>DrawMesh</c>, not particles ─────────────────────────────────
    ///
    /// None of these three <b>can be done with vanilla's particle effects</b>:
    ///
    ///   - the magma pool … <c>Fire Particles</c> is flame, not a pool. What is asked for is
    ///     <b>the same glow as the lava</b>, and <see cref="VolcanoLavaFx"/> already does that
    ///     with its own mesh
    ///   - the light on the plume … a particle's colour belongs to its prefab, so we cannot touch
    ///     it from here
    ///   - the lightning … vanilla's lightning (<c>Custom/Effects/Lightning</c>) goes
    ///     <b>from cloud to ground</b>, whereas volcanic lightning stays within the column
    ///
    /// So we use the same technique as <see cref="VolcanoLavaFx"/> —
    /// **borrow only the shader from <see cref="ShaderPool"/> and build the <c>Material</c>
    /// ourselves.** Borrow a CS <c>Material</c> instance and it comes out invisible in our own
    /// <c>DrawMesh</c> (trap 1; that class's doc).
    ///
    /// ── one <c>DrawMesh</c> per frame ──────────────────────────────────────────────────────
    ///
    /// All three are packed into a single mesh. The vertex count is at most
    /// <c>pool 2+<see cref="PoolSegments"/> + light <see cref="LightRings"/>×(<see cref="PoolSegments"/>+1)
    /// + lightning <see cref="PlumeLightning.MaxBolts"/>×(<see cref="PlumeLightning.PointCount"/>-1)×4</c>
    /// = just under 400, which is small enough that **rebuilding it every frame is not a problem**
    /// (the lightning changes shape every frame, so there is no option not to rebuild).
    ///
    /// ── ★★ do not advance while paused ───────────────────────────────────────────────────
    ///
    /// The clock is advanced from <see cref="VolcanoVanillaFx.EffectTimeDelta"/>
    /// (not <c>Time.deltaTime</c>). Lightning that keeps flashing while paused is a lie.
    ///
    /// ── the static cache trap ──────────────────────────────────────────────────────────────
    ///
    /// The <c>Mesh</c> / <c>Material</c> are held as **one reference each**. Put them in an array
    /// and they stay non-null while holding a destroyed (fake-null) object, and go silently
    /// invisible in the second city (the worked example is in the class doc of
    /// <see cref="VolcanoLavaFx"/>).
    /// </summary>
    public static class VolcanoCraterFx
    {
        /// <summary>The number of segments in the pool's circle and the light rings.</summary>
        private const int PoolSegments = 24;

        /// <summary>The number of light rings (stacked from the bottom up, getting darker).</summary>
        private const int LightRings = 5;

        /// <summary>How far the pool floats above the crater floor (m). Purely to avoid z-fighting.</summary>
        private const float PoolLiftMetres = 1.5f;

        /// <summary>The width of a lightning band (m). Too thin and it disappears at distance.</summary>
        private const float BoltWidthMetres = 9f;

        /// <summary>The drawing layer. The same as <see cref="VolcanoLavaFx"/>.</summary>
        private const int CraterLayer = 0;

        /// <summary>Frames to wait before trying again when the shader could not be looked up.</summary>
        private const int ShaderRetryFrames = 300;

        private static Mesh _mesh;
        private static Material _material;
        private static Texture2D _texture;

        private static float _clockSeconds;
        private static int _shaderMissCount;
        private static bool _shaderWarned;
        private static bool _errorLogged;

        private static Vector3[] _vertices;
        private static Vector2[] _uvs;
        private static Color32[] _colors;
        private static int[] _triangles;

        private static readonly LightningPoint[] _boltPath =
            new LightningPoint[PlumeLightning.PointCount];

        /// <summary>The number of bolts drawn in the last frame (for diagnostics).</summary>
        public static int BoltsDrawn { get; private set; }

        /// <summary>Whether the material could be built (for diagnostics). If not, nothing is drawn.</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>Whether the crater glow was drawn in the last frame (for diagnostics).</summary>
        public static bool Drawing { get; private set; }

        /// <summary>
        /// **Main thread, every frame.** On frames where <paramref name="camera"/> is null,
        /// nothing is drawn (<c>DrawMesh</c> itself does not need a camera, but there is no reason
        /// to draw either).
        /// </summary>
        public static void Update(VolcanoSnapshot snapshot, RenderManager.CameraInfo camera,
                                  float plumeHeightMetres, EruptionColumn column)
        {
            Drawing = false;
            BoltsDrawn = 0;

            try
            {
                Step(snapshot, camera, plumeHeightMetres, column);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano crater glow failed", e);
                }
                else
                {
                    Log.Diag("volcanoCrater", "crater glow failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoSnapshot snapshot, RenderManager.CameraInfo camera,
                                 float plumeHeightMetres, EruptionColumn column)
        {
            if (snapshot == null || !snapshot.Valid) return;
            if (!snapshot.Footprint.Valid) return;
            if (!ModSettings.VolcanoEnabled.value) return;
            if (!ModSettings.VolcanoEruptionFx.value) return;
            if (camera == null) return;

            // ★★ **The eruption stage only.** The plume has been coming out since partway through
            //    the uplift, but the magma pool is only visible once the crater has been carved.
            if (!snapshot.EruptionActive) return;

            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt > 0f) _clockSeconds += dt;
            if (dt <= 0f) return;

            if (_material == null) _material = BuildMaterial();
            if (_material == null) return;

            Vec3 vent = snapshot.VentWorld;
            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            float craterRadius = VolcanoShape.CraterRadiusOf(snapshot.Footprint.RadiusMetres);

            float poolRadius = CraterGlow.PoolRadiusMetres(craterRadius, unit);
            if (!(poolRadius > 0f)) return;

            float brightness = CraterGlow.PoolBrightness(unit, _clockSeconds);

            EnsureBuffers();
            int v = 0;
            int t = 0;

            AppendPool(ref v, ref t, vent, poolRadius, brightness);
            AppendLight(ref v, ref t, vent, poolRadius, plumeHeightMetres, brightness);
            AppendBolts(ref v, ref t, vent, plumeHeightMetres, unit, column, camera);

            if (t == 0) return;

            UploadAndDraw(v, t);
            Drawing = true;
        }

        // ------------------------------------------------------------------
        // Shapes
        // ------------------------------------------------------------------

        /// <summary>
        /// The magma pool. A horizontal disc laid on the crater floor, **brightest at the centre**
        /// (the same direction as the lava's <c>LavaGlow</c>).
        /// </summary>
        private static void AppendPool(ref int v, ref int t, Vec3 vent, float radius,
                                       float brightness)
        {
            int centre = v;
            float y = vent.Y + PoolLiftMetres;

            _vertices[v] = new Vector3(vent.X, y, vent.Z);
            _uvs[v] = new Vector2(0.5f, 0.5f);
            _colors[v] = Tint(brightness);
            v++;

            for (int i = 0; i <= PoolSegments; i++)
            {
                float a = 6.2831853f * i / PoolSegments;
                float cx = (float)Math.Cos(a);
                float cz = (float)Math.Sin(a);

                _vertices[v] = new Vector3(vent.X + cx * radius, y, vent.Z + cz * radius);
                _uvs[v] = new Vector2(0.5f + cx * 0.5f, 0.5f + cz * 0.5f);
                // ★ The rim is dark. **Keep it at the same brightness to the rim and the disc's
                //   outline reads as a line.**
                _colors[v] = Tint(brightness * 0.25f);
                v++;
            }

            for (int i = 0; i < PoolSegments; i++)
            {
                _triangles[t++] = centre;
                _triangles[t++] = centre + 1 + i;
                _triangles[t++] = centre + 2 + i;
            }
        }

        /// <summary>
        /// The light reaching the plume. Simply <b>stacks horizontal rings</b> above the crater,
        /// darker towards the top.
        /// The brightness is decided by <c>Core/Volcano/CraterGlow.LightAt</c>
        /// (it reaches exactly 0 at the height it carries to — do not leave a faint glow all the
        /// way up).
        /// </summary>
        private static void AppendLight(ref int v, ref int t, Vec3 vent, float poolRadius,
                                        float plumeHeightMetres, float brightness)
        {
            if (!(plumeHeightMetres > 0f)) return;

            float reach = plumeHeightMetres * CraterGlow.ReachFraction;
            if (!(reach > 0f)) return;

            for (int ring = 0; ring < LightRings; ring++)
            {
                // The lower rings are denser. The light falls off sharply near the crater.
                float s = (ring + 1) / (float)LightRings;
                float y = reach * s * s;

                float k = CraterGlow.LightAt(y, plumeHeightMetres, brightness);
                if (!(k > 0.01f)) continue;

                // Wider towards the top (matching the plume as it thickens).
                float radius = poolRadius * (1f + 1.6f * s);

                int centre = v;
                _vertices[v] = new Vector3(vent.X, vent.Y + y, vent.Z);
                _uvs[v] = new Vector2(0.5f, 0.5f);
                _colors[v] = Tint(k);
                v++;

                for (int i = 0; i <= PoolSegments; i++)
                {
                    float a = 6.2831853f * i / PoolSegments;
                    float cx = (float)Math.Cos(a);
                    float cz = (float)Math.Sin(a);

                    _vertices[v] = new Vector3(vent.X + cx * radius, vent.Y + y,
                                               vent.Z + cz * radius);
                    _uvs[v] = new Vector2(0.5f + cx * 0.5f, 0.5f + cz * 0.5f);
                    _colors[v] = Tint(0f);
                    v++;
                }

                for (int i = 0; i < PoolSegments; i++)
                {
                    _triangles[t++] = centre;
                    _triangles[t++] = centre + 1 + i;
                    _triangles[t++] = centre + 2 + i;
                }
            }
        }

        /// <summary>
        /// The lightning inside the plume. Draws the polyline that
        /// <c>Core/Volcano/PlumeLightning</c> decides, one per slot, as **a band facing the
        /// camera**.
        ///
        /// ★ The colour is <b>a near-white blue</b>. Make it the same colour as the magma's orange
        ///   and you cannot tell whether what is glowing is lava or a discharge.
        /// </summary>
        private static void AppendBolts(ref int v, ref int t, Vec3 vent,
                                        float plumeHeightMetres, float unit,
                                        EruptionColumn column,
                                        RenderManager.CameraInfo camera)
        {
            if (!ModSettings.VolcanoLightningFx.value) return;
            if (!(plumeHeightMetres > 0f)) return;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(vent.X)),
                unchecked((uint)Mathf.RoundToInt(vent.Z)));

            int slot = PlumeLightning.SlotAt(_clockSeconds);

            // ★★ **Make the band face the camera.**
            //    It used to widen perpendicular to "the direction that section travels
            //    horizontally", but lightning runs almost straight up, so there is hardly any
            //    horizontal travel and the direction is unstable.
            //    And with a fixed direction, **viewed from that direction the quad turns edge-on
            //    and disappears**.
            //    Build a horizontal right vector from the camera's forward and widen towards that.
            Vector3 right = CameraRight(camera);

            // ★ The column's shape is held by EruptionColumn. Core is handed only this one
            //   delegate (it is never allowed to touch the type).
            EruptionColumn shape = column;
            float height = plumeHeightMetres;
            RadiusAtFraction radiusAt = delegate(float fraction)
            {
                return shape.RadiusAt(height * fraction);
            };

            for (int back = 0; back < PlumeLightning.MaxBolts; back++)
            {
                int s = slot - back;
                if (s < 0) break;

                float k = PlumeLightning.BrightnessAt(seed, s, _clockSeconds, unit);
                if (!(k > 0.02f)) continue;

                int n = PlumeLightning.PathInto(_boltPath, seed, s, plumeHeightMetres, radiusAt);
                if (n < 2) continue;

                AppendBolt(ref v, ref t, vent, n, k, right);
                BoltsDrawn++;
            }
        }

        /// <summary>
        /// Turn one polyline into a band. <paramref name="right"/> is **horizontal right as seen
        /// from the camera**, and since we widen towards that, the quad never turns edge-on and
        /// disappears from any viewing angle.
        /// </summary>
        private static void AppendBolt(ref int v, ref int t, Vec3 vent, int n, float brightness,
                                       Vector3 right)
        {
            Color32 hot = Spark(brightness);
            Color32 edge = Spark(brightness * 0.15f);

            for (int i = 0; i < n - 1; i++)
            {
                LightningPoint a = _boltPath[i];
                LightningPoint b = _boltPath[i + 1];

                float nx = right.x;
                float nz = right.z;
                float half = BoltWidthMetres * 0.5f;

                int baseIndex = v;

                _vertices[v] = new Vector3(vent.X + a.X - nx * half, vent.Y + a.Y,
                                           vent.Z + a.Z - nz * half);
                _uvs[v] = new Vector2(0f, 0f);
                _colors[v] = edge;
                v++;

                _vertices[v] = new Vector3(vent.X + a.X + nx * half, vent.Y + a.Y,
                                           vent.Z + a.Z + nz * half);
                _uvs[v] = new Vector2(1f, 0f);
                _colors[v] = edge;
                v++;

                _vertices[v] = new Vector3(vent.X + b.X - nx * half, vent.Y + b.Y,
                                           vent.Z + b.Z - nz * half);
                _uvs[v] = new Vector2(0f, 1f);
                _colors[v] = hot;
                v++;

                _vertices[v] = new Vector3(vent.X + b.X + nx * half, vent.Y + b.Y,
                                           vent.Z + b.Z + nz * half);
                _uvs[v] = new Vector2(1f, 1f);
                _colors[v] = hot;
                v++;

                _triangles[t++] = baseIndex;
                _triangles[t++] = baseIndex + 2;
                _triangles[t++] = baseIndex + 1;

                _triangles[t++] = baseIndex + 1;
                _triangles[t++] = baseIndex + 2;
                _triangles[t++] = baseIndex + 3;
            }
        }

        /// <summary>
        /// **Horizontal right** as seen from the camera (unit length).
        ///
        /// Built from the horizontal component of <c>RenderManager.CameraInfo.m_forward</c>.
        ///
        /// ★ <c>CameraInfo</c> also has <c>m_right</c> (confirmed in IL). For an untilted camera
        ///   it gives the same value, but **<c>m_right</c> stops being horizontal once there is
        ///   roll**. What is wanted here is "horizontal right", so it is rebuilt from the forward
        ///   vector.
        ///
        /// On a frame looking straight down (horizontal component 0) there is no way to decide, so
        /// **fall back to an arbitrary direction** — seen from directly above, the band has the
        /// same area whichever way it faces.
        /// </summary>
        private static Vector3 CameraRight(RenderManager.CameraInfo camera)
        {
            Vector3 forward = camera.m_forward;
            forward.y = 0f;

            float len = forward.magnitude;
            if (len < 0.001f) return new Vector3(1f, 0f, 0f);

            forward /= len;
            return new Vector3(forward.z, 0f, -forward.x);
        }

        // ------------------------------------------------------------------
        // The Unity side
        // ------------------------------------------------------------------

        private static int MaxVertices
        {
            get
            {
                int pool = 1 + PoolSegments + 1;
                int light = LightRings * (1 + PoolSegments + 1);
                int bolts = PlumeLightning.MaxBolts * (PlumeLightning.PointCount - 1) * 4;
                return pool + light + bolts;
            }
        }

        private static int MaxTriangleIndices
        {
            get
            {
                int pool = PoolSegments * 3;
                int light = LightRings * PoolSegments * 3;
                int bolts = PlumeLightning.MaxBolts * (PlumeLightning.PointCount - 1) * 6;
                return pool + light + bolts;
            }
        }

        private static void EnsureBuffers()
        {
            if (_vertices != null) return;

            _vertices = new Vector3[MaxVertices];
            _uvs = new Vector2[MaxVertices];
            _colors = new Color32[MaxVertices];
            _triangles = new int[MaxTriangleIndices];
        }

        /// <summary>
        /// Put only what was used into the mesh and draw once.
        ///
        /// ★★ **Collapse the leftover vertices onto vertex 0** (the same as
        ///   <see cref="VolcanoLavaFx"/>).
        ///   Leave them at <c>(0,0,0)</c> and, even though no triangle points at them,
        ///   <c>RecalculateBounds</c> **stretches the bounds all the way to the map origin and
        ///   kills frustum culling**.
        /// </summary>
        private static void UploadAndDraw(int vertexCount, int triangleCount)
        {
            if (vertexCount <= 0 || triangleCount <= 0) return;

            for (int i = vertexCount; i < _vertices.Length; i++) _vertices[i] = _vertices[0];
            for (int i = triangleCount; i < _triangles.Length; i++) _triangles[i] = 0;

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "DisasterPlus_VolcanoCrater";
                // ★ It is rewritten every frame. Telling Unity so reduces reallocation.
                _mesh.MarkDynamic();
            }

            // ★ Empty the triangles before putting the vertices in. Reverse the order and the
            //   previous frame's triangles point into the new (shorter) vertex array and throw on
            //   the spot.
            _mesh.triangles = null;
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.colors32 = _colors;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();

            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, CraterLayer,
                              null, 0, null, false, false);
        }

        /// <summary>
        /// The magma's colour. **Orange** (the same direction as <see cref="VolcanoLavaFx"/>'s
        /// tint). The blending is additive, so the alpha acts as the brightness itself.
        /// </summary>
        private static Color32 Tint(float k)
        {
            float v = Clamp01(k);
            return new Color32((byte)(255f * v),
                               (byte)(255f * v * 0.42f),
                               (byte)(255f * v * 0.12f),
                               (byte)(255f * v));
        }

        /// <summary>The discharge's colour. **A near-white blue** (so it is distinguishable from the magma's orange).</summary>
        private static Color32 Spark(float k)
        {
            float v = Clamp01(k);
            return new Color32((byte)(255f * v * 0.82f),
                               (byte)(255f * v * 0.88f),
                               (byte)(255f * v),
                               (byte)(255f * v));
        }

        private static Material BuildMaterial()
        {
            // ★ Do not go looking every frame (the same throttling as VolcanoLavaFx).
            if (_shaderMissCount > 0)
            {
                _shaderMissCount--;
                return null;
            }

            ShaderPick pick = ShaderPool.Resolve(ShaderPreference.Additive);
            if (!pick.Usable)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    Log.Warn("volcano crater glow: no usable shader resolved; the magma pool, "
                             + "the light on the plume and the lightning are not drawn. "
                             + "The eruption itself is unaffected");
                }
                _shaderMissCount = ShaderRetryFrames;
                return null;
            }

            var m = new Material(pick.Shader);
            m.name = "DisasterPlus_VolcanoCrater";

            if (_texture == null) _texture = BuildTexture();
            if (_texture != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _texture);

            // ★ Do not apply it to some other borrowed shader (_Mode / _SrcBlend are Standard's
            //   contract).
            if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            // ★ The colour is carried by the vertex colours, so leave the tint white
            //   (put a colour in here and it is applied twice, on top of the vertex colours).
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>
        /// One texture, bright in the centre and transparent at the edge. The same one is used for
        /// the pool, the light rings and the lightning (the lightning only uses it across the
        /// band, so an edge that fades out is enough).
        /// </summary>
        private static Texture2D BuildTexture()
        {
            try
            {
                const int Size = 64;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.name = "DisasterPlus_VolcanoCraterTex";
                tex.wrapMode = TextureWrapMode.Clamp;

                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size * 2f - 1f;
                        float v = (y + 0.5f) / Size * 2f - 1f;
                        float d = (float)Math.Sqrt(u * u + v * v);

                        float k = d >= 1f ? 0f : (1f - d) * (1f - d);
                        byte b = (byte)(255f * Clamp01(k));
                        pixels[y * Size + x] = new Color32(255, 255, 255, b);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false);
                return tex;
            }
            catch (Exception e)
            {
                Log.Info("volcano crater glow: the texture could not be built ("
                         + e.GetType().Name + "); the glow uses a flat colour");
                return null;
            }
        }

        /// <summary>
        /// **Call on level unload and when turned off in the settings.** Idempotent.
        /// None of <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> is a <c>Component</c>, so they
        /// do not go down with the parent <c>GameObject</c> — **destroy them explicitly.**
        /// </summary>
        public static void Destroy()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);

            _mesh = null;
            _material = null;
            _texture = null;

            _vertices = null;
            _uvs = null;
            _colors = null;
            _triangles = null;

            _clockSeconds = 0f;
            _shaderMissCount = 0;
            Drawing = false;
            BoltsDrawn = 0;
            // _shaderWarned / _errorLogged are not reset (they are facts about the build of the game).
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
