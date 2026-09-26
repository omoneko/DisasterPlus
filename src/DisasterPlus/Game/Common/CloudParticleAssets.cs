using System;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **Our own white cloud assets** (one texture and one material). Main thread only.
    ///
    /// ── Why borrowed assets would not do (2026-08-22, the owner's remark) ────────────
    ///
    /// &gt; About the typhoon's cloud effect — I can still see something like smoke.
    /// &gt; Could you show the white cloud that MissileDisaster uses for its mushroom cloud,
    /// &gt; swirling up high instead?
    ///
    /// ④ <b>actually enumerated and scored</b> vanilla's particle materials and borrowed the
    /// most cloud-like one. The log from the game shows that worked as intended —
    /// <c>resolved: Large Pool Steam</c>. **Picking the asset was a success.**
    /// It still looks like smoke because <b>the steam image is thin</b>.
    ///
    /// The missile mod hit the same wall first, and its measurements survive:
    ///
    /// <code>
    /// texture                    opacity measured over the body of the cloud
    /// soft-glow (steam, smoke)        0.34 - 0.41   ← the sky shows through the cloud
    /// cloud with an opaque core       0.997         ← looks like a cloud
    /// </code>
    ///
    /// > "The soft-glow texture is almost transparent everywhere but its centre -
    /// >  right for a wisp of dust, fatal for a cloud, which the background must not
    /// >  show through."
    /// >   —— <c>MissileDisaster/Game/Effects/ParticleAssets.cs</c>
    ///
    /// **So stop borrowing and draw the cloud ourselves.** Borrow <b>the shader only</b>
    /// (<see cref="ShaderPool"/>), never the <c>Material</c> instance — borrow that and our
    /// own rendering comes out invisible and uncoloured (trap 1 of ⑤'s <c>VolcanoLavaFx</c>).
    ///
    /// ── ★★ It must be alpha-blended ────────────────────────
    ///
    /// A cloud is something that <b>hides the background</b>. Grab an additive shader and the
    /// brighter it is the more the background shows through, so no texture will ever make it
    /// a cloud. Specify <see cref="ShaderPreference.AlphaBlended"/>.
    /// In the game's log, <c>Custom/Particles/Alpha Blended</c> is found
    /// **by borrowing from an already-loaded material** (<c>Shader.Find</c> will not find it).
    ///
    /// ── Do not hold on across cities ───────────────────────────────
    ///
    /// Neither <c>Material</c> nor <c>Texture2D</c> is a <c>Component</c>, so destroying the
    /// <c>GameObject</c> does not take them with it. Always call <see cref="Destroy"/> on
    /// level unload. **Hold them as individual references** (put them in an array and you end
    /// up holding a destroyed object that still tests non-null).
    /// </summary>
    public static class CloudParticleAssets
    {
        /// <summary>**Fully opaque** up to here (as a fraction from the texture centre).</summary>
        public const float CoreEnd = 0.42f;

        /// <summary>Fully transparent by here.</summary>
        public const float EdgeEnd = 0.95f;

        /// <summary>
        /// How much the rim wobbles, three times around. **A row of perfectly round
        /// particles reads as "a cluster of bubbles"** — break the rim up and the overlapping
        /// particles read as one continuous mass.
        /// </summary>
        public const float RimWobble = 0.10f;

        /// <summary>Texture edge length (px).</summary>
        private const int TextureSize = 128;

        /// <summary>How many calls to wait before trying again when the shader could not be found.</summary>
        private const int RetryCalls = 300;

        private static Material _material;
        private static Texture2D _texture;
        private static int _missCount;
        private static bool _warned;
        private static string _detail;

        /// <summary>
        /// The white cloud material. **null if it could not be resolved** (callers must not
        /// draw). Once built, it is reused until the city is left.
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

        /// <summary>One line for diagnostics. **null when nothing has been tried yet.**</summary>
        public static string Detail { get { return _detail; } }

        /// <summary>**Always call on level unload.** Idempotent.</summary>
        public static void Destroy()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);

            _material = null;
            _texture = null;
            _missCount = 0;
            _detail = null;
            // _warned is not reset (it is a fact about the game build).
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

                // ★ <c>Custom/Particles/Alpha Blended</c> multiplies <c>_TintColor</c> in
                //   **as-is** (not Unity's stock particle-shader convention of "half grey,
                //   0.5" — a distinction the missile mod pinned down in the game itself).
                //   White = show the particle's own colour unchanged.
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
        /// The image of a single cloud particle. **The core is fully opaque** and fades
        /// smoothly to the rim. The rim wobbles three times around, so overlapping particles
        /// read as one continuous mass (see the measurements in the class doc).
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

                    // smoothstep. 1 inside the core, 0 at the rim.
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
