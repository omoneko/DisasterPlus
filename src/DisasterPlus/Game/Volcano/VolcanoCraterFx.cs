using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **火口のマグマだまり・噴煙への光・噴煙の中の雷。main スレッド専用、毎フレーム。**
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// > 噴火口のマグマだまり（溶岩同様光る）と噴煙への光の放射、
    /// > 噴煙の中で雷（噴石同士が当たって生じるやつ）が発生するのを再現してほしい
    ///
    /// ── ★★ 粒子ではなく自前の <c>DrawMesh</c> である ────────────────────
    ///
    /// この 3 つはどれも<b>バニラの粒子エフェクトでは出せない</b>:
    ///
    ///   - マグマだまり … <c>Fire Particles</c> は炎であって溜まりではない。
    ///     求められているのは<b>溶岩と同じ光り方</b>で、それは
    ///     <see cref="VolcanoLavaFx"/> が既に自前のメッシュで出している
    ///   - 噴煙への光 … 粒子の色はプレハブのものなので、こちらからは触れない
    ///   - 雷 … バニラの雷（<c>Custom/Effects/Lightning</c>）は
    ///     <b>雲から地面へ</b>のもので、火山雷は柱の中で完結する
    ///
    /// だから <see cref="VolcanoLavaFx"/> と同じ手口を取る ——
    /// **シェーダだけ <see cref="ShaderPool"/> から借り、<c>Material</c> は自作する。**
    /// CS の <c>Material</c> インスタンスを借りると自前の <c>DrawMesh</c> では
    /// 不可視になる（罠 1。あちらのクラス doc）。
    ///
    /// ── 1 フレーム 1 回の <c>DrawMesh</c> ───────────────────────────
    ///
    /// 3 つとも 1 枚のメッシュに詰める。頂点は最大で
    /// <c>だまり 2+<see cref="PoolSegments"/> ＋ 光 <see cref="LightRings"/>×(<see cref="PoolSegments"/>+1)
    /// ＋ 雷 <see cref="PlumeLightning.MaxBolts"/>×(<see cref="PlumeLightning.PointCount"/>-1)×4</c>
    /// ＝ 400 弱で、**毎フレーム組み直しても問題にならない**大きさである
    /// （雷は毎フレーム形が変わるので、組み直さない選択肢が無い）。
    ///
    /// ── ★★ ポーズ中は進めない ─────────────────────────────
    ///
    /// 時計は <see cref="VolcanoVanillaFx.EffectTimeDelta"/> から進める
    /// （<c>Time.deltaTime</c> ではない）。一時停止で雷が光り続けるのは嘘である。
    ///
    /// ── 静的キャッシュの罠 ─────────────────────────────────
    ///
    /// <c>Mesh</c> / <c>Material</c> は**参照 1 個ずつ**で持つ。配列に入れると
    /// 破棄済み（fake-null）を抱えたまま非 null になり、2 つ目の都市で無言で
    /// 不可視になる（<see cref="VolcanoLavaFx"/> のクラス doc の実例）。
    /// </summary>
    public static class VolcanoCraterFx
    {
        /// <summary>だまりの円と光の輪の分割数。</summary>
        private const int PoolSegments = 24;

        /// <summary>光の輪の枚数（下から上へ、暗くなりながら重ねる）。</summary>
        private const int LightRings = 5;

        /// <summary>だまりを火口の底から浮かせる量（m）。z-fighting を避けるだけ。</summary>
        private const float PoolLiftMetres = 1.5f;

        /// <summary>雷の帯の幅（m）。細すぎると遠景で消える。</summary>
        private const float BoltWidthMetres = 9f;

        /// <summary>描画レイヤー。<see cref="VolcanoLavaFx"/> と同じ。</summary>
        private const int CraterLayer = 0;

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

        private static readonly LightningPoint[] _boltPath =
            new LightningPoint[PlumeLightning.PointCount];

        /// <summary>直近のフレームで描いた雷の本数（診断用）。</summary>
        public static int BoltsDrawn { get; private set; }

        /// <summary>マテリアルが作れているか（診断用）。作れなければ何も描かない。</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>直近のフレームで火口の光を描いたか（診断用）。</summary>
        public static bool Drawing { get; private set; }

        /// <summary>
        /// **main スレッド、毎フレーム。** <paramref name="camera"/> が null のフレームは
        /// 何も描かない（<c>DrawMesh</c> 自体はカメラを要らないが、描く理由も無い）。
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

            // ★★ **噴火の段だけ。** 隆起の途中から噴煙は出ているが、
            //    マグマだまりが見えるのは火口が彫れてからである。
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
        // 形
        // ------------------------------------------------------------------

        /// <summary>
        /// マグマだまり。火口の底に置く水平な円盤で、**中心がいちばん明るい**
        /// （溶岩の <c>LavaGlow</c> と同じ向き）。
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
                // ★ 縁は暗い。**縁まで同じ明るさだと、円盤の輪郭が線に見える。**
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
        /// 噴煙へ届く光。火口の上へ<b>水平な輪を重ねる</b>だけで、上ほど暗い。
        /// 明るさは <c>Core/Volcano/CraterGlow.LightAt</c> が決める
        /// （届く高さでちょうど 0 になる —— 上まで薄く光らせない）。
        /// </summary>
        private static void AppendLight(ref int v, ref int t, Vec3 vent, float poolRadius,
                                        float plumeHeightMetres, float brightness)
        {
            if (!(plumeHeightMetres > 0f)) return;

            float reach = plumeHeightMetres * CraterGlow.ReachFraction;
            if (!(reach > 0f)) return;

            for (int ring = 0; ring < LightRings; ring++)
            {
                // 下の輪ほど密。光は火口の近くで急に落ちる。
                float s = (ring + 1) / (float)LightRings;
                float y = reach * s * s;

                float k = CraterGlow.LightAt(y, plumeHeightMetres, brightness);
                if (!(k > 0.01f)) continue;

                // 上ほど広がる（噴煙が太るのに合わせる）。
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
        /// 噴煙の中の雷。<c>Core/Volcano/PlumeLightning</c> が枠ごとに 1 本ずつ決めた
        /// 折れ線を、**カメラのほうを向いた帯**として描く。
        ///
        /// ★ 色は<b>白に近い青</b>である。マグマの橙と同じ色にすると、
        ///   光っているのが溶岩なのか放電なのか見分けが付かない。
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

            // ★★ **帯はカメラのほうを向けること。**
            //    以前は「その区間が水平に進む向きの直角」へ広げていたが、
            //    雷はほぼ真上へ走るので水平の進みがほとんど無く、向きが安定しない。
            //    しかも固定の向きだと、**その向きから見たときに板が真横を向いて消える**。
            //    カメラの前向きから水平の右方向を作って、そちらへ広げる。
            Vector3 right = CameraRight(camera);

            // ★ 柱の形は EruptionColumn が持っている。Core にはこの 1 本の
            //   デリゲートだけを渡す（あちらから型に触らせない）。
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
        /// 折れ線 1 本を帯にする。<paramref name="right"/> は**カメラから見た水平の右**で、
        /// そちらへ広げるので、どの角度から見ても板が真横を向いて消えることが無い。
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
        /// カメラから見た**水平の右向き**（長さ 1）。
        ///
        /// <c>RenderManager.CameraInfo.m_forward</c> の水平成分から作る。
        ///
        /// ★ <c>CameraInfo</c> には <c>m_right</c> もある（IL で確認済み）。傾いていない
        ///   カメラでは同じ値になるが、**ロールが入ると <c>m_right</c> は水平でなくなる**。
        ///   ここが欲しいのは「水平の右」なので、前向きから作り直す。
        ///
        /// 真下を向いているフレーム（水平成分が 0）では決めようが無いので、
        /// **適当な向きに倒す** —— 真下から見ているなら、帯がどちらを向いていても
        /// 面積は同じである。
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
        // Unity 側
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
        /// 使った分だけをメッシュへ入れて 1 回描く。
        ///
        /// ★★ **余った頂点は 0 番目と同じ位置に潰す**（<see cref="VolcanoLavaFx"/> と同じ）。
        ///   <c>(0,0,0)</c> のまま残すと、三角形が 1 つも指していなくても
        ///   <c>RecalculateBounds</c> が**マップの原点まで境界を伸ばして視錐台カリングを殺す**。
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
                // ★ 毎フレーム書き換える。Unity にそう伝えると再確保が減る。
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

            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, CraterLayer,
                              null, 0, null, false, false);
        }

        /// <summary>
        /// マグマの色。**橙**（<see cref="VolcanoLavaFx"/> のティントと同じ向き）。
        /// 加算合成なのでアルファは明るさそのものとして効く。
        /// </summary>
        private static Color32 Tint(float k)
        {
            float v = Clamp01(k);
            return new Color32((byte)(255f * v),
                               (byte)(255f * v * 0.42f),
                               (byte)(255f * v * 0.12f),
                               (byte)(255f * v));
        }

        /// <summary>放電の色。**白に近い青**（マグマの橙と見分けが付くこと）。</summary>
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
            // ★ 毎フレーム探しに行かない（VolcanoLavaFx と同じ間引き）。
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

            // ★ 借りてきた別のシェーダには掛けない（_Mode / _SrcBlend は Standard の契約）。
            if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            // ★ 色は頂点カラーで運ぶので、ティントは白のままにする
            //   （ここで色を入れると頂点カラーと二重に掛かる）。
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>
        /// 中心が明るく縁が透明な 1 枚。だまりにも光の輪にも雷にも同じものを使う
        /// （雷は帯を横切る方向にだけ使うので、縁が透ける形で足りる）。
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
        /// **レベルアンロードと、設定で切ったときに呼ぶ。** 冪等。
        /// <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> はどれも <c>Component</c> では
        /// ないので、親の <c>GameObject</c> の道連れにならない ——**明示的に破棄する。**
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
            // _shaderWarned / _errorLogged は戻さない（ゲームのビルドに対する事実である）。
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
