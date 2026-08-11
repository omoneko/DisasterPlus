using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦に炎をまとわせる。main スレッドからのみ呼ぶこと（Unity オブジェクトを作る）。
    ///
    /// CS のマテリアルは借りない。CS のシェーダはエンジンが供給する per-instance データを
    /// 要求するので、素の Renderer に載せると何も描画されないか真っ黒になる。
    /// シェーダは Shader.Find で自前に作る。
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
        private static Material _flameMaterial;

        // Sync() は毎フレーム呼ばれるので、ここで使う集合は使い回して確保する。
        // 中身は毎回 Clear() してから使うので、呼び出しをまたいだ値の持ち越しは無い。
        private static readonly HashSet<ushort> _aliveScratch = new HashSet<ushort>();
        private static readonly List<ushort> _staleScratch = new List<ushort>();

        /// <summary>渦の見た目上の回転速度（度/秒）。旋風の芯を表現するための演出値。</summary>
        private const float SpinDegreesPerSecond = 200f;

        /// <summary>レジストリの内容に合わせてエフェクトを生成・更新・破棄する。</summary>
        public static void Sync()
        {
            var views = FireWhirlRegistry.Snapshot();
            float dt = Time.deltaTime;

            _aliveScratch.Clear();
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                _aliveScratch.Add(v.DisasterId);

                GameObject go;
                if (!_objects.TryGetValue(v.DisasterId, out go) || go == null)
                {
                    // go == null は Unity のフェイク null（都市を跨いで破棄済み）にも当たる。
                    go = Create(v.DisasterId);
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

        private static Material FlameMaterial()
        {
            if (_flameMaterial != null) return _flameMaterial;

            Shader s = Shader.Find("Particles/Additive")
                    ?? Shader.Find("Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Standard");

            // Material.color は書かない。色は main.startColor（Create）が決めている。
            // Material.color が触るのは _Color だが Particles/Additive のティントは _TintColor なので、
            // ここで色を入れても何も起きない。「効いているように読める死んだ行」を残すと、
            // 後から誰かが _TintColor に直してしまい、理由なく見た目が変わる。
            _flameMaterial = new Material(s);
            return _flameMaterial;
        }

        private static GameObject Create(ushort disasterId)
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
            renderer.material = FlameMaterial();
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
        }
    }
}
