using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>雲が今どうなっているか。</summary>
    public enum TyphoonCloudState
    {
        /// <summary>設定で切っている（あるいはまだ都市に入っていない）。</summary>
        Off,

        /// <summary>台風が居ないので描いていない。**不具合ではない。**</summary>
        NotBuilt,

        /// <summary>毎フレーム描いている。</summary>
        Drawing,

        /// <summary>メッシュかマテリアルの構築が失敗した。</summary>
        BuildFailed,

        /// <summary><c>Shader.Find</c> が 1 つも解決しなかった。</summary>
        ShaderMissing,
    }

    /// <summary>
    /// 台風の巨大な回転雲。<b>main スレッド専用。</b>
    ///
    /// ── なぜゼロから作るのか（既製品が 1 つも無い） ──────────────────
    ///
    /// <c>DisasterInfo.m_effect</c> は**フィールドごと存在しない**。災害まわりで
    /// <c>EffectInfo</c> 型のフィールドを持つのは <c>DisasterProperties.m_mediumExplosion</c> と
    /// <c>MeteorAI.m_impactEffect</c> の 2 つだけで、**雲も渦もプレハブ化されていない**
    /// （IL 事実文書 §C-1）。そしてバニラの雲は半径 6400 km のスカイドームに貼られた
    /// ノイズシェーダで、位置は <c>(0, m_HorizonOffset, 0)</c> 固定・**ワールド座標を
    /// 持たない**。<c>m_Coverage</c> は毎フレーム <c>m_MaxCoverage × SampleCloudCoverage()</c> で
    /// 上書きされる（§C-2）。したがって「台風の位置に雲の渦を置く」合成は**原理的に
    /// できない**。④は自分でメッシュを組み、自分でマテリアルを作り、自分で描く。
    ///
    /// スカイドームは無限遠に描かれるので、④の雲は**必ずその手前に出る。深度の破綻は
    /// 起きない**（§C-2）。
    ///
    /// ── 罠 1: CS のマテリアルを借りない（③が確定させた） ────────────────
    ///
    /// CS のシェーダはエンジンが供給する per-instance データを要求する
    /// （<c>VortexAI.RenderExtraStuff</c> は <c>VehicleManager.m_materialBlock</c> に
    /// <c>ID_TyreMatrix</c> / <c>ID_TyrePosition</c> / <c>ID_LightState</c> / <c>ID_Color</c> を
    /// 詰めてから <c>DrawMesh</c> している。§C-1）。それを自前の <c>DrawMesh</c> に載せると
    /// **何も描画されないか真っ黒になる**。だから借りるとしてもメッシュだけで、
    /// マテリアルは <c>Shader.Find</c> から自作する（火災旋風 §4.9、
    /// ③の <c>FireWhirlFlameFx.FlameMaterial</c> と同じ形）。
    ///
    /// **シェーダが 1 つも解決しなければ何も描かない**（<see cref="TyphoonCloudState.ShaderMissing"/>）。
    /// 将来のゲーム更新でそうなったときに黙って不可視にならないよう、
    /// <c>Assumptions</c> にも同じ検証を 1 件置いてある。
    ///
    /// ── 罠 2: 静的キャッシュを <c>Mesh[]</c> にしない（③が確定させた） ──────────
    ///
    /// <c>UnityEngine.Object</c> は <c>==</c> を多重定義していて破棄済みオブジェクトが
    /// null と等価になるが、**配列参照の比較にはそれが効かない**。<c>static Mesh[]</c> は
    /// 破棄済みメッシュを抱えたまま非 null であり続け、**2 つ目の都市で無言・無ログの
    /// まま見えなくなる**（火災旋風 §4.8）。ここは <c>Mesh</c> と <c>Material</c> を
    /// **1 個ずつの参照**で持ち、毎フレームその参照そのものを <c>== null</c> で見る。
    ///
    /// ── <c>Mesh</c> と <c>Material</c> は自分で <c>Object.Destroy</c> する ──────────
    ///
    /// どちらも <c>Component</c> ではないので、<c>GameObject</c> を消しても道連れに
    /// ならない（②が <c>WaveformView</c> の <c>Texture2D</c> で踏んだのと同じ形のリーク）。
    /// <see cref="Destroy"/> が両方を明示的に破棄する。
    ///
    /// ── この型は sim スレッドから 1 度も呼ばれない ────────────────────
    ///
    /// **それが T9（雲）を④の他の要素から独立させている実体である。**
    /// <c>TyphoonController.Forget</c> の後始末列にこの型を足してはいけない ——
    /// 足した瞬間に雲が台風本体の依存になり、切れなくなる。雲は
    /// <c>TyphoonFeature.OnMainThreadUpdate</c> が毎フレーム <see cref="Update"/> を呼び、
    /// **スナップショットが <c>Active == false</c> になったフレームで自分で後始末する**。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// <c>Graphics.DrawMesh</c> **1 回**（2304 頂点 / 4560 三角形）と
    /// <c>Matrix4x4.TRS</c> 1 個、バニラ雲の増強が有効なときは float 3 本の書き込みだけ。
    /// **ヒープ確保は 0 バイト**（<c>Matrix4x4</c> / <c>Quaternion</c> / <c>Vector3</c> は
    /// いずれも struct、メッシュとマテリアルは都市ごとに 1 回だけ作る）。
    /// </summary>
    public static class TyphoonCloud
    {
        /// <summary>ゆっくり回す（度／秒）。6 度/秒 ＝ 1 回転 60 秒。**④が決めた演出値。**</summary>
        private const float SpinDegreesPerSecond = 6f;

        /// <summary>雲の高度（m）。スカイドームは無限遠なので必ずその手前に出る（§C-2）。</summary>
        private const float CloudAltitudeMetres = 900f;

        /// <summary>山岳マップで山に埋まらないための、中心の地形高からの最低クリアランス（m）。</summary>
        private const float MinClearanceMetres = 300f;

        /// <summary>描画に使うレイヤー。0 ＝ Default はどのカメラのカリングマスクにも入る。</summary>
        private const int CloudLayer = 0;

        // メッシュは固定寸法で 1 回だけ組み、大きさは行列のスケールで変える。
        private const float MeshInnerRadius = 120f;
        private const float MeshOuterRadius = 1000f;
        private const float MeshHeightMetres = 140f;

        /// <summary>バニラ空の雲量の最大倍率（強度 255 のとき）。</summary>
        private const float BoostMaxCoverage = 1.4f;

        /// <summary>バニラ空の雲の流速の最大倍率。</summary>
        private const float BoostWindForce = 2.5f;

        /// <summary>バニラ空の雲の変形速度の最大倍率。</summary>
        private const float BoostEvolutionSpeed = 2f;

        /// <summary>バニラ空の雲の設定を探し直すまでに空けるフレーム数。
        /// <c>Resources.FindObjectsOfTypeAll</c> は確保と全走査を伴うので毎フレームは回さない。</summary>
        private const int BoostRetryFrames = 300;

        // ★ 配列にしない（罠 2）。参照 1 個ずつで持ち、fake-null の自己修復を効かせる。
        private static Mesh _mesh;
        private static Material _material;

        private static TyphoonCloudState _state = TyphoonCloudState.Off;
        private static float _spinDegrees;
        private static int _lastDrawCalls;
        private static float _lastRadius;

        private static DayNightDynamicCloudsProperties _clouds;
        private static int _boostMissCount;
        private static bool _boostApplied;
        private static float _originalMaxCoverage;
        private static float _originalWindForce;
        private static float _originalEvolutionSpeed;

        /// <summary>バニラ空の雲の設定がこの環境に無いことを 1 度だけ名乗ったか。</summary>
        private static bool _boostUnavailableLogged;

        /// <summary>シェーダが解決しないことを <c>Log.Warn</c> で 1 度だけ鳴らしたか。
        /// **<see cref="Destroy"/> で戻さない**（ゲームのビルドに対する事実であって
        /// 都市ごとの状態ではない）。</summary>
        private static bool _shaderWarned;

        private static bool _errorLogged;

        public static TyphoonCloudState State { get { return _state; } }

        /// <summary>バニラ空の雲を今増強しているか。</summary>
        public static bool VanillaBoostApplied { get { return _boostApplied; } }

        /// <summary>直近のフレームで出した <c>DrawMesh</c> の回数（0 か 1）。</summary>
        public static int LastDrawCalls { get { return _lastDrawCalls; } }

        /// <summary>直近に描いた雲の外周半径（m）。診断用。</summary>
        public static float LastRadiusMetres { get { return _lastRadius; } }

        /// <summary>
        /// **main スレッド、毎フレーム。**
        /// <paramref name="snapshot"/> が古くても構わない —— 1 フレーム前の中心に
        /// 雲があっても誰も気付かない。
        /// </summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                _state = TyphoonCloudState.BuildFailed;
                _lastDrawCalls = 0;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon cloud failed", e);
                }

                // 触ったものは返す。例外が出たフレームでバニラ空の設定を
                // 握ったままにしない。
                ReleaseVanillaBoost();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid || !snapshot.Active)
            {
                // ★ 台風が終わったフレームで**自分で**後始末する。sim 側の
                //   TyphoonController は雲を 1 度も呼ばない（クラス doc）。
                ReleaseVanillaBoost();
                _state = TyphoonCloudState.NotBuilt;
                _lastDrawCalls = 0;
                _lastRadius = 0f;
                return;
            }

            float radius = snapshot.GaleRadius;
            if (!(radius > 0f))
            {
                ReleaseVanillaBoost();
                _state = TyphoonCloudState.NotBuilt;
                _lastDrawCalls = 0;
                _lastRadius = 0f;
                return;
            }

            // ★ 罠 2: 参照そのものを毎フレーム見る。破棄済みなら Unity の fake-null で
            //   null と等価になり、ここで作り直される（2 つ目の都市の自己修復）。
            if (_mesh == null) _mesh = BuildMesh();
            if (_material == null) _material = BuildMaterial();
            if (_mesh == null || _material == null)
            {
                _lastDrawCalls = 0;
                return;
            }

            _spinDegrees += SpinDegreesPerSecond * Time.deltaTime;
            if (_spinDegrees >= 360f) _spinDegrees -= 360f;

            Vec3 centre = snapshot.Centre;
            float altitude = centre.Y + MinClearanceMetres;
            if (altitude < CloudAltitudeMetres) altitude = CloudAltitudeMetres;

            float scale = radius / MeshOuterRadius;

            // 以下は全て struct。**ヒープ確保は 0 バイト**（クラス doc）。
            var position = new Vector3(centre.X, altitude, centre.Z);
            var rotation = Quaternion.AngleAxis(_spinDegrees, Vector3.up);
            var matrix = Matrix4x4.TRS(position, rotation, new Vector3(scale, 1f, scale));

            Graphics.DrawMesh(_mesh, matrix, _material, CloudLayer);

            _state = TyphoonCloudState.Drawing;
            _lastDrawCalls = 1;
            _lastRadius = radius;

            ApplyVanillaBoost(snapshot.Intensity);
        }

        // ── メッシュとマテリアル ───────────────────────────────────

        /// <summary>
        /// <c>SpiralMesh</c>（Core の純データ）から <c>Mesh</c> を組む。
        /// 都市ごとに 1 回だけ走る。**毎フレームの経路ではない。**
        /// </summary>
        private static Mesh BuildMesh()
        {
            var source = new Vec3[SpiralMesh.VertexCount];
            var uvSource = new float[SpiralMesh.VertexCount * 2];
            var triangles = new int[SpiralMesh.TriangleIndexCount];
            SpiralMesh.Build(MeshInnerRadius, MeshOuterRadius, MeshHeightMetres,
                             source, uvSource, triangles);

            var vertices = new Vector3[source.Length];
            var uvs = new Vector2[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                vertices[i] = new Vector3(source[i].X, source[i].Y, source[i].Z);
                uvs[i] = new Vector2(uvSource[i * 2], uvSource[i * 2 + 1]);
            }

            var mesh = new Mesh();
            mesh.name = "DisasterPlus_TyphoonCloud";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// **CS のマテリアルを借りない**（罠 1）。半透明で描きたいので、まず粒子系の
        /// アルファブレンドを狙い、無ければ順に落とす。<c>Standard</c> まで落ちたときだけ
        /// 透過の設定を手で入れる —— 既定の <c>Standard</c> は不透明なので、
        /// **都市の上に不透明な灰色の円盤を置くことになる**。
        /// </summary>
        private static Material BuildMaterial()
        {
            Shader s = FindShader();
            if (s == null)
            {
                // ★ Log.Warn はスロットルされない。ここは**毎フレームの経路**
                //   （マテリアルが作れない限り毎フレーム再挑戦する）なので、
                //   1 回だけ鳴らして以後は黙る。
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    Log.Warn("typhoon cloud: no usable shader resolved; the cloud is not drawn "
                             + "(Disaster + does not borrow a Cities material - that renders "
                             + "invisible or black in a hand-rolled DrawMesh)");
                }
                _state = TyphoonCloudState.ShaderMissing;
                return null;
            }

            var m = new Material(s);
            m.name = "DisasterPlus_TyphoonCloud";

            // 嵐雲の色。粒子系シェーダのティントは _TintColor、Standard は _Color
            // （③が確定させた区別。FireWhirlFlameFx の doc）。**効かないほうを
            // 書いて満足しない**ので、実在するプロパティにだけ入れる。
            var tint = new Color(0.32f, 0.34f, 0.38f, 0.5f);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);

            if (s.name == "Standard") MakeStandardTransparent(m);

            m.renderQueue = 3000;   // Transparent
            return m;
        }

        /// <summary>
        /// 使えるシェーダを 1 つ返す。半透明で描きたいので粒子系のアルファブレンドを
        /// 優先し、無ければ順に落とす。判定は必ず <c>!= null</c>（<c>UnityEngine.Object</c> の
        /// 多重定義）で行う —— <c>??</c> は fake-null を素通しするので、破棄済みの
        /// <c>Shader</c> を掴んだときにフォールバックが働かない。
        /// </summary>
        private static Shader FindShader()
        {
            Shader s = Shader.Find("Particles/Alpha Blended");
            if (s != null) return s;

            s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (s != null) return s;

            s = Shader.Find("Particles/Additive");
            if (s != null) return s;

            s = Shader.Find("Standard");
            return s != null ? s : null;
        }

        /// <summary>
        /// <c>Standard</c> まで落ちたときだけ通る。既定の <c>Standard</c> は不透明なので、
        /// これを飛ばすと**都市の上に不透明な灰色の円盤**が乗る。
        /// Unity 5.6 の StandardShaderGUI が Transparent モードで入れるのと同じ設定。
        /// </summary>
        private static void MakeStandardTransparent(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        // ── バニラ空の雲の増強（無ければ黙って諦める） ─────────────────

        /// <summary>
        /// <c>DayNightDynamicCloudsProperties</c> の 3 値を上げ、空全体を重く・速くする。
        /// 上書きされないのはこの 3 つだけである（<c>m_Coverage</c> は毎フレーム
        /// <c>m_MaxCoverage × SampleCloudCoverage()</c> で潰されるので書いても無意味。§C-2）。
        ///
        /// **この型がこの環境に無いことは正当である**（DLC・グラフィック設定。PARTIAL）。
        /// 無ければ診断に 1 行出して諦める —— ④の自前の雲はこれに依存しない。
        /// <c>Assumptions</c> には入れない（FAIL にすると狼少年になる）。
        /// </summary>
        private static void ApplyVanillaBoost(byte intensity)
        {
            if (!ModSettings.TyphoonVanillaCloudBoost.value)
            {
                ReleaseVanillaBoost();
                return;
            }

            // ★ 参照 1 個。破棄済みなら fake-null で null と等価になり、探し直される。
            //
            // ★★ ただし**毎フレーム探しに行かない。** SceneObjects.FindInScene は
            //    Resources.FindObjectsOfTypeAll<T>() を呼び、**配列を確保して全オブジェクトを
            //    走査する**。この型が存在しない環境（正当。§C-2）では見つからないので、
            //    素直に書くと台風の間ずっと毎フレーム確保と全走査が走る。
            //    TyphoonReader のプレハブ走査と同じ間引きを掛ける。
            if (_clouds == null)
            {
                if (_boostMissCount > 0)
                {
                    _boostMissCount--;
                    _boostApplied = false;
                    return;
                }
                _clouds = SceneObjects.FindInScene<DayNightDynamicCloudsProperties>();
                if (_clouds == null) _boostMissCount = BoostRetryFrames;
            }

            if (_clouds == null)
            {
                if (!_boostUnavailableLogged)
                {
                    _boostUnavailableLogged = true;
                    Log.Info("typhoon cloud: DayNightDynamicCloudsProperties is not present in "
                             + "this environment; only Disaster +'s own cloud is drawn "
                             + "(this is normal on some DLC/graphics settings)");
                }
                _boostApplied = false;
                return;
            }

            if (!_boostApplied)
            {
                // ★ 元の値は**最初の 1 回だけ**控える。毎フレーム読み直すと、
                //   自分が書いた値を「元の値」として覚え直して指数的に増える。
                _originalMaxCoverage = _clouds.m_MaxCoverage;
                _originalWindForce = _clouds.m_WindForce;
                _originalEvolutionSpeed = _clouds.m_EvolutionSpeed;
                _boostApplied = true;
            }

            // 台風が近づく（強くなる）ほど重く・速くする。強度 0 で等倍。
            float t = intensity / 255f;
            float coverage = _originalMaxCoverage * (1f + (BoostMaxCoverage - 1f) * t);
            if (coverage > 1f) coverage = 1f;

            _clouds.m_MaxCoverage = coverage;
            _clouds.m_WindForce = _originalWindForce * (1f + (BoostWindForce - 1f) * t);
            _clouds.m_EvolutionSpeed = _originalEvolutionSpeed * (1f + (BoostEvolutionSpeed - 1f) * t);
        }

        /// <summary>
        /// バニラ空の雲の設定を元へ戻す。**冪等。** 台風が終わったフレーム・
        /// 設定を切ったフレーム・<see cref="Destroy"/> から呼ばれる。
        /// </summary>
        public static void ReleaseVanillaBoost()
        {
            if (!_boostApplied) return;
            _boostApplied = false;

            if (_clouds == null) return;   // 都市ごと消えた。戻す先が無い

            _clouds.m_MaxCoverage = _originalMaxCoverage;
            _clouds.m_WindForce = _originalWindForce;
            _clouds.m_EvolutionSpeed = _originalEvolutionSpeed;
        }

        /// <summary>
        /// **レベルアンロードと、設定で雲を切ったときに呼ぶ。** main スレッド専用。
        ///
        /// <c>Mesh</c> も <c>Material</c> も <c>Component</c> ではないので、
        /// <c>GameObject</c> を消しても道連れにならない。**自分で <c>Object.Destroy</c> する**
        /// （クラス doc）。冪等である。
        /// </summary>
        public static void Destroy()
        {
            ReleaseVanillaBoost();

            if (_mesh != null) Object.Destroy(_mesh);
            _mesh = null;

            if (_material != null) Object.Destroy(_material);
            _material = null;

            _clouds = null;
            _boostMissCount = 0;
            _state = TyphoonCloudState.Off;
            _spinDegrees = 0f;
            _lastDrawCalls = 0;
            _lastRadius = 0f;
            _boostUnavailableLogged = false;
            // ★ _errorLogged は戻さない（ゲームのビルドに対する事実であって
            //    都市ごとの状態ではない。④の他の型と同じ判断）。
        }
    }
}
