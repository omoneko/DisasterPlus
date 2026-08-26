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
    /// ── ★★ これが噴煙の<b>唯一</b>の描き手になった（2026-08-22）────────────
    ///
    /// はじめはゲーム粒子の灰の柱（<see cref="EruptionColumn"/> の 9 段）と
    /// 重ねて出していた。所有者の指示でそれをやめた:
    ///
    /// &gt; 白色の噴煙のエフェクトが優れているので、既存の灰色の煙のエフェクトは
    /// &gt; オミットでお願いします。その代わり白色の噴煙に少し灰色も足してください。
    ///
    /// 重ねると、塊の隙間から細かい灰の粒が見えて<b>2 つの噴煙が重なっている</b>
    /// ように見えた。灰色ぶんはこちらの色（<c>AshColor</c> と <c>AshMixSpread</c>）へ移した。
    ///
    /// ★ <c>VolcanoEruptionFx.RenderColumn</c> と灰の複製は**消していない**。
    ///   柱の形（<see cref="EruptionColumn"/>）は噴煙の高さと雷の通り道に今も要るし、
    ///   <b>塊が 1 個も描けない環境</b>（マテリアルが引けない）では
    ///   そちらへ戻すのが唯一の逃げ道だからである。
    ///   2 つの表現が同じ太さを名乗るよう、<see cref="PlumeParcels.ColumnRadiusAt"/> は
    ///   <see cref="EruptionColumn"/> の定数をそのまま使っている。
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
        ///
        /// ★ 2026-08-22 に少し明るくした。ゲーム粒子の灰色の噴煙を止めたので
        ///   （所有者の指示）、<b>灰色ぶんはこちらが受け持つ</b>ことになった。
        ///   真っ黒のままだと、根元が煤の塊に見える。
        /// </summary>
        private static readonly Color32 AshColor = new Color32(96, 91, 88, 255);

        /// <summary>傘の色。日を受けた灰白。**真っ白にしない**（光って見える）。</summary>
        private static readonly Color32 SunlitColor = new Color32(226, 226, 230, 255);

        /// <summary>
        /// 塊ごとの灰色の混ざり方の幅。
        ///
        /// ── ★★ なぜ塊ごとに変えるのか（2026-08-22、所有者の指示）─────────────
        ///
        /// &gt; 白色の噴煙のエフェクトが優れているので、既存の灰色の煙のエフェクトは
        /// &gt; オミットでお願いします。その代わり白色の噴煙に少し灰色も足してください。
        ///
        /// 高さだけで白 → 灰を決めると、**同じ高さの塊が全部同じ色**になり、
        /// きれいな縞に見える（＝また「幾何的」に戻る）。塊ごとに ±この幅で
        /// 灰色寄りへずらすと、白い雲の中に灰の濃い塊が混じる、本物の噴煙の色になる。
        /// </summary>
        private const float AshMixSpread = 0.34f;

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

                // ★ 明るさは高さで決まる（下が灰、上が白）が、そこへ塊ごとの
                //   ばらつきを足す。足さないと同じ高さが全部同じ色になる。
                float mix = p.Brightness
                            - AshMixSpread * 0.5f
                            + AshMixSpread * DeterministicRandom.Unit(seed, (uint)i * 13u + 5u);

                _buffer[i].startColor = Blend(SunlitColor, AshColor, mix, alpha);

                // ★ 毎フレーム上限へ戻す。**シミュレーションに歳を取らせない**
                //   （クラス doc の「描画係としてだけ使う」の実体である）。
                _buffer[i].remainingLifetime = 1000f;
                _buffer[i].startLifetime = 1000f;
            }

            _system.SetParticles(_buffer, count);

            // ★★ **GameObject を粒のところへ動かす。**（2026-08-22、実機報告
            //    「雷が発生した瞬間消えてしまいます」）
            //
            //    粒はワールド座標で置いているが（simulationSpace = World）、
            //    <b>GameObject はずっと原点(0,0,0)に置きっぱなしだった</b>。
            //    Unity は <c>ParticleSystemRenderer</c> を<b>transform を基準にした
            //    境界</b>で視錐台カリングするので、**原点が画面から外れた瞬間に
            //    システムごと消える**。
            //
            //    ④で見つかった（あちらは雷でカメラが寄ると消えていた）。⑤は
            //    火口を見ていることが多いので表に出にくいだけで、**同じ穴である**。
            //
            //    ★ 例外が出る経路ではないので try で包まない。
            _object.transform.position = new Vector3(vent.X, vent.Y, vent.Z);

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
