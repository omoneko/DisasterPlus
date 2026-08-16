using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦に炎をまとわせる。main スレッドからのみ呼ぶこと（Unity オブジェクトを作る）。
    ///
    /// CS のマテリアルは借りない。CS のシェーダはエンジンが供給する per-instance データを
    /// 要求するので、素の Renderer に載せると何も描画されないか真っ黒になる。
    /// シェーダは自前に解決して自分で <c>Material</c> を作る。
    ///
    /// ★★ <b>シェーダが 1 つも解決しなければ「描かない」。</b>実機テストで
    ///   <c>Shader.Find</c> が**組み込みの <c>"Standard"</c> を含めて全て null を返した**。
    ///   ここは以前 <c>?? Shader.Find("Standard")</c> で終わる 3 段の連鎖のあと
    ///   <c>new Material(s)</c> を無検査で呼んでいたので、
    ///   <c>Internal_CreateWithShader</c> が <c>NullReferenceException</c> を投げ、
    ///   それが毎フレームの経路だったため**同じ例外が 6,938 行**出た。
    ///   ④<c>TyphoonCloud</c> と⑤<c>VolcanoLavaFx</c> は最初から「解決しなければ描かない」
    ///   経路を持っていて綺麗に FAIL を報告できたので、③も同じ形に揃える。
    ///
    /// ★ <c>??</c> は使わない。<c>UnityEngine.Object</c> の <c>==</c> は多重定義されていて
    ///   破棄済みオブジェクトが null と等価になるが、<c>??</c> は**素の参照 null しか見ない**
    ///   ので fake-null を素通しする（④の <c>TyphoonCloud.FindShader</c> の同じ注記）。
    ///   判定は必ず <c>!= null</c> で行う。
    ///
    /// DispatchEffect を使わないのは、magnitude が粒子密度でしかなく、大きさは
    /// EffectInfo.SpawnArea 半径でしか変えられないため。旋風は 40〜220m とスケールが
    /// 大きく変わるので、自前の ParticleSystem の方が素直になる。
    ///
    /// Unity 5.6 の VelocityOverLifetimeModule には orbitalX/Y/Z が無い（2018 以降の追加）。
    /// 代わりに発生体の Transform 自体を毎フレーム回転させ、Local 空間の速度ベクトルを
    /// その回転で毎フレーム引き直させることで渦の芯を表現する（回転する発生体 + Local 空間の
    /// VelocityOverLifetime は 5.x 世代でよく使われた擬似オービタルの手法）。
    /// </summary>
    public static class FireWhirlFlameFx
    {
        private static readonly Dictionary<ushort, GameObject> _objects = new Dictionary<ushort, GameObject>();

        // ★ 配列にしない。参照 1 個で持ち、fake-null の自己修復を効かせる（§4.8）。
        private static Material _flameMaterial;

        // Sync() は毎フレーム呼ばれるので、ここで使う集合は使い回して確保する。
        // 中身は毎回 Clear() してから使うので、呼び出しをまたいだ値の持ち越しは無い。
        private static readonly HashSet<ushort> _aliveScratch = new HashSet<ushort>();
        private static readonly List<ushort> _staleScratch = new List<ushort>();

        /// <summary>渦の見た目上の回転速度（度/秒）。旋風の芯を表現するための演出値。</summary>
        private const float SpinDegreesPerSecond = 200f;

        /// <summary>
        /// シェーダを探し直すまでに空けるフレーム数（④の <c>TyphoonCloud</c> と同じ間引き）。
        /// マテリアルが作れない限り <see cref="FlameMaterial"/> は毎フレーム呼ばれるので、
        /// 素直に書くと探索がセッションのあいだ毎フレーム走る。
        /// </summary>
        private const int ShaderRetryFrames = 300;

        /// <summary>解決しなかったときに次に探すまでの残りフレーム数。</summary>
        private static int _shaderMissCount;

        /// <summary>実際に解決したシェーダの名前（**null なら 1 つも取れなかった**）。診断用。</summary>
        private static string _shaderName;

        /// <summary>
        /// 粒子系（加算 / アルファブレンド）が取れたか。**<c>Standard</c> はここに数えない** ——
        /// 数えた瞬間にこの旗は構造上 false になれなくなる（⑤ <c>VolcanoLavaFx</c> のクラス doc）。
        /// </summary>
        private static bool _particleShaderResolved;

        /// <summary>シェーダが解決しないことを <c>Log.Warn</c> で 1 度だけ鳴らしたか。
        /// **<see cref="Clear"/> で戻さない**（ゲームのビルドに対する事実であって
        /// 都市ごとの状態ではない。④⑤と同じ判断）。</summary>
        private static bool _shaderWarned;

        /// <summary>炎のマテリアルを作れているか（診断用）。</summary>
        public static bool MaterialResolved { get { return _flameMaterial != null; } }

        /// <summary>実際に使っているシェーダの名前（診断用）。作れていなければ null。</summary>
        public static string ShaderName { get { return _shaderName; } }

        /// <summary>粒子系のシェーダで解決したか（診断と <c>Assumptions</c> 用）。</summary>
        public static bool ParticleShaderResolved { get { return _particleShaderResolved; } }

        /// <summary>今エフェクトを出している渦の数（診断用）。</summary>
        public static int ObjectCount { get { return _objects.Count; } }

        /// <summary>レジストリの内容に合わせてエフェクトを生成・更新・破棄する。</summary>
        public static void Sync()
        {
            var views = FireWhirlRegistry.Snapshot();
            float dt = Time.deltaTime;

            // ★ 解決は「作る必要が出たとき」に**このフレームで 1 回だけ**行う。
            //   ループの中で毎回呼ぶと間引きカウンタが渦の数だけ減る。
            Material flame = null;
            bool flameAsked = false;

            _aliveScratch.Clear();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                _aliveScratch.Add(v.DisasterId);

                GameObject go;
                if (!_objects.TryGetValue(v.DisasterId, out go) || go == null)
                {
                    // go == null は Unity のフェイク null（都市を跨いで破棄済み）にも当たる。
                    if (!flameAsked)
                    {
                        flameAsked = true;
                        flame = FlameMaterial();
                    }

                    // ★★ ここが「描かない」経路である。**null のまま Create へ進めない。**
                    //   進めると new Material(null) が NullReferenceException を投げ、
                    //   毎フレームの経路なのでログが埋まる（クラス doc）。
                    //   旋風そのもの（固定・延焼・寿命）はこれが無くても動く。
                    if (flame == null) continue;

                    go = Create(v.DisasterId, flame);
                    _objects[v.DisasterId] = go;
                }

                go.transform.position = new Vector3(v.Center.X, v.Center.Y, v.Center.Z);
                // 渦を巻いて見せる回転。Configure の VelocityOverLifetime(Local) と組み合わさって
                // 接線方向の速度ベクトルが毎フレーム引き直され、渦の芯のように見える。
                go.transform.Rotate(Vector3.up, SpinDegreesPerSecond * dt, Space.World);
                Configure(go, v.Radius);
            }

            // 消えた旋風のエフェクトを片付ける。
            _staleScratch.Clear();
            foreach (var kv in _objects)
            {
                if (!_aliveScratch.Contains(kv.Key)) _staleScratch.Add(kv.Key);
            }
            for (int i = 0; i < _staleScratch.Count; i++)
            {
                if (_objects[_staleScratch[i]] != null) Object.Destroy(_objects[_staleScratch[i]]);
                _objects.Remove(_staleScratch[i]);
            }
        }

        /// <summary>
        /// 炎のマテリアル。**解決できなければ null を返す**（＝描かない）。
        /// 呼び出し側は必ず null を見ること。
        /// </summary>
        private static Material FlameMaterial()
        {
            if (_flameMaterial != null) return _flameMaterial;

            // ★ 毎フレーム探しに行かない（④の TyphoonCloud と同じ間引き）。
            if (_shaderMissCount > 0)
            {
                _shaderMissCount--;
                return null;
            }

            bool particle;
            Shader s = FindShader(out particle);
            _shaderName = s != null ? s.name : null;
            _particleShaderResolved = particle;

            if (s == null)
            {
                if (!_shaderWarned)
                {
                    _shaderWarned = true;
                    // ★ Log.Warn はスロットルされない。ここは毎フレームの経路なので
                    //   1 回だけ鳴らして以後は黙る（6,938 行を出した経路そのもの）。
                    Log.Warn("fire whirl: no usable shader resolved; the flames are not drawn "
                             + "(Disaster + does not borrow a Cities material - that renders "
                             + "invisible or black on a hand-rolled renderer). The fire whirl "
                             + "itself still spins, stays pinned and still spreads fire");
                }
                _shaderMissCount = ShaderRetryFrames;
                return null;
            }

            // Material.color は書かない。色は main.startColor（Create）が決めている。
            // Material.color が触るのは _Color だが Particles/Additive のティントは _TintColor なので、
            // ここで色を入れても何も起きない。「効いているように読める死んだ行」を残すと、
            // 後から誰かが _TintColor に直してしまい、理由なく見た目が変わる。
            var m = new Material(s);
            m.name = "DisasterPlus_FireWhirlFlame";

            // ★ 粒子系が 1 つも取れなかったときの受け皿。透過にしないと炎が
            //   **不透明な四角い板の群れ**になる（⑤ VolcanoEruption と同じ 7 行）。
            if (s.name == "Standard") MakeStandardTransparent(m);

            _flameMaterial = m;
            return _flameMaterial;
        }

        /// <summary>
        /// 使えるシェーダを 1 つ返す。炎なので加算合成を優先する。
        /// <paramref name="particleResolved"/> は**粒子系が取れたか**で、
        /// <c>Standard</c> では false。1 つも取れなければ null を返す。
        /// </summary>
        private static Shader FindShader(out bool particleResolved)
        {
            particleResolved = true;

            // ★ 判定は必ず != null（?? は fake-null を素通しする。クラス doc）。
            Shader s = Shader.Find("Particles/Additive");
            if (s != null) return s;

            s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s != null) return s;

            s = Shader.Find("Particles/Alpha Blended");
            if (s != null) return s;

            s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (s != null) return s;

            // ★ ここから下は「粒子系が 1 つも無かった」場合の受け皿である。
            particleResolved = false;

            s = Shader.Find("Standard");
            return s != null ? s : null;
        }

        /// <summary>
        /// <c>Standard</c> まで落ちたときだけ通る。既定の <c>Standard</c> は不透明なので、
        /// これを飛ばすと炎が不透明な板になる。Unity 5.6 の StandardShaderGUI が
        /// Transparent モードで入れるのと同じ設定（④⑤と同じ）。
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

        private static GameObject Create(ushort disasterId, Material flame)
        {
            var go = new GameObject("DisasterPlus_FireWhirl_" + disasterId);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.2f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.25f, 0.05f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 渦を巻きながら上昇させる。速度そのものは半径に応じて Configure が入れる。
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = flame;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return go;
        }

        /// <summary>半径に合わせて形と量を変える。旋風は 40〜220m まで大きさが変わる。</summary>
        private static void Configure(GameObject go, float radius)
        {
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) return;

            var main = ps.main;
            main.startSize = radius * 0.20f;
            main.startSpeed = radius * 0.25f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = radius * 0.35f;
            shape.angle = 12f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 40f + radius * 1.5f;

            // Local 空間の接線方向(x)＋上昇(y)速度。発生体自体は Sync() 側で毎フレーム
            // SpinDegreesPerSecond で回転しているので、この Local ベクトルは World 側では
            // 円を描くように向きを変え続け、渦に見える。
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(radius * 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(radius * 0.30f);
        }

        /// <summary>
        /// レベルアンロード時に必ず呼ぶ。
        /// 静的なコレクションに破棄済みの Unity オブジェクトを抱えたままにすると、
        /// 2 つ目の都市で無言のまま炎が出なくなる。
        /// </summary>
        public static void Clear()
        {
            foreach (var kv in _objects)
            {
                if (kv.Value != null) Object.Destroy(kv.Value);
            }
            _objects.Clear();

            if (_flameMaterial != null) Object.Destroy(_flameMaterial);
            _flameMaterial = null;

            _shaderMissCount = 0;
            // ★ _shaderName / _particleShaderResolved / _shaderWarned は戻さない。
            //   ゲームのビルドに対する事実であって都市ごとの状態ではない（④⑤と同じ）。
        }
    }
}
