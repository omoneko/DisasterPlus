using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 噴煙を<b>雲の塊の群れ</b>として描く。**main スレッド専用**（Unity のオブジェクト）。
    ///
    /// ── 所有者の依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 噴煙のアニメーションもまだまだリアルではありません。幾何的なものではなく
    /// &gt; もっと自然的なカオスな煙のアニメーションを作ってほしいです。煙のエフェクトに
    /// &gt; 加えて核キノコ雲（MissileDisaster）のエフェクトも一部利用してリアルにして
    /// &gt; ください。
    ///
    /// ── ★★ MissileDisaster から借りたのは「描き方」である ────────────────
    ///
    /// キノコ雲の <c>MushroomCloudPuffsFx</c> と同じ手を使う ——
    /// <b>放出を止めた <c>ParticleSystem</c> を「描画係」としてだけ使い、
    /// 粒は毎フレーム <c>SetParticles</c> でこちらが置く</b>。
    /// ④の <c>TyphoonVortexPuffFx</c> で一度通した道である。
    ///
    /// これが要るのは、ゲームの粒子プレハブでは<b>粒 1 個の大きさを変えられない</b>
    /// からである（<c>SpawnArea</c> の半径を広げても粒は大きくならず、
    /// 同じ大きさの粒が薄く散るだけ。<c>BlastCluster</c> のクラス doc に同じ話がある）。
    /// 自分で置けば、塊を何百 m の大きさで描ける。
    ///
    /// ★★ <b>形は借りない。</b> 位置は <see cref="PlumeParcels"/> ——
    ///   核の単発の泡ではなく、供給が続く火山の噴煙柱である。
    ///
    /// ── これは灰の柱を「置き換えない」──────────────────────────────
    ///
    /// <c>VolcanoEruptionFx</c> のゲーム粒子（<see cref="EruptionColumn"/> の 9 段）は
    /// <b>そのまま残す</b>。所有者の依頼が「煙のエフェクトに<b>加えて</b>」だからであり、
    /// 実際にも役割が違う —— あちらは細かい灰の霞、こちらは大きな塊である。
    /// 2 つの表現が同じ太さを名乗るよう、<see cref="PlumeParcels.ColumnRadiusAt"/> は
    /// <see cref="EruptionColumn"/> の定数をそのまま使っている。
    ///
    /// ── 落ちたら畳む ───────────────────────────────────────
    ///
    /// <see cref="Update"/> が false を返したら**何も描いていない**。
    /// 呼び出し側はゲーム粒子だけの絵へ退避すればよい（それは今までの絵である）。
    /// </summary>
    public static class VolcanoPlumePuffFx
    {
        /// <summary>
        /// 噴出口の近くの色。**灰は黒い。** 真っ白にすると水蒸気に見える。
        /// </summary>
        private static readonly Color32 AshColor = new Color32(74, 68, 64, 255);

        /// <summary>傘の色。日を受けた灰白。**真っ白にしない**（光って見える）。</summary>
        private static readonly Color32 SunlitColor = new Color32(226, 226, 230, 255);

        /// <summary>
        /// 画面に対する粒の大きさの上限（<c>ParticleSystemRenderer.maxParticleSize</c>）。
        /// 既定の 0.5 のままだと、**近寄ったとたんに噴煙が縮む**。
        /// </summary>
        private const float MaxScreenFraction = 6f;

        /// <summary>
        /// 塊の見かけの大きさに掛ける倍率。**1 より大きい** ——
        /// 塊どうしが重ならないと、煙ではなく点々に見える
        /// （<c>tools/PlumePreview</c> の cover が 3 割を切ったらここを疑う）。
        /// </summary>
        private const float PuffSizeGain = 1.15f;

        private static GameObject _object;
        private static ParticleSystem _system;
        private static ParticleSystem.Particle[] _buffer;
        private static bool _errorLogged;

        /// <summary>直近のフレームで置いた塊の数（診断用）。0 は「描いていない」。</summary>
        public static int PuffsPlaced { get; private set; }

        /// <summary>直近のフレームで描いたか。false なら呼び出し側が退避する。</summary>
        public static bool Drawing { get; private set; }

        /// <summary>直近の失敗（診断用）。**黙って描かないをやらない。**</summary>
        public static string LastFailure { get; private set; }

        /// <summary>
        /// **main スレッド、毎フレーム。**
        /// </summary>
        /// <param name="vent">噴出口のワールド座標（<c>VolcanoSnapshot.Vent</c>）。</param>
        /// <param name="ventRadiusMetres">火口の半径（m）。</param>
        /// <param name="columnHeightMetres">柱の高さ（m）。</param>
        /// <param name="intensityUnit">噴火の強さ <c>[0,1]</c>。薄さと数に効く。</param>
        /// <param name="timeSeconds">噴火が始まってからの秒数（**連続で増える値**）。</param>
        public static bool Update(Vec3 vent, float ventRadiusMetres, float columnHeightMetres,
                                  float intensityUnit, float timeSeconds,
                                  float windX, float windZ, uint seed)
        {
            Drawing = false;
            PuffsPlaced = 0;

            try
            {
                return Step(vent, ventRadiusMetres, columnHeightMetres, intensityUnit,
                            timeSeconds, windX, windZ, seed);
            }
            catch (Exception e)
            {
                LastFailure = "the plume puffs threw " + e.GetType().Name;

                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano plume puffs failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcPlume",
                             "plume puffs failed: " + e.GetType().Name);
                }

                // ★ 落ちたら畳む。**壊れた噴煙を出したままにしない。**
                Destroy();
                return false;
            }
        }

        private static bool Step(Vec3 vent, float ventRadiusMetres, float columnHeightMetres,
                                 float intensityUnit, float timeSeconds,
                                 float windX, float windZ, uint seed)
        {
            if (!(ventRadiusMetres > 0f) || !(columnHeightMetres > 0f))
            {
                LastFailure = "the vent radius or the column height is not usable";
                return false;
            }

            Material material = CloudParticleAssets.Cloud;
            if (material == null)
            {
                LastFailure = "no cloud material (" + (CloudParticleAssets.Detail ?? "no detail")
                              + ")";
                return false;
            }

            if (!EnsureSystem(material)) return false;

            float unit = Clamp01(intensityUnit);

            // ★★ **弱い噴火では塊の数そのものを減らす。** 薄くするだけだと、
            //    「弱い噴火」ではなく「同じ大きさの薄い噴煙」に見える。
            int count = (int)(PlumeParcels.Count * (0.35f + 0.65f * unit));
            if (count < 1) count = 1;
            if (count > PlumeParcels.Count) count = PlumeParcels.Count;

            for (int i = 0; i < count; i++)
            {
                PlumeParcel p = PlumeParcels.At(i, timeSeconds, ventRadiusMetres,
                                                columnHeightMetres, windX, windZ, seed);

                _buffer[i].position = new Vector3(vent.X + p.X, vent.Y + p.Y, vent.Z + p.Z);

                // startSize は**直径**なので、半径を 2 倍する。
                _buffer[i].startSize = p.RadiusMetres * PuffSizeGain * 2f;
                _buffer[i].rotation = p.RotationDegrees;

                // ★ 強さは濃さにも効く。弱い噴火の噴煙は透ける。
                float alpha = p.Alpha * (0.45f + 0.55f * unit);
                _buffer[i].startColor = Blend(SunlitColor, AshColor, p.Brightness, alpha);

                // ★ 毎フレーム上限へ戻す。**シミュレーションに歳を取らせない**
                //   （クラス doc の「描画係としてだけ使う」の実体である）。
                _buffer[i].remainingLifetime = 1000f;
                _buffer[i].startLifetime = 1000f;
            }

            _system.SetParticles(_buffer, count);

            PuffsPlaced = count;
            Drawing = true;
            LastFailure = null;
            return true;
        }

        /// <summary>
        /// 描画係の <c>ParticleSystem</c> を用意する。**1 都市に 1 個。**
        /// </summary>
        private static bool EnsureSystem(Material material)
        {
            if (_object != null && _system != null && _buffer != null)
            {
                // ★★ **マテリアルは④と共有している**（<see cref="CloudParticleAssets"/>）。
                //    <c>TyphoonCloudFx.Destroy</c> があれを消すので、台風が終わったり
                //    設定で雲を切られたりすると、<b>噴火の最中に足元から素材が消える。</b>
                //    そのとき <c>renderer.material</c> は Unity の fake-null になり、
                //    噴煙は無言で見えなくなる —— GameObject は生きているので、
                //    ここで見ないと誰も気づけない。
                var live = _system.GetComponent<ParticleSystemRenderer>();
                if (live != null && live.sharedMaterial == null)
                {
                    live.material = material;
                }
                return true;
            }

            Destroy();

            var go = new GameObject("DisasterPlus_VolcanoPlumePuffs");
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            // ★ 位置は**世界座標のメートル**で入れる。
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = PlumeParcels.Count;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;

            // ★★ **ゲームには 1 粒も生ませない。** 置くのはこちらである。
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // ★ 大きく・柔らかく・重なる塊なので、奥行き順に並べないと縁が汚れる。
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = MaxScreenFraction;
            renderer.material = material;

            ps.Play();

            _object = go;
            _system = ps;
            _buffer = new ParticleSystem.Particle[PlumeParcels.Count];
            return true;
        }

        /// <summary>
        /// **レベルアンロードと、噴火が終わったときに呼ぶ。** 冪等。
        ///
        /// <c>GameObject</c> はこちらのものなので消す。<c>Material</c> と
        /// <c>Texture2D</c> は <see cref="CloudParticleAssets"/> のものなので**触らない**。
        /// </summary>
        public static void Destroy()
        {
            if (_object != null) UnityEngine.Object.Destroy(_object);

            _object = null;
            _system = null;
            _buffer = null;
            Drawing = false;
            PuffsPlaced = 0;
        }

        private static Color32 Blend(Color32 sunlit, Color32 ash, float brightness, float alpha)
        {
            float t = Clamp01(brightness);
            float a = Clamp01(alpha) * 255f;

            return new Color32(
                (byte)(ash.r + (sunlit.r - ash.r) * t),
                (byte)(ash.g + (sunlit.g - ash.g) * t),
                (byte)(ash.b + (sunlit.b - ash.b) * t),
                (byte)a);
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
