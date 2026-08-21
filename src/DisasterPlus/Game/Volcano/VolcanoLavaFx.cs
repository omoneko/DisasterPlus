using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 溶岩の面を描くために何が取れたか（<c>Shader.Find</c> か、
    /// 読み込み済み <c>Material</c> からの<b>シェーダの</b>借用か）。
    ///
    /// ★ <b><c>Shader.Find("Standard")</c> を「解決した」の判定に混ぜない。</b>
    /// <c>Standard</c> は Unity の組み込みで**必ず非 null** なので、
    /// <c>… || Shader.Find("Standard") != null</c> という検査は**構造上 1 度も
    /// 失敗できない**（④のレビューがまさにこれを見つけている）。
    /// ここは 2 つの事実を分けて持つ:
    ///
    /// <code>
    /// ResolvedShaderName      実際に使うシェーダの名前（null なら 1 つも取れなかった）
    /// ParticleShaderResolved  粒子系（加算 / アルファブレンド）が取れたか  ← 検査はこちら
    /// </code>
    /// </summary>
    public struct VolcanoLavaShaderFacts
    {
        /// <summary>実際に使うシェーダの名前。**null なら何も描かない。**</summary>
        public readonly string ResolvedShaderName;

        /// <summary>
        /// 粒子系のシェーダ（加算またはアルファブレンド）が取れたか。
        /// **これが false でも <c>Standard</c> を透過モードにして描く**が、
        /// 光っては見えない。<c>Assumptions</c> の述語はこちらである。
        /// </summary>
        public readonly bool ParticleShaderResolved;

        /// <summary>診断に出す 1 行（**英語**）。取れていなければ null。</summary>
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
    /// 溶岩の光る面。**main スレッド専用。**
    ///
    /// ── なぜゼロから作るのか ─────────────────────────────────
    ///
    /// 溶岩・マグマ・溶融物のプレハブもマテリアルもシェーダも、**DLL の文字列ヒープに
    /// 1 件も無い**（§B-5）。借りられる既製品が存在しないので、形（<c>LavaRibbon</c>）も
    /// 面（マテリアルとテクスチャ）も⑤が作る。
    ///
    /// ── 罠 1: CS のマテリアルを借りない（③が確定させた）────────────────
    ///
    /// CS のシェーダはエンジンが供給する per-instance データを要求する
    /// （<c>VortexAI.RenderExtraStuff</c> は <c>m_materialBlock</c> に詰めてから
    /// <c>DrawMesh</c> している）。それを自前の <c>DrawMesh</c> に載せると
    /// **何も描画されないか真っ黒になる**（火災旋風 §4.9）。
    /// マテリアルは自作する。**そのシェーダは <see cref="ShaderPool"/> が取ってくる**
    /// —— 借りるのは<b>シェーダだけ</b>で、<c>Material</c> インスタンスは借りない
    /// （2 つの違いはあちらのクラス doc）。
    ///
    /// **どのシェーダで解決したかは診断に出す**（<see cref="ShaderDetail"/>）——
    /// 将来のゲーム更新で黙って不可視になったときに、そう名乗れるようにするためである。
    ///
    /// > ★★ **かつてここには「このビルドで実際に解決する名前は <c>Particles/Additive</c>
    /// > である（§H-22）」と書いてあった。それは誤りだった**（本プロジェクトで 13 件目の
    /// > 誤った「確認済み」）。根拠は出荷アセットのバイト走査で
    /// > <c>globalgamemanagers</c> に文字列が在ったことだったが、
    /// > **アセットに名前が在ることと <c>Shader.Find</c> が解決することは別である**。
    /// > 実機では <c>Shader.Find</c> が**組み込みの <c>"Standard"</c> を含めて
    /// > 全ての名前に null を返した**。いま名前で引けないときは
    /// > 読み込み済み <c>Material</c> からシェーダを借りる（<see cref="ShaderPool"/>）。
    /// <c>Assumptions</c> にも 1 件置いてあるが、その述語は
    /// <b>「粒子系が取れたか」</b>であって「何か取れたか」ではない
    /// （<see cref="VolcanoLavaShaderFacts"/> のクラス doc）。
    ///
    /// ── 罠 2: 静的キャッシュを配列にしない（③が出荷した不具合）────────────
    ///
    /// <c>UnityEngine.Object</c> は <c>==</c> を多重定義していて破棄済みオブジェクトが
    /// null と等価になるが、**配列要素の参照にはそれが効かない**。
    /// <c>static Mesh[]</c> / <c>static Material[]</c> は破棄済みを抱えたまま非 null で
    /// あり続け、**2 つ目の都市で無言・無ログのまま見えなくなる**（火災旋風 §4.8）。
    /// ここは <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> を**1 個ずつの参照**で持ち、
    /// 毎フレームその参照そのものを <c>== null</c> で見る。
    ///
    /// **3 つとも <c>Component</c> ではない**ので <c>GameObject</c> の道連れにならない。
    /// <see cref="Destroy"/> が自分で <c>Object.Destroy</c> する。
    ///
    /// ── 毎フレームの費用 ─────────────────────────────────
    ///
    /// <c>Graphics.DrawMesh</c> **1 回**だけ（頂点は最大 8 本 × 128 点 × 2 = 2048、
    /// 三角形の添字は最大 6096。影は落とさず受けない）。
    /// **ヒープ確保は 0 バイト**（<c>Matrix4x4</c> は struct）。
    /// 色を書き直すのは冷え具合が <see cref="TintStep"/> 動いたときだけである。
    ///
    /// メッシュを組み直すのは <b>スナップショットの軌跡配列が差し替わったフレームだけ</b>
    /// である。<c>VolcanoLava</c> は前進した回にしか配列を作り直さないので、
    /// 参照が同じなら組み直す理由が無い（<c>ReferenceEquals</c> 1 回で判定できる）。
    /// 前進は 8 sim フレームぶんのゲーム内時間に 1 回までなので、
    /// **組み直しは毎秒 6 回を超えない。**
    ///
    /// ── ★★ 時間で動くものが 1 つも無い（2026-08-22、実機の指摘④）─────────────
    ///
    /// > 熔岩流の光り方が点滅しているのはリアルではありません。
    /// > 噴火が終わっても光り続けているのは修正してください。
    ///
    /// 以前はここが UV を毎秒 0.35 流していた。帯の全長に明暗の縞が 3 本しか
    /// 無いところへ流していたので、**地面のある 1 点は 1 秒弱で明 → 暗 → 明を
    /// 繰り返していた**。それが「点滅」である。いまは
    ///
    /// <list type="bullet">
    /// <item><b>輝きは場所の関数</b>（<c>Core/Volcano/LavaGlow</c>）—— 冷えた地殻と、
    ///   板と板のあいだの光る割れ目と、火口・前進端の熱い帯</item>
    /// <item><b>年齢で冷える</b> —— 溶岩が前へ進むと軌跡に点が増え、既に置かれた
    ///   場所の <c>v</c> が前進端の帯から外れて地殻の側へ入る。時間は参照しない</item>
    /// <item><b>冷え切ったら面ごと畳む</b>（<c>LavaGlow.Visible</c>）——
    ///   軌跡の配列は火山が終わっても残るので、ここで止めないと帯が地面に残り続ける</item>
    /// </list>
    ///
    /// したがって<b>ポーズ中に動くものはもう 1 つも無い</b>（④のレビューが挙げた
    /// 「止まった都市の上で雲だけが回る」欠陥は、構造的に起こしようがなくなった）。
    ///
    /// ── この型は sim スレッドから 1 度も呼ばれない ────────────────────
    ///
    /// **それが T9 を⑤の他の要素から独立させている実体である。**
    /// 読むのは <c>VolcanoHub.Latest</c> の不変配列だけで、
    /// <c>VolcanoLava</c> の内部配列には触らない。
    ///
    /// **レビューの grep（実際に走らせて件数を合わせてある）**:
    /// <code>
    /// grep -rl "VolcanoLavaFx" src/DisasterPlus --include=*.cs
    /// # -> ちょうど 4 ファイル:
    /// #      Game/Volcano/VolcanoLavaFx.cs        （この file）
    /// #      Game/Volcano/VolcanoFeature.cs       （OnMainThreadUpdate / OnLevelUnloading /
    /// #                                            WriteDiagnostics の 3 箇所だけ）
    /// #      Game/Volcano/VolcanoEffectRows.cs    （描画中の点数と、材料が無いときの注記）
    /// #      Game/Diagnostics/Assumptions.Volcano.cs（シェーダの前提 1 件）
    ///
    /// grep -l "VolcanoLavaFx" src/DisasterPlus/Game/Volcano/VolcanoState.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoSurvey.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoClearing.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoUplift.cs     ///                         src/DisasterPlus/Game/Volcano/VolcanoLava.cs
    /// # -> 1 件も出ない（sim スレッド側から呼ばれていない証拠）
    /// </code>
    /// </summary>
    public static class VolcanoLavaFx
    {
        /// <summary>地面からどれだけ浮かせるか（m）。地形の量子 1/64 m よりずっと大きく取る。</summary>
        private const float HeightOffsetMetres = 1.5f;

        // ★★ かつてここに ScrollPerSecond（UV を毎秒 0.35 流す）が在った。
        //    **あれが「点滅」の正体である**（2026-08-22、実機の指摘④）。
        //    帯の全長に明暗の縞が 3 本しか無いところへ UV を流していたので、
        //    地面のある 1 点は 1 秒弱で明 → 暗 → 明を繰り返していた。
        //    **輝きは時間ではなく場所の関数にする**（Core/Volcano/LavaGlow）。
        //    戻さないこと。

        /// <summary>描画に使うレイヤー。0 ＝ Default はどのカメラのカリングマスクにも入る。</summary>
        private const int LavaLayer = 0;

        /// <summary>色を書き直す冷え具合の刻み（毎フレーム書かないため）。</summary>
        private const float TintStep = 0.02f;

        /// <summary>
        /// 自作テクスチャの 1 辺（帯を横切る方向 × 帯に沿う方向）。
        /// **Core が持っている**（tools/VolcanoPreview と同じ絵を焼くため。
        /// 32 → 128 に上げたのは、割れ目の線が 32 では階段になるからである）。
        /// </summary>
        private const int TextureSize = LavaGlow.TextureSize;

        /// <summary>シェーダを探し直すまでに空けるフレーム数（④の <c>TyphoonCloud</c> と同じ間引き）。</summary>
        private const int ShaderRetryFrames = 300;

        // ★ 配列にしない（罠 2）。参照 1 個ずつで持ち、fake-null の自己修復を効かせる。
        private static Mesh _mesh;
        private static Material _material;
        private static Texture2D _texture;

        /// <summary>直近に組んだ軌跡配列（**参照の同一性だけを見る**）。</summary>
        private static Vec2[] _builtPoints;

        /// <summary>
        /// <see cref="_builtPoints"/> からはメッシュを組めなかったか
        /// （点が 2 個に満たない流れしか無い等）。**毎フレームの組み直しを止めるため**に在る。
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

        /// <summary>今フレーム溶岩の面を描いたか。</summary>
        public static bool Drawing { get { return _drawCalls > 0; } }

        /// <summary>マテリアルを作れているか。</summary>
        public static bool MaterialResolved { get { return _material != null; } }

        /// <summary>
        /// 診断に出す 1 行（**英語**）。**T9 の見た目についての唯一の診断出力**である。
        /// <c>Assumptions</c> は同じ答えを <see cref="ScanShaderFacts"/> から引くので、
        /// ここに「粒子系か」を別の口として生やさない
        /// （同じ事実の口が 2 つあると、必ず片方が古くなる）。
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

        /// <summary>直近のフレームで出した <c>DrawMesh</c> の回数（0 か 1）。</summary>
        public static int DrawCalls { get { return _drawCalls; } }

        /// <summary>今描いている軌跡の点数（診断用）。</summary>
        public static int PointsDrawn { get { return _pointsDrawn; } }

        /// <summary>
        /// **main スレッド専用。** どのシェーダが取れるかを調べるだけの純粋な走査。
        /// <c>Assumptions</c> と <see cref="BuildMaterial"/> の両方がこれを使う。
        /// </summary>
        public static VolcanoLavaShaderFacts ScanShaderFacts()
        {
            try
            {
                // ★ 解決の順序と手段は ShaderPool に 1 か所だけ置いてある。
                //   ここで別の順序を書くと、検査が報告する名前と実際に使うシェーダが
                //   ずれる（④の Assumptions が同じ理由で同じ注記を持っている）。
                //   Standard は「粒子系が取れた」に数えない（クラス doc）。
                ShaderPick pick = ShaderPool.Resolve(ShaderPreference.Additive);
                return new VolcanoLavaShaderFacts(pick.Name, pick.Particle, pick.Describe());
            }
            catch
            {
                return new VolcanoLavaShaderFacts(null, false, "NONE (the scan threw)");
            }
        }

        /// <summary>**main スレッド、毎フレーム。**</summary>
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
                // 溶岩が消えたフレームで**自分で**後始末する。sim 側からは呼ばれない。
                Destroy();
                return;
            }

            // ★★ **冷え切ったら面ごと畳む**（2026-08-22、実機の指摘④
            //    「噴火が終わっても光り続けているのは修正してください」）。
            //    軌跡の配列は火山が終わっても残る（次の山まで捨てない）ので、
            //    ここで止めないと帯はいつまでも地面に在り続ける。
            //    冷える途中は CoolFade が 0 へ向かって暗くしていく。
            if (!LavaGlow.Visible(snapshot.LavaCoolUnit))
            {
                Destroy();
                return;
            }

            // ★ 参照が変わったときだけ組み直す（クラス doc の費用表）。
            //   VolcanoLava は前進した回にしか配列を作り直さない。
            //
            // ★ 2 つ目の条件は fake-null の自己修復である（メッシュだけが
            //   破棄されて参照は同じ、という状態から戻る）。**_buildFailed で
            //   守っている** —— 守らないと「組めなかった軌跡」を毎フレーム
            //   組み直そうとして、確保だけが毎フレーム走る。
            if (!ReferenceEquals(_builtPoints, snapshot.LavaTrailPoints)
                || (_mesh == null && !_buildFailed))
            {
                RebuildMesh(snapshot);
            }

            if (_material == null) _material = BuildMaterial();
            if (_mesh == null || _material == null) return;

            // ★★ UV は 1 mm も動かさない。**輝きは場所の関数である**（LavaGlow）。
            //    ゆっくり変わるのは「溶岩が前へ進んで、既に置かれた場所の v が
            //    前進端の帯から外れていく」ためで、時間を参照した結果ではない。
            ApplyTint(snapshot.LavaCoolUnit);

            // ★ 影を落とさない・受けない（④のレビューが同じ指摘をしている）。
            //   半透明で光る面が影のパスに入ると、都市に帯の影が落ちる。
            //   camera は null（＝全カメラ）—— 1 台に絞るとそこだけ溶岩が消える。
            Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, LavaLayer,
                              null,     // camera: 全カメラ
                              0,        // submeshIndex
                              null,     // MaterialPropertyBlock
                              false,    // castShadows
                              false);   // receiveShadows

            _drawCalls = 1;
        }

        /// <summary>
        /// 全流路のリボンを 1 枚のメッシュへ詰め合わせる。**流れごとに
        /// <c>LavaRibbon.Build</c> を呼ぶ** —— 別々の流れの点を 1 本の折れ線として
        /// 渡すと、流れの間を飛び回る帯になる（<c>LavaRibbon</c> のクラス doc）。
        ///
        /// 幅は軌跡そのものから測った累積距離で決める（<c>LavaPath.SpreadRadiusFor</c>）。
        /// 各流路の走行距離をスナップショットに増やさずに済むうえ、
        /// 間引かれた軌跡でも正しい値になる。
        ///
        /// 高さは <c>SampleDetailHeight</c>（読み取りなのでどちらのスレッドからでも安全。
        /// <c>TerrainHeightSampler</c> の doc）に <see cref="HeightOffsetMetres"/> を足す。
        /// </summary>
        private static void RebuildMesh(VolcanoSnapshot snapshot)
        {
            DestroyMesh();

            Vec2[] points = snapshot.LavaTrailPoints;
            int[] counts = snapshot.LavaTrailCounts;

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
                    AppendRibbon(points, cursor, c, vertices, uvs, triangles, ref vOut, ref tOut);
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

        /// <summary>1 本ぶんのリボンを詰め合わせ先へ足す。</summary>
        private static void AppendRibbon(Vec2[] points, int start, int count,
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
                // 幅は半径の 2 倍。
                widths[i] = LavaPath.SpreadRadiusFor(travelled) * 2f;
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
        /// 地面の高さ（m）。読み取りだけなので main スレッドから安全
        /// （<c>TerrainHeightSampler</c> のクラス doc）。**組み直しのときにしか呼ばない。**
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
        /// **CS のマテリアルは借りない**（罠 1）。<see cref="ShaderPool"/> が
        /// 選んだ<b>シェーダ</b>で自作し、<c>Standard</c> まで落ちたときだけ透過を入れる
        /// （既定の <c>Standard</c> は不透明なので、地面に不透明な灰色の帯が乗る）。
        ///
        /// ★ **解決したシェーダはここで掴んだ参照をそのまま使う。**
        ///   名前で引き直してはいけない —— 借りてきたシェーダは
        ///   <c>Shader.Find</c> では引けないからこそ借りたのであって、
        ///   名前で引き直すと必ず null になり、溶岩が永久に描かれなくなる。
        /// </summary>
        private static Material BuildMaterial()
        {
            // ★ 毎フレーム探しに行かない（④の TyphoonCloud と同じ間引き）。
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
                    // ★ Log.Warn はスロットルされない。ここは毎フレームの経路なので
                    //   1 回だけ鳴らして以後は黙る。
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

            // ★ 借りてきた別のシェーダには掛けない（_Mode / _SrcBlend は Standard の契約）。
            if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

            m.renderQueue = 3000;   // Transparent
            _tintedCool = -1f;
            return m;
        }

        /// <summary>
        /// 帯のテクスチャ 1 枚。<c>u</c> が帯を横切る方向、
        /// <c>v</c> が帯に沿う方向（<b>0 が火口、1 が前進端</b>）。
        /// **模様は <c>Core/Volcano/LavaGlow</c> が決める** —— 冷えた地殻と、
        /// 板と板のあいだの光る割れ目と、両端（火口と前進端）の熱い帯である。
        ///
        /// ★★ **UV は動かさない。** 以前はここに明暗の縞を焼いて毎フレーム
        ///   流していたが、それが指摘④の「点滅」だった（あちらのクラス doc）。
        ///
        /// 作れなければ割り当てないだけで、べた塗りになる。
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

                        // 熱いところは黄白、冷えたところは暗い赤褐色へ落ちる。
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
                // ★ どちらの向きも [0,1] を 1 枚で覆う。**繰り返さない** ——
                //   v = 0 は火口、v = 1 は前進端という意味を持つ軸である。
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                t.SetPixels32(pixels);
                t.Apply(false, false);
                return t;
            }
            catch
            {
                // 作れなくても溶岩は出る（模様が無くなるだけ）。ログは出さない。
                return null;
            }
        }

        /// <summary>[0,1] を 0-255 のバイトへ。</summary>
        private static byte Byte(float v)
        {
            int i = Mathf.RoundToInt(v * 255f);
            return (byte)(i < 0 ? 0 : (i > 255 ? 255 : i));
        }

        /// <summary>
        /// 冷え具合で色を落とす。**止まった溶岩が永久に光っていないこと**が要件で、
        /// <c>VolcanoLava.CoolUnit</c> が 1 → 0 に落ちるあいだに暗くなり、
        /// <b>0 でちょうど消える</b>（<c>LavaGlow.CoolFade</c>）。
        ///
        /// ★★ かつてここは <c>k = 0.15 + 0.85 × cool</c> /
        ///   <c>α = 0.55 + 0.45 × cool</c> だった。**冷え切っても k = 0.15 / α = 0.55 が
        ///   残る式**で、どれだけ待っても消えなかった（指摘④の後半そのもの）。
        ///
        /// **毎フレームは書かない**（<see cref="TintStep"/>）。
        /// </summary>
        private static void ApplyTint(float coolUnit)
        {
            float cool = coolUnit;
            if (float.IsNaN(cool)) cool = 0f;
            if (cool < 0f) cool = 0f;
            if (cool > 1f) cool = 1f;

            if (_tintedCool >= 0f && Mathf.Abs(cool - _tintedCool) < TintStep) return;
            _tintedCool = cool;

            // 冷えるほど暗い赤へ。**0 で完全に消える。**
            float k = LavaGlow.CoolFade(cool);
            var tint = new Color(k, k * 0.55f, k * 0.2f, k);

            // 粒子系のティントは _TintColor、Standard は _Color（③が確定させた区別）。
            // **効かないほうを書いて満足しない**ので、実在するプロパティにだけ入れる。
            if (_material.HasProperty("_TintColor")) _material.SetColor("_TintColor", tint);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", tint);
        }

        // ★ かつてここに SimulationIsPaused（UV のスクロールをポーズ中に止めるため）が
        //   在った。**動かすものが 1 つも無くなったので消した**（LavaGlow）。

        private static void DestroyMesh()
        {
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _mesh = null;
            _builtPoints = null;
            _buildFailed = false;
            _pointsDrawn = 0;
        }

        /// <summary>
        /// **レベルアンロードと、設定で溶岩の描画を切ったときに呼ぶ。** main スレッド専用。
        ///
        /// <c>Mesh</c> / <c>Material</c> / <c>Texture2D</c> はどれも <c>Component</c> では
        /// ないので、<c>GameObject</c> を消しても道連れにならない。
        /// **自分で <c>Object.Destroy</c> する。** 冪等。
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
            // ★ _shaderName / _shaderDetail は診断が「何で解決したか」を名乗るための
            //   事実なので、都市を出ても消さない。_shaderWarned / _errorLogged も
            //   同じ理由で戻さない（ゲームのビルドに対する事実であって
            //   都市ごとの状態ではない）。
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
