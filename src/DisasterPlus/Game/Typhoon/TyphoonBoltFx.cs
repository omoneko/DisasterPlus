using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>台風の雲の中で光る稲妻。</b>**main スレッド専用**（Unity のオブジェクト）。
    ///
    /// ── 所有者の指示（2026-08-25）─────────────────────────────────
    ///
    /// &gt; まだ雷の発生場所が台風の雲より上です。いっそのこと「雷雨」にせずに
    /// &gt; 「雨」だけにして、時々台風の雲の中から稲妻を発生させる方がうまく
    /// &gt; いくかもしれません
    ///
    /// ── 2 つで 1 つの直しである ───────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>ゲームに雷を落とさせない。</b> <c>TyphoonWeather</c> が雨を
    ///   <c>MaxRainWithoutLightning</c>（0.8）で止める。バニラが空から雷を落とす
    ///   条件は <c>m_currentRain &gt; 0.8</c> ただ 1 つである
    ///   （<c>WeatherManager.SimulationStepImpl</c> IL_09D3 実測）</item>
    /// <item><b>雲の中に自分で描く。</b> それがこの型である</item>
    /// </list>
    ///
    /// ── 作りは⑤の噴煙の雷と同じ ─────────────────────────────────
    ///
    /// <c>VolcanoCraterFx</c> で通した道をそのまま使う:
    ///
    /// <list type="bullet">
    /// <item>シェーダだけ <see cref="ShaderPool"/> から借り、<c>Material</c> は自作する
    ///   （**借りた <c>Material</c> のインスタンスは絶対に使い回さない**）</item>
    /// <item>帯は<b>カメラのほうを向ける</b>。固定の向きだと、その向きから見たとき
    ///   板が真横を向いて消える</item>
    /// <item>余った頂点は 0 番目へ潰す。<c>(0,0,0)</c> のまま残すと
    ///   <c>RecalculateBounds</c> が**マップの原点まで境界を伸ばして
    ///   視錐台カリングを殺す**</item>
    /// </list>
    ///
    /// 形だけが違う —— 噴煙は細い柱の中をほぼ真上へ走り、台風は平たい雲の中を
    /// 横へ走る（雲間放電。<see cref="TyphoonBolt"/> のクラス doc）。
    /// </summary>
    public static class TyphoonBoltFx
    {
        /// <summary>雷の帯の幅（m）。細すぎると遠景で消える。</summary>
        private const float BoltWidthMetres = 26f;

        /// <summary>描画レイヤー。⑤の火口と同じ。</summary>
        private const int BoltLayer = 0;

        /// <summary>シェーダが引けなかったときに、次に試すまで待つフレーム数。</summary>
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

        /// <summary>直近のフレームで描いた稲妻の本数（診断用）。</summary>
        public static int BoltsDrawn { get; private set; }

        /// <summary>マテリアルが作れているか（診断用）。作れなければ何も描かない。</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>
        /// **main スレッド、毎フレーム。**
        /// <paramref name="altitudeMetres"/> は雲底の高さ、
        /// <paramref name="thicknessMetres"/> は雲の厚み
        /// （<c>TyphoonCloudFx</c> が粒に渡しているのと**同じ値**を渡すこと ——
        /// ずれると雷が雲の外で光る）。
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

            // ★ 時計は台風が居るあいだだけ進める。**止まっているフレームで進めない**
            //   （進めると、ポーズ中に稲妻の枠だけが流れる）。
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
        /// 折れ線 1 本を帯にする。<paramref name="right"/> は**カメラから見た水平の右**で、
        /// そちらへ広げるので、どの角度から見ても板が真横を向いて消えることが無い。
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

        /// <summary>**レベルアンロードと、台風が終わったときに呼ぶ。** 冪等。</summary>
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
            // _shaderWarned / _errorLogged は戻さない（この環境に対する事実である）。
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
        /// 使った分だけをメッシュへ入れて 1 回描く。
        ///
        /// ★★ **余った頂点は 0 番目と同じ位置に潰す。** <c>(0,0,0)</c> のまま残すと
        ///   <c>RecalculateBounds</c> が**マップの原点まで境界を伸ばして
        ///   視錐台カリングを殺す**（⑤で踏んだ穴と同じ）。
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

            // ★ 頂点を入れる前に三角形を空にする。順序を逆にすると、前のフレームの
            //   三角形が新しい（短い）頂点配列を指してその場で例外になる。
            _mesh.triangles = null;
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.colors32 = _colors;
            _mesh.triangles = _triangles;
            _mesh.RecalculateBounds();

            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, BoltLayer,
                              null, 0, null, false, false);
        }

        /// <summary>稲妻の色。**白に近い青。**加算合成なのでアルファは明るさに効く。</summary>
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

            // ★ 色は頂点カラーで運ぶので、ティントは白のままにする。
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>中心が明るく縁が透明な 1 枚（⑤の火口と同じ作り）。</summary>
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
