using System;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **自前の白い雲の素材**（テクスチャ 1 枚とマテリアル 1 個）。main スレッド専用。
    ///
    /// ── なぜ借り物では駄目だったのか（2026-08-22、所有者の指摘）────────────
    ///
    /// &gt; 台風の雲のエフェクトについて、まだ煙のようなものが見えるんですが、
    /// &gt; MissileDisaster のキノコ雲のエフェクトに使っている白い雲を
    /// &gt; 上空の方で渦上に表示させられますか？
    ///
    /// ④はバニラの粒子マテリアルを<b>実際に列挙して採点し</b>、いちばん雲らしいものを
    /// 借りていた。実機のログはそれが期待どおり働いたことを示している ——
    /// <c>resolved: Large Pool Steam</c>。**素材の選択は成功していた。**
    /// それでも煙に見えるのは、<b>湯気の絵が薄いから</b>である。
    ///
    /// ミサイル MOD が同じ壁を先に殴っており、その計測が残っている:
    ///
    /// <code>
    /// テクスチャ            雲の本体で測った不透明度
    /// soft-glow（湯気・煙）      0.34 - 0.41   ← 雲の向こうに空が見える
    /// 芯が不透明な雲             0.997         ← 雲に見える
    /// </code>
    ///
    /// > "The soft-glow texture is almost transparent everywhere but its centre -
    /// >  right for a wisp of dust, fatal for a cloud, which the background must not
    /// >  show through."
    /// >   —— <c>MissileDisaster/Game/Effects/ParticleAssets.cs</c>
    ///
    /// **だから借りるのをやめ、雲の絵を自分で作る。** 借りるのは<b>シェーダだけ</b>で
    /// （<see cref="ShaderPool"/>）、<c>Material</c> インスタンスは借りない ——
    /// 借りると自前の描画では不可視・無着色になる（⑤の <c>VolcanoLavaFx</c> の罠 1）。
    ///
    /// ── ★★ アルファブレンドでなければならない ────────────────────────
    ///
    /// 雲は<b>背景を隠す</b>ものである。加算合成のシェーダを掴むと、
    /// 明るいほど背景が透けるので、どんなテクスチャを入れても雲にならない。
    /// <see cref="ShaderPreference.AlphaBlended"/> を指定すること。
    /// 実機のログでは <c>Custom/Particles/Alpha Blended</c> が
    /// **読み込み済みマテリアルからの借用で**引けている（<c>Shader.Find</c> では引けない）。
    ///
    /// ── 都市をまたいで抱えない ───────────────────────────────
    ///
    /// <c>Material</c> も <c>Texture2D</c> も <c>Component</c> ではないので、
    /// <c>GameObject</c> を消しても道連れにならない。<see cref="Destroy"/> を
    /// レベルアンロードで必ず呼ぶこと。**参照 1 個ずつで持つ**（配列にすると
    /// 破棄済みを抱えたまま非 null になる）。
    /// </summary>
    public static class CloudParticleAssets
    {
        /// <summary>ここまでは**完全に不透明**（テクスチャの中心からの比）。</summary>
        public const float CoreEnd = 0.42f;

        /// <summary>ここで透明になりきる。</summary>
        public const float EdgeEnd = 0.95f;

        /// <summary>
        /// 縁を 3 回ぶん揺らす量。**真円の粒が並ぶと「泡の集まり」に見える** ——
        /// 縁を崩すと、重なった粒がひと続きの塊として読める。
        /// </summary>
        public const float RimWobble = 0.10f;

        /// <summary>テクスチャの一辺（px）。</summary>
        private const int TextureSize = 128;

        /// <summary>シェーダが引けなかったときに、次に試すまで待つ呼び出し回数。</summary>
        private const int RetryCalls = 300;

        private static Material _material;
        private static Texture2D _texture;
        private static int _missCount;
        private static bool _warned;
        private static string _detail;

        /// <summary>
        /// 白い雲のマテリアル。**引けなければ null**（呼び出し側は描かないこと）。
        /// 1 度作ったら都市を出るまで使い回す。
        /// </summary>
        public static Material Cloud
        {
            get
            {
                if (_material != null) return _material;

                if (_missCount > 0)
                {
                    _missCount--;
                    return null;
                }

                _material = Build();
                return _material;
            }
        }

        /// <summary>診断用の 1 行。**まだ試していないときは null。**</summary>
        public static string Detail { get { return _detail; } }

        /// <summary>**レベルアンロードで必ず呼ぶ。** 冪等。</summary>
        public static void Destroy()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);

            _material = null;
            _texture = null;
            _missCount = 0;
            _detail = null;
            // _warned は戻さない（ゲームのビルドに対する事実である）。
        }

        private static Material Build()
        {
            try
            {
                ShaderPick pick = ShaderPool.Resolve(ShaderPreference.AlphaBlended);
                if (!pick.Usable)
                {
                    _detail = "no alpha-blended particle shader; the white cloud is not drawn";
                    if (!_warned)
                    {
                        _warned = true;
                        Log.Warn("cloud particles: " + _detail);
                    }
                    _missCount = RetryCalls;
                    return null;
                }

                if (_texture == null) _texture = BuildTexture();

                var m = new Material(pick.Shader);
                m.name = "DisasterPlus_Cloud";

                if (_texture != null)
                {
                    m.mainTexture = _texture;
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", _texture);
                }

                // ★ <c>Custom/Particles/Alpha Blended</c> は <c>_TintColor</c> を**そのまま**
                //   掛ける（Unity 純正の粒子シェーダの「半灰色 0.5」の慣習ではない。
                //   ミサイル MOD が実機で確定させた区別）。白＝粒子自身の色をそのまま出す。
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                m.color = Color.white;

                if (pick.StandardFallback) ShaderPool.MakeStandardTransparent(m);

                _detail = "white cloud on " + pick.Describe();
                Log.Info("cloud particles: " + _detail);
                return m;
            }
            catch (Exception e)
            {
                _detail = "the cloud material could not be built (" + e.GetType().Name + ")";
                if (!_warned)
                {
                    _warned = true;
                    Log.Warn("cloud particles: " + _detail);
                }
                _missCount = RetryCalls;
                return null;
            }
        }

        /// <summary>
        /// 雲の粒 1 個の絵。**芯は完全に不透明**で、縁へ滑らかに消える。
        /// 縁は 3 回ぶん揺らしてあるので、重なった粒がひと続きの塊に見える
        /// （クラス doc の計測）。
        /// </summary>
        private static Texture2D BuildTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.ARGB32, false);
            tex.name = "DisasterPlus_CloudTex";
            tex.wrapMode = TextureWrapMode.Clamp;

            float half = (TextureSize - 1) * 0.5f;
            var pixels = new Color32[TextureSize * TextureSize];

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;

                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float angle = (float)Math.Atan2(dy, dx);
                    float wobble = 1f + RimWobble * (float)Math.Sin(3.0 * angle);
                    if (wobble < 0.0001f) wobble = 0.0001f;

                    float t = (d / wobble - CoreEnd) / (EdgeEnd - CoreEnd);
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;

                    // smoothstep。芯の中は 1、縁で 0。
                    float a = 1f - t * t * (3f - 2f * t);
                    pixels[y * TextureSize + x] =
                        new Color32(255, 255, 255, (byte)(255f * a));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            return tex;
        }
    }
}
