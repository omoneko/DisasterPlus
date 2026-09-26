using System;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **Our own artwork** for the tiles on the disaster panel (two <c>Texture2D</c>s).
    /// Main thread only.
    ///
    /// ── The request (2026-08-22) ─────────────────────────────────
    ///
    /// &gt; I'd like the volcano and typhoon tab icons to be illustrations.
    ///
    /// ── ★★ Do not write a single vanilla sprite name ────────────────────
    ///
    /// That is the promise in the <see cref="DisasterPanelBar"/> class doc. Sprite names are
    /// atlas data and cannot be read from the assembly, so guessing at a name can give you
    /// **an invisible tile**. And on top of that, ④ and ⑤ are <b>disasters that do not exist
    /// in vanilla</b>, so there is no artwork to guess at in the first place.
    ///
    /// The artwork lives in <see cref="DisasterIconArt"/> (Core, the per-pixel formulae), and
    /// this only bakes it into a <c>Texture2D</c>. **The discussion of the shapes is on the
    /// Core side.** It is assembled the same way as the siren mod's <c>WarningIcon</c>, and
    /// plugging it into a <c>UITextureSprite</c> shows it as-is.
    ///
    /// ── Baked once per city ───────────────────────────────
    ///
    /// Two 128×128 textures (128 KB). Always call <see cref="Destroy"/> on level unload —
    /// a <c>Texture2D</c> is not a <c>Component</c>, so destroying the <c>GameObject</c> does
    /// not take it along.
    ///
    /// ★ **Hold them as individual references.** Put them in an array and you end up holding
    ///   a destroyed one (fake-null) that still tests non-null, and the artwork silently
    ///   stops appearing in the second city (the shape ⑤ actually hit).
    /// </summary>
    public static class DisasterTileIcons
    {
        /// <summary>Edge length (px). The tile is 109×100, so this is enough.</summary>
        private const int Size = 128;

        /// <summary>Which artwork to bake. **A two-valued bool leaves no room for a third.**</summary>
        private enum IconKind { Volcano, Typhoon, TrenchQuake }

        private static Texture2D _volcano;
        private static Texture2D _typhoon;
        private static Texture2D _trenchQuake;
        private static bool _failed;

        /// <summary>The volcano artwork. **null if it cannot be built** (the caller keeps the text label).</summary>
        public static Texture2D Volcano
        {
            get
            {
                if (_volcano != null) return _volcano;
                if (_failed) return null;

                _volcano = Build(IconKind.Volcano, "DisasterPlus_IconVolcano");
                return _volcano;
            }
        }

        /// <summary>The typhoon artwork. As above.</summary>
        public static Texture2D Typhoon
        {
            get
            {
                if (_typhoon != null) return _typhoon;
                if (_failed) return null;

                _typhoon = Build(IconKind.Typhoon, "DisasterPlus_IconTyphoon");
                return _typhoon;
            }
        }

        /// <summary>The trench earthquake artwork. As above.</summary>
        public static Texture2D TrenchQuake
        {
            get
            {
                if (_trenchQuake != null) return _trenchQuake;
                if (_failed) return null;

                _trenchQuake = Build(IconKind.TrenchQuake, "DisasterPlus_IconTrenchQuake");
                return _trenchQuake;
            }
        }

        /// <summary>**Always call on level unload.** Idempotent.</summary>
        public static void Destroy()
        {
            if (_volcano != null) UnityEngine.Object.Destroy(_volcano);
            if (_typhoon != null) UnityEngine.Object.Destroy(_typhoon);
            if (_trenchQuake != null) UnityEngine.Object.Destroy(_trenchQuake);

            _volcano = null;
            _typhoon = null;
            _trenchQuake = null;
            // _failed is not reset (it is a fact about the game build).
        }

        private static Texture2D Build(IconKind kind, string name)
        {
            try
            {
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.name = name;
                tex.wrapMode = TextureWrapMode.Clamp;

                var pixels = new Color32[Size * Size];

                for (int y = 0; y < Size; y++)
                {
                    // ★ For v, **0 is the bottom** (the promise in
                    //   <see cref="DisasterIconArt"/>). Unity textures also have row 0 at the
                    //   bottom, so it can go straight in.
                    float v = (y + 0.5f) / Size;

                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size;

                        IconPixel p;
                        switch (kind)
                        {
                            case IconKind.Volcano: p = DisasterIconArt.Volcano(u, v); break;
                            case IconKind.Typhoon: p = DisasterIconArt.Typhoon(u, v); break;
                            default: p = DisasterIconArt.TrenchQuake(u, v); break;
                        }

                        pixels[y * Size + x] = new Color32(p.R, p.G, p.B, p.A);
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false);
                return tex;
            }
            catch (Exception e)
            {
                _failed = true;
                Log.Warn("disaster tile icons: the texture could not be built ("
                         + e.GetType().Name + "); the tiles keep their text label");
                return null;
            }
        }
    }
}
