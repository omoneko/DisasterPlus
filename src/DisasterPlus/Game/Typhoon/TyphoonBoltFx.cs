using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Lightning flashing inside the typhoon's cloud.</b> **Main thread only** (Unity
    /// objects).
    ///
    /// ── The owner's instruction (2026-08-25) ──────────────────────────────
    ///
    /// &gt; The lightning still appears above the typhoon's cloud. It might work better
    /// &gt; to drop the "thunderstorm" and have just "rain", with lightning occasionally
    /// &gt; coming out of the typhoon's clouds
    ///
    /// ── Two halves of one fix ─────────────────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>Do not let the game drop lightning.</b> <c>TyphoonWeather</c> stops the
    ///   rain at <c>MaxRainWithoutLightning</c> (0.8). The only condition under which
    ///   vanilla drops lightning out of the sky is <c>m_currentRain &gt; 0.8</c>
    ///   (measured in <c>WeatherManager.SimulationStepImpl</c> IL_09D3)</item>
    /// <item><b>Draw it inside the cloud ourselves.</b> That is this type</item>
    /// </list>
    ///
    /// ── The construction is the same as ⑤'s ash-plume lightning ───────────
    ///
    /// We reuse the road already travelled in <c>VolcanoCraterFx</c>:
    ///
    /// <list type="bullet">
    /// <item>Borrow only the shader from <see cref="ShaderPool"/> and build the
    ///   <c>Material</c> ourselves (**never reuse a borrowed <c>Material</c>
    ///   instance**)</item>
    /// <item>Point the ribbon <b>at the camera</b>. With a fixed orientation the quad
    ///   turns edge-on and disappears when viewed from that direction</item>
    /// <item>Collapse leftover vertices onto vertex 0. Leave them at <c>(0,0,0)</c> and
    ///   <c>RecalculateBounds</c> **stretches the bounds all the way to the map origin and
    ///   kills frustum culling**</item>
    /// </list>
    ///
    /// Only the shape differs — the ash plume runs almost straight up inside a narrow
    /// column, while the typhoon runs sideways inside a flat cloud (cloud-to-cloud
    /// discharge. <see cref="TyphoonBolt"/>'s class doc).
    /// </summary>
    public static class TyphoonBoltFx
    {
        /// <summary>The width of the lightning ribbon (m). Too thin and it disappears at a
        /// distance.</summary>
        private const float BoltWidthMetres = 26f;

        /// <summary>The render layer. The same as ⑤'s crater.</summary>
        private const int BoltLayer = 0;

        /// <summary>How many frames to wait before trying again when the shader could not
        /// be resolved.</summary>
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

        private static readonly TyphoonBoltPoint[] _path =
            new TyphoonBoltPoint[TyphoonBolt.PointCount];

        /// <summary>How many bolts were drawn on the most recent frame
        /// (diagnostics).</summary>
        public static int BoltsDrawn { get; private set; }

        /// <summary>Whether the material could be built (diagnostics). If not, nothing is
        /// drawn.</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>
        /// **Main thread, every frame.**
        /// <paramref name="altitudeMetres"/> is the cloud base height and
        /// <paramref name="thicknessMetres"/> the cloud thickness (pass **the same
        /// values** <c>TyphoonCloudFx</c> passes to the particles — if they drift apart
        /// the lightning flashes outside the cloud).
        /// </summary>
        public static void Update(TyphoonSnapshot snapshot, RenderManager.CameraInfo camera,
                                  float radiusMetres, float altitudeMetres,
                                  float thicknessMetres)
        {
            BoltsDrawn = 0;

            try
            {
                Step(snapshot, camera, radiusMetres, altitudeMetres, thicknessMetres);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon lightning failed", e);
                }
                else
                {
                    Log.Diag("typhoonBolt", "lightning failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(TyphoonSnapshot snapshot, RenderManager.CameraInfo camera,
                                 float radiusMetres, float altitudeMetres,
                                 float thicknessMetres)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active) return;
            if (camera == null) return;
            if (!(radiusMetres > 0f)) return;
            if (!ModSettings.TyphoonLightning.value) return;

            // ★ Advance the clock only while a typhoon is about. **Do not advance it on
            //   frames where things are stopped** (do that and the lightning's timing
            //   carries on running while the game is paused).
            float dt = Time.deltaTime;
            if (dt > 0f && !SimulationIsPaused()) _clockSeconds += dt;

            if (_material == null) _material = BuildMaterial();
            if (_material == null) return;

            EnsureBuffers();

            uint seed = DeterministicRandom.Hash(snapshot.TyphoonId, 0x424F4C54u);
            float unit = snapshot.Intensity / 255f;

            int slot = TyphoonBolt.SlotAt(_clockSeconds);
            Vector3 right = CameraRight(camera);
            Vec3 centre = snapshot.Centre;

            int v = 0;
            int t = 0;

            for (int back = 0; back < TyphoonBolt.MaxBolts; back++)
            {
                int s = slot - back;
                if (s < 0) break;

                float k = TyphoonBolt.BrightnessAt(seed, s, _clockSeconds, unit);
                if (!(k > 0.02f)) continue;

                int n = TyphoonBolt.PathInto(_path, seed, s, radiusMetres, thicknessMetres);
                if (n < 2) continue;

                AppendBolt(ref v, ref t, centre, altitudeMetres, n, k, right);
                BoltsDrawn++;
            }

            if (BoltsDrawn == 0) return;
            UploadAndDraw(v, t);
        }

        /// <summary>
        /// Turn one polyline into a ribbon. <paramref name="right"/> is **the horizontal
        /// right as seen from the camera**, and since we spread towards it, the quad never
        /// turns edge-on and disappears from any viewing angle.
        /// </summary>
        private static void AppendBolt(ref int v, ref int t, Vec3 centre, float altitude,
                                       int n, float brightness, Vector3 right)
        {
            Color32 hot = Spark(brightness);
            Color32 edge = Spark(brightness * 0.15f);

            float half = BoltWidthMetres * 0.5f;

            for (int i = 0; i < n - 1; i++)
            {
                TyphoonBoltPoint a = _path[i];
                TyphoonBoltPoint b = _path[i + 1];

                int baseIndex = v;

                _vertices[v] = new Vector3(centre.X + a.X - right.x * half,
                                           altitude + a.Y,
                                           centre.Z + a.Z - right.z * half);
                _uvs[v] = new Vector2(0f, 0f);
                _colors[v] = edge;
                v++;

                _vertices[v] = new Vector3(centre.X + a.X + right.x * half,
                                           altitude + a.Y,
                                           centre.Z + a.Z + right.z * half);
                _uvs[v] = new Vector2(1f, 0f);
                _colors[v] = edge;
                v++;

                _vertices[v] = new Vector3(centre.X + b.X + right.x * half,
                                           altitude + b.Y,
                                           centre.Z + b.Z + right.z * half);
                _uvs[v] = new Vector2(1f, 1f);
                _colors[v] = hot;
                v++;

                _vertices[v] = new Vector3(centre.X + b.X - right.x * half,
                                           altitude + b.Y,
                                           centre.Z + b.Z - right.z * half);
                _uvs[v] = new Vector2(0f, 1f);
                _colors[v] = hot;
                v++;

                _triangles[t++] = baseIndex;
                _triangles[t++] = baseIndex + 1;
                _triangles[t++] = baseIndex + 2;
                _triangles[t++] = baseIndex;
                _triangles[t++] = baseIndex + 2;
                _triangles[t++] = baseIndex + 3;
            }
        }

        /// <summary>**Call on level unload and when the typhoon ends.** Idempotent.</summary>
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
            BoltsDrawn = 0;
            // _shaderWarned / _errorLogged are not reset (they are facts about this
            // environment).
        }

        // ------------------------------------------------------------------

        private static int MaxVertices
        {
            get { return TyphoonBolt.MaxBolts * (TyphoonBolt.PointCount - 1) * 4; }
        }

        private static int MaxTriangleIndices
        {
            get { return TyphoonBolt.MaxBolts * (TyphoonBolt.PointCount - 1) * 6; }
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
        /// Put only what we used into the mesh and draw it once.
        ///
        /// ★★ **Collapse leftover vertices onto the same position as vertex 0.** Leave
        ///   them at <c>(0,0,0)</c> and <c>RecalculateBounds</c> **stretches the bounds all
        ///   the way to the map origin and kills frustum culling** (the same hole we fell
        ///   into in ⑤).
        /// </summary>
        private static void UploadAndDraw(int vertexCount, int triangleCount)
        {
            if (vertexCount <= 0 || triangleCount <= 0) return;

            for (int i = vertexCount; i < _vertices.Length; i++) _vertices[i] = _vertices[0];
            for (int i = triangleCount; i < _triangles.Length; i++) _triangles[i] = 0;

            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "DisasterPlus_TyphoonBolt";
                _mesh.MarkDynamic();
            }

            // ★ Empty the triangles before putting the vertices in. Do it the other way
            //   round and the previous frame's triangles point into the new (shorter)
            //   vertex array and throw on the spot.
            _mesh.triangles = null;
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.colors32 = _colors;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();

            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, BoltLayer,
                              null, 0, null, false, false);
        }

        /// <summary>The lightning colour. **Blue, close to white.** The blend is additive,
        /// so alpha acts on the brightness.</summary>
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
                    Log.Warn("typhoon lightning: no usable shader resolved; the bolts inside "
                             + "the cloud are not drawn. The rain, the wind and the cloud "
                             + "itself are unaffected");
                }
                _shaderMissCount = ShaderRetryFrames;
                return null;
            }

            var m = new Material(pick.Shader);
            m.name = "DisasterPlus_TyphoonBolt";

            if (_texture == null) _texture = BuildTexture();
            if (_texture != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _texture);

            if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            // ★ The colour travels in the vertex colours, so leave the tint white.
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>One sheet, bright in the middle and transparent at the edges (the same
        /// construction as ⑤'s crater).</summary>
        private static Texture2D BuildTexture()
        {
            try
            {
                const int Size = 64;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.name = "DisasterPlus_TyphoonBoltTex";
                tex.wrapMode = TextureWrapMode.Clamp;

                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size * 2f - 1f;
                        float d = u < 0f ? -u : u;

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
                Log.Warn("typhoon lightning texture failed: " + e.GetType().Name);
                return null;
            }
        }

        private static Vector3 CameraRight(RenderManager.CameraInfo camera)
        {
            Vector3 forward = camera.m_forward;
            forward.y = 0f;

            float len = forward.magnitude;
            if (len < 0.001f) return new Vector3(1f, 0f, 0f);

            forward /= len;
            return new Vector3(forward.z, 0f, -forward.x);
        }

        private static bool SimulationIsPaused()
        {
            try
            {
                return ColossalFramework.Singleton<SimulationManager>.exists
                       && ColossalFramework.Singleton<SimulationManager>.instance
                              .SimulationPaused;
            }
            catch
            {
                return false;
            }
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
