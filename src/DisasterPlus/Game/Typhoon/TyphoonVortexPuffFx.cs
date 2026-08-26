using System;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風の渦を、**自前の白い雲の粒**で組む。<b>main スレッド専用、毎フレーム。</b>
    ///
    /// ── 依頼（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 台風の雲のエフェクトについて、まだ煙のようなものが見えるんですが、
    /// &gt; MissileDisaster のキノコ雲のエフェクトに使っている白い雲を
    /// &gt; 上空の方で渦上に表示させられますか？
    ///
    /// ── ★★ 粒を「撒く」のをやめて「置く」──────────────────────────
    ///
    /// 旧実装（<see cref="TyphoonCloudFx"/>）は <c>ParticleEffect.RenderEffect</c> で
    /// バニラの粒子を**撒いていた**。撒いた粒はバニラの粒子シミュレーションのもので、
    /// 生まれた瞬間に速度をもらってあとは漂う ——
    /// **こちらは形を持ち続けられない**（渦は 1 フレームごとに置き直さなければ渦に見えない）。
    ///
    /// ここは <c>ParticleSystem</c> を<b>描画係としてだけ</b>使う:
    ///
    ///   - <c>emission.enabled = false</c>（ゲームは 1 粒も生まない）
    ///   - 粒は毎フレーム <c>SetParticles</c> で**こちらが置く**
    ///   - 寿命は毎フレーム上限へ戻す（シミュレーションに歳を取らせない）
    ///
    /// **ミサイル MOD の <c>MushroomCloudPuffsFx</c> と同じ手口である。**
    /// あちらのクラス doc がこの選択の理由をそのまま書いている ——
    /// 「A simulated particle gets a velocity at birth and drifts」。
    ///
    /// ── 位置は既にある ──────────────────────────────────
    ///
    /// 渦の形は <see cref="VortexPuffLayout"/>（Core、テスト付き）が既に持っている ——
    /// 3 本の腕・眼の壁・4 段の高さ・粒ごとの大きさと濃さ。旧実装はこれを
    /// 「どこに撒くか」に使っていた。**同じ表をそのまま「どこに置くか」に使う。**
    /// 形の議論はやり直さない。
    ///
    /// ── 何が変わったのか（絵として）──────────────────────────
    ///
    /// | | 旧（借り物を撒く） | 新（自前を置く） |
    /// |---|---|---|
    /// | 素材 | <c>Large Pool Steam</c>（不透明度 0.34-0.41） | 芯が不透明な白い雲（0.997） |
    /// | 形 | 撒いた直後から漂って崩れる | 毎フレーム置き直すので崩れない |
    /// | 回転 | 粒は回らない（撒く位置だけが回る） | 粒ごと回る |
    ///
    /// ── 引けない環境では何もしない ────────────────────────────
    ///
    /// マテリアルが作れなければ <see cref="Drawing"/> が false のままで、
    /// 呼び出し側が**借り物の粒子へ退避する**（<see cref="TyphoonCloudFx"/>）。
    /// 黙って空にはしない。
    /// </summary>
    public static class TyphoonVortexPuffFx
    {
        /// <summary>
        /// 粒の色。**真っ白にしない** —— 真っ白は光っているように見える。
        /// 入道雲の日向側のわずかに青い白である。
        /// </summary>
        private static readonly Color32 SunlitColor = new Color32(242, 244, 248, 255);

        /// <summary>
        /// 底面の色。雲は下から見ると暗い。上下で色を変えないと、
        /// **平らな円盤が空に貼り付いているように見える**。
        /// </summary>
        private static readonly Color32 ShadedColor = new Color32(150, 156, 170, 255);

        /// <summary>いちばん濃い粒の不透明度。芯が不透明なテクスチャなので 1 に近くてよい。</summary>
        private const float MaxAlpha = 0.95f;

        /// <summary>いちばん薄い粒の不透明度（外側の腕の先）。</summary>
        private const float MinAlpha = 0.42f;

        /// <summary>
        /// 粒 1 個の見かけの大きさに掛ける倍率（<c>CrowdPuff.SizeFraction</c> に対して）。
        ///
        /// ★ **1 より大きい。** 粒どうしが重ならないと、雲ではなく点々に見える
        ///   （<see cref="VortexPuffCrowd"/> のクラス doc の 0.41 がその状態）。
        ///   重なりの量は <c>tools/TyphoonPreview</c> の
        ///   「粒の面積 ÷ 渦の面積」で測ってある。
        /// </summary>
        private const float PuffSizeGain = 1.0f;

        /// <summary>
        /// 画面に対する粒の大きさの上限（<c>ParticleSystemRenderer.maxParticleSize</c>）。
        /// 既定の 0.5 のままだと、**近寄ったとたんに雲が縮む**。
        /// </summary>
        private const float MaxScreenFraction = 4f;

        private static GameObject _object;
        private static ParticleSystem _system;
        private static ParticleSystem.Particle[] _buffer;
        private static bool _errorLogged;

        /// <summary>
        /// この雲が生まれてからの秒数。**塊の一生を進めるのはこれである。**
        ///
        /// ★★ <b>ここが「一瞬だけ現れて消える」の直しどころだった。</b>
        ///   （2026-08-22、所有者の報告）以前は <c>VortexPuffCrowd</c> の
        ///   <b>添字だけで決まる静止した並び</b>を置いていたので、雲は 1 度置いたら
        ///   二度と変わらなかった。今は毎フレーム時計が進み、塊が
        ///   生まれ・流れ・消える（<c>Core.Typhoon.TyphoonCloudParcels</c>）。
        /// </summary>
        private static float _clockSeconds;

        /// <summary>
        /// この台風の種。<b>1 つの台風のあいだ変わらないこと</b>が要点である
        /// （<see cref="Step"/> の doc）。
        /// </summary>
        private static uint _seed;

        /// <summary>直近のフレームで置いた粒の数（診断用）。0 は「描いていない」。</summary>
        public static int PuffsPlaced { get; private set; }

        /// <summary>直近のフレームで描いたか。false なら呼び出し側が退避する。</summary>
        public static bool Drawing { get; private set; }

        /// <summary>
        /// **main スレッド、毎フレーム。**
        /// <paramref name="radiusMetres"/> は渦の半径、<paramref name="spinDegrees"/> は
        /// 渦が今どれだけ回っているか（<see cref="TyphoonCloudFx"/> と同じ値を渡すこと）。
        ///
        /// 戻り値が false なら**何も描いていない**ので、呼び出し側は退避すること。
        /// </summary>
        public static bool Update(TyphoonSnapshot snapshot, float radiusMetres,
                                  float spinDegrees, float altitudeMetres,
                                  float thicknessMetres)
        {
            Drawing = false;
            PuffsPlaced = 0;

            try
            {
                return Step(snapshot, radiusMetres, spinDegrees, altitudeMetres, thicknessMetres);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon vortex cloud failed", e);
                }
                else
                {
                    Log.Diag("typhoonCloud", "vortex cloud failed: " + e.GetType().Name);
                }

                // ★ 落ちたら畳む。**壊れた雲を出したままにしない**
                //   （呼び出し側が借り物へ退避する）。
                Destroy();
                return false;
            }
        }

        private static bool Step(TyphoonSnapshot snapshot, float radiusMetres, float spinDegrees,
                                 float altitudeMetres, float thicknessMetres)
        {
            if (snapshot == null || !(radiusMetres > 0f)) return false;

            Material material = CloudParticleAssets.Cloud;
            if (material == null) return false;

            if (!EnsureSystem(material)) return false;

            Vec3 centre = snapshot.Centre;
            float spin = spinDegrees * 0.0174532925f;

            // ★ 時計はここで進める。ポーズ中は spinDegrees が止まるので、
            //   同じ扱いにするため呼び出し側の刻みではなく Time.deltaTime を使う
            //   —— ただしポーズ判定は呼び出し側が済ませており、止まっている
            //   フレームでも Update そのものは呼ばれる。**止まった雲を出すより、
            //   ゆっくり動く雲のほうがましである**（噴煙も同じ扱い）。
            _clockSeconds += Time.deltaTime;

            // ★ 種は台風ごとに 1 度だけ決める。スロット番号が変わったら別の台風である。
            uint slotSeed = DeterministicRandom.Hash(snapshot.TyphoonId, 0x54595048u);
            if (slotSeed != _seed)
            {
                _seed = slotSeed;
                _clockSeconds = 0f;
            }

            // ★★ **種は動かしてはいけない。**（2026-08-22、実機報告
            //    「高速回転する台風雲が一瞬現れる」）
            //
            //    ここは中心の座標からハッシュを取っていた。**台風は動く**ので
            //    毎フレーム種が変わり、<b>900 個の塊が毎フレーム別の場所へ飛んだ</b>。
            //    渦が高速で回っているように見えたのはそれである
            //    （⑤の噴煙は動かないので、同じ書き方でも表に出なかった）。
            //
            //    掴んでいる災害スロットの番号を種にする。**1 つの台風のあいだ
            //    変わらず、次の台風では変わる**という、ちょうど要る性質がある。
            uint seed = _seed;

            for (int i = 0; i < TyphoonCloudParcels.Count; i++)
            {
                TyphoonParcel p = TyphoonCloudParcels.At(i, _clockSeconds, radiusMetres,
                                                         spin, seed);

                _buffer[i].position = new Vector3(
                    centre.X + p.X,
                    altitudeMetres + p.Y,
                    centre.Z + p.Z);

                // startSize は**直径**なので、半径を 2 倍する。
                _buffer[i].startSize = p.RadiusMetres * PuffSizeGain * 2f;
                _buffer[i].rotation = p.RotationDegrees;

                float alpha = MinAlpha + (MaxAlpha - MinAlpha) * Clamp01(p.Alpha);
                _buffer[i].startColor = Blend(SunlitColor, ShadedColor,
                                              1f - Clamp01(p.Brightness), alpha);

                // ★ 毎フレーム上限へ戻す。**シミュレーションに歳を取らせない**
                //   （クラス doc の「描画係としてだけ使う」の実体である）。
                _buffer[i].remainingLifetime = 1000f;
                _buffer[i].startLifetime = 1000f;
            }

            _system.SetParticles(_buffer, TyphoonCloudParcels.Count);

            PuffsPlaced = TyphoonCloudParcels.Count;
            Drawing = true;
            return true;
        }

        /// <summary>
        /// 描画係の <c>ParticleSystem</c> を用意する。**1 都市に 1 個。**
        /// 既にあってマテリアルも生きていれば何もしない。
        /// </summary>
        private static bool EnsureSystem(Material material)
        {
            if (_object != null && _system != null && _buffer != null)
            {
                // ★★ マテリアルは⑤（火山）と共有している（CloudParticleAssets）。
                //    あちらが消したときのために毎フレーム fake-null を見る。
                var live = _system.GetComponent<ParticleSystemRenderer>();
                if (live != null && live.sharedMaterial == null) live.material = material;
                return true;
            }

            Destroy();

            var go = new GameObject("DisasterPlus_TyphoonVortexCloud");
            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            // ★ 位置は**世界座標のメートル**で入れる。ローカルにすると、
            //   台風が動くたびに GameObject を動かす必要が出る。
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = TyphoonCloudParcels.Count;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;

            // ★★ **ゲームには 1 粒も生ませない。** 置くのはこちらである。
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // ★ 大きく・柔らかく・重なる粒なので、奥行き順に並べないと縁が汚れる。
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = MaxScreenFraction;
            renderer.material = material;

            ps.Play();

            _object = go;
            _system = ps;
            _buffer = new ParticleSystem.Particle[TyphoonCloudParcels.Count];
            return true;
        }

        /// <summary>
        /// **レベルアンロードと、台風が居なくなったときに呼ぶ。** 冪等。
        ///
        /// <c>GameObject</c> はこちらのものなので消す。<c>Material</c> と
        /// <c>Texture2D</c> は <see cref="CloudParticleAssets"/> のものなので**触らない**
        /// （あちらがレベルアンロードで自分で消す）。
        /// </summary>
        public static void Destroy()
        {
            if (_object != null) UnityEngine.Object.Destroy(_object);

            _object = null;
            _system = null;
            _buffer = null;
            _clockSeconds = 0f;
            _seed = 0u;
            Drawing = false;
            PuffsPlaced = 0;
        }

        private static Color32 Blend(Color32 top, Color32 bottom, float towardsBottom,
                                     float alpha)
        {
            float t = Clamp01(towardsBottom);
            return new Color32(
                (byte)(top.r + (bottom.r - top.r) * t),
                (byte)(top.g + (bottom.g - top.g) * t),
                (byte)(top.b + (bottom.b - top.b) * t),
                (byte)(255f * Clamp01(alpha)));
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
