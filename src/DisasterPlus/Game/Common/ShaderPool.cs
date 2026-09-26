using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>Which blend mode to aim for. If one is unavailable it falls back to the other.</summary>
    public enum ShaderPreference
    {
        /// <summary>Additive (flame, eruption plume, lava).</summary>
        Additive = 0,

        /// <summary>Alpha-blended (cloud).</summary>
        AlphaBlended = 1,
    }

    /// <summary>
    /// One result returned by <see cref="ShaderPool"/>. **If <c>Shader</c> is null, do not draw.**
    /// </summary>
    public struct ShaderPick
    {
        /// <summary>The shader to use. **If null, draw nothing.**</summary>
        public readonly UnityEngine.Shader Shader;

        /// <summary>That shader's name (for diagnostics). null if unavailable.</summary>
        public readonly string Name;

        /// <summary>
        /// Whether it resolved to something in the particle family (additive / alpha-blended /
        /// particle). **<c>Standard</c> does not count here** — count it and this flag becomes
        /// structurally incapable of being false (see ⑤'s <c>VolcanoLavaFx</c> class doc).
        /// </summary>
        public readonly bool Particle;

        /// <summary>
        /// Whether it was **borrowed from a loaded <c>Material</c>** rather than found with
        /// <c>Shader.Find</c>. What was borrowed is <b>the shader only</b>, never the
        /// <c>Material</c> itself (the distinction in the <see cref="ShaderPool"/> class doc).
        /// </summary>
        public readonly bool Borrowed;

        /// <summary>
        /// Whether it fell all the way back to <c>Standard</c>. **Only then** does the caller
        /// apply <see cref="ShaderPool.MakeStandardTransparent"/>
        /// (<c>Standard</c> is opaque by default, so without it you get an opaque slab).
        /// </summary>
        public readonly bool StandardFallback;

        public ShaderPick(UnityEngine.Shader shader, bool particle, bool borrowed,
                          bool standardFallback)
        {
            Shader = shader;
            Name = shader != null ? shader.name : null;
            Particle = particle;
            Borrowed = borrowed;
            StandardFallback = standardFallback;
        }

        /// <summary>Whether a material can be built. **A destroyed one (fake-null) is rejected here too.**</summary>
        public bool Usable { get { return Shader != null; } }

        /// <summary>The one-line description for diagnostics (**in English**).</summary>
        public string Describe()
        {
            if (Shader == null) return "NONE (no shader resolved)";

            string s = Name;
            if (Borrowed) s += "  (shader borrowed from a loaded material, not Shader.Find)";
            if (StandardFallback)
            {
                s += "  (fallback: no particle shader in this build; forced to transparent "
                     + "so it does not draw opaque quads)";
            }
            else if (!Particle)
            {
                s += "  (fallback: not a particle shader, so it blends but does not glow)";
            }
            return s;
        }
    }

    /// <summary>
    /// Returns one shader to put on our own <c>Material</c>. **Main thread only.**
    ///
    /// ── Why <c>Shader.Find</c> alone is not enough (the 13th mistaken "confirmed") ───
    ///
    /// In the game, <c>Shader.Find</c> **returned null for every name, including the built-in
    /// <c>"Standard"</c>**.
    /// Earlier investigation had scanned the shipped assets byte by byte and found the strings
    /// <c>Particles/Additive</c> and <c>Particles/Alpha Blended</c> inside
    /// <c>globalgamemanagers</c>, but <b>a name being present in an asset and
    /// <c>Shader.Find</c> resolving it are two different things</b> — Unity strips shaders it
    /// did not include in the build, and <c>Shader.Find</c> **only returns shaders that are
    /// actually loaded**.
    ///
    /// ── What is borrowed is "the shader", not "the material" ─────────────
    ///
    /// The two are easy to confuse, but they are entirely separate matters:
    ///
    /// <code>
    /// ✗ Borrow a CS Material and put it on our own MeshRenderer / DrawMesh
    ///     → nothing is drawn, or it comes out pitch black (③ appendix A, fire whirl §4.9).
    ///       CS's materials assume per-instance data supplied by the engine
    ///       (VehicleManager.m_materialBlock's ID_TyreMatrix and the like).
    ///
    /// ✓ Take only the Shader from a CS Material and build new Material(thatShader) ourselves
    ///     → the standard move when a name cannot be resolved in a shipped Unity game.
    ///       Not one value that assumes per-instance data is carried over.
    /// </code>
    ///
    /// **This type never once returns a borrowed <c>Material</c> instance.**
    /// It returns only the <c>Shader</c>; the caller builds its own <c>Material</c> and calls
    /// <c>Object.Destroy</c> on it itself.
    ///
    /// ── Cost (<c>Resources.FindObjectsOfTypeAll</c> is not cheap) ──────────────
    ///
    /// The sweep allocates an array and walks every object, and on top of that
    /// <c>Object.name</c> builds a string from the native side, so it **allocates one string
    /// per material**. That is why it runs **once per session**. Once resolved, the result is
    /// carried around and no further sweeping happens.
    /// Only when nothing at all could be resolved does it sweep again after
    /// <see cref="RetryFrames"/> (insurance in case a future build changes the loading timing.
    /// The same thinning as ④'s <c>ApplyVanillaBoost</c>, longer here because the sweep is
    /// heavier).
    /// **Calling it from a per-frame path does not trigger a sweep.**
    ///
    /// ── Holds no per-city state ────────────────────────────
    ///
    /// All this holds is <b>facts about the game build</b>; it holds neither city state nor a
    /// <c>Material</c>. So there is nothing to clear on level unload.
    /// If a shader were somehow destroyed, <see cref="ShaderPick.Usable"/> rejects the
    /// fake-null through <c>UnityEngine.Object</c>'s <c>==</c> overload and it is resolved
    /// again on the next call (**not putting them in an array is precisely what makes that
    /// self-repair work**. Fire whirl §4.8).
    ///
    /// ── Log the inventory ──────────────────────────────────
    ///
    /// **Nobody yet knows** which shaders actually exist in this environment.
    /// So that the next playtest can bring back an answer rather than a guess, the distinct
    /// shader names are written to <c>Log.Info</c> once at the point of the sweep (with a cap
    /// on how many).
    ///
    /// The first one runs during the level load — because <c>Assumptions</c>' checks for ③④⑤
    /// call in here. **That is deliberate**: the inventory ends up in the log even in a
    /// session where no disaster ever happened.
    /// </summary>
    public static class ShaderPool
    {
        /// <summary>How many frames to leave before sweeping again when nothing resolved.</summary>
        private const int RetryFrames = 1800;

        /// <summary>The cap on names written to the log (this is a diagnostic, not a dump).</summary>
        private const int InventoryLogMax = 60;

        /// <summary>How many names go on one line.</summary>
        private const int NamesPerLine = 6;

        /// <summary>The score meaning "do not borrow at any tier" (lower is better).</summary>
        private const int NoScore = int.MaxValue;

        /// <summary>The names passed to <c>Shader.Find</c> when aiming for additive. **The order is the priority.**</summary>
        private static readonly string[] AdditiveNames =
        {
            "Particles/Additive",
            "Legacy Shaders/Particles/Additive",
            "Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended",
        };

        /// <summary>The names when aiming for alpha-blended. **The order is the priority.**</summary>
        private static readonly string[] AlphaBlendedNames =
        {
            "Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Additive",
            "Legacy Shaders/Particles/Additive",
        };

        // ★ Not an array. Hold one reference per preference (a field of the struct) and test
        //   that reference itself with != null every time (the self-repair in the class doc).
        private static ShaderPick _additive;
        private static ShaderPick _alphaBlended;

        private static bool _scanned;
        private static int _nextScanFrame;
        private static bool _errorLogged;

        /// <summary>
        /// Returns one usable shader. **Main thread only** (it touches <c>Shader</c> /
        /// <c>Resources</c>). **Safe to call every frame** — once resolved it just returns the
        /// cache, and the sweep is thinned out.
        ///
        /// <b>Always check <see cref="ShaderPick.Usable"/> on the return value.</b>
        /// false means "nothing can be drawn in this environment", not an exception.
        /// </summary>
        public static ShaderPick Resolve(ShaderPreference preference)
        {
            try
            {
                EnsureResolved();
            }
            catch (Exception e)
            {
                // ★ Do not let this throw. Every caller is on a per-frame rendering path, and
                //   raising an exception fills the log frame by frame (the shape ③ hit at
                //   6,938 lines).
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("shader resolution failed; nothing is drawn", e);
                }
                return new ShaderPick(null, false, false, false);
            }

            return preference == ShaderPreference.AlphaBlended ? _alphaBlended : _additive;
        }

        /// <summary>
        /// Puts <c>Standard</c> into transparent mode. **Call it only when
        /// <see cref="ShaderPick.StandardFallback"/> is set.** These are the same settings
        /// Unity 5.6's StandardShaderGUI applies for Transparent mode.
        ///
        /// Never apply it to some other borrowed shader — <c>_Mode</c> and <c>_SrcBlend</c>
        /// are <c>Standard</c>'s contract, and on another shader they are meaningless or
        /// harmful.
        /// </summary>
        public static void MakeStandardTransparent(Material m)
        {
            if (m == null) return;

            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        // ── Resolution ───────────────────────────────────────────

        /// <summary>
        /// Resolves both preferences **together**, because that way the sweep only happens
        /// once. Returns immediately if both are already resolved (i.e. this is where the
        /// per-frame calls end).
        /// </summary>
        private static void EnsureResolved()
        {
            if (_additive.Usable && _alphaBlended.Usable) return;

            // ★ Thinning out the sweep. After one sweep, do not run another until RetryFrames
            //   have passed.
            if (_scanned && Time.frameCount < _nextScanFrame) return;
            _nextScanFrame = Time.frameCount + RetryFrames;

            Catalog catalog = ScanCatalog();

            // ★ The inventory is logged once per session (the answer for the next playtest).
            if (!_scanned)
            {
                _scanned = true;
                LogInventory(catalog);
            }

            if (!_additive.Usable) _additive = Choose(ShaderPreference.Additive, catalog);
            if (!_alphaBlended.Usable) _alphaBlended = Choose(ShaderPreference.AlphaBlended, catalog);
        }

        /// <summary>
        /// Decides one result. **This order is the specification**:
        /// <code>
        /// ① Shader.Find(a particle-family name)   cheap. In an environment where the name
        ///                                         resolves, it ends here
        /// ② borrow from a loaded Material         the standard move when a name cannot be
        ///                                         resolved in a shipped game
        /// ③ Shader.Find("Standard")               opaque. The caller drops it to transparent
        /// ④ "Standard" from the inventory         insurance for an environment where
        ///                                         Shader.Find itself is broken
        /// ⑤ null                                  **do not draw**
        /// </code>
        /// </summary>
        private static ShaderPick Choose(ShaderPreference preference, Catalog catalog)
        {
            string[] names = preference == ShaderPreference.AlphaBlended
                ? AlphaBlendedNames : AdditiveNames;

            // ① Always test with != null (?? lets a fake-null straight through).
            for (int i = 0; i < names.Length; i++)
            {
                UnityEngine.Shader s = UnityEngine.Shader.Find(names[i]);
                if (s != null) return new ShaderPick(s, true, false, false);
            }

            // ② Borrow the shader **only** (never the Material. See the class doc).
            UnityEngine.Shader borrowed = preference == ShaderPreference.AlphaBlended
                ? catalog.AlphaBlended : catalog.Additive;
            if (borrowed != null)
            {
                bool particle = preference == ShaderPreference.AlphaBlended
                    ? catalog.AlphaBlendedParticle : catalog.AdditiveParticle;
                return new ShaderPick(borrowed, particle, true, false);
            }

            // ③ The catch-all for an environment with no particle shader at all.
            UnityEngine.Shader standard = UnityEngine.Shader.Find("Standard");
            if (standard != null) return new ShaderPick(standard, false, false, true);

            // ④ An environment where Shader.Find returns null even for a built-in (observed
            //    in the game).
            if (catalog.Standard != null) return new ShaderPick(catalog.Standard, false, true, true);

            // ⑤ Nothing at all. **Do not draw** (do not paper over it with a borrowed Material).
            return new ShaderPick(null, false, false, false);
        }

        // ── Sweeping the inventory ─────────────────────────────────────

        /// <summary>What one sweep found. **Not held statically** (used up on the spot).</summary>
        private struct Catalog
        {
            public UnityEngine.Shader Additive;
            public int AdditiveScore;
            public bool AdditiveParticle;

            public UnityEngine.Shader AlphaBlended;
            public int AlphaBlendedScore;
            public bool AlphaBlendedParticle;

            public UnityEngine.Shader Standard;

            public List<string> Names;
            public int MaterialCount;
            public int DistinctCount;
        }

        /// <summary>
        /// Sweeps every loaded <c>Material</c> exactly once and collects the <c>Shader</c>s
        /// riding on them. **It allocates, so do not call it from anywhere but
        /// <see cref="EnsureResolved"/>.**
        /// </summary>
        private static Catalog ScanCatalog()
        {
            var catalog = new Catalog();
            catalog.AdditiveScore = NoScore;
            catalog.AlphaBlendedScore = NoScore;
            catalog.Names = new List<string>();

            var seen = new HashSet<string>();

            // Resources.FindObjectsOfTypeAll returns inactive objects and assets too
            // (behaviour confirmed in the SceneObjects class doc). We want shaders, so no
            // narrowing happens here.
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            if (materials == null) return catalog;

            catalog.MaterialCount = materials.Length;

            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null) continue;   // fake-null (destroyed)

                UnityEngine.Shader s = m.shader;
                if (s == null) continue;

                string name = s.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!seen.Add(name)) continue;   // look at the same shader only once

                catalog.Names.Add(name);

                if (name == "Standard" && catalog.Standard == null) catalog.Standard = s;

                Consider(ref catalog, s, name);
            }

            catalog.DistinctCount = catalog.Names.Count;
            return catalog;
        }

        /// <summary>Scores one shader as a candidate for both preferences.</summary>
        private static void Consider(ref Catalog catalog, UnityEngine.Shader s, string name)
        {
            string lower = name.ToLowerInvariant();

            bool particle;

            int additive = ScoreFor(lower, ShaderPreference.Additive, out particle);
            if (Better(additive, catalog.AdditiveScore, name, catalog.Additive))
            {
                catalog.Additive = s;
                catalog.AdditiveScore = additive;
                catalog.AdditiveParticle = particle;
            }

            int alpha = ScoreFor(lower, ShaderPreference.AlphaBlended, out particle);
            if (Better(alpha, catalog.AlphaBlendedScore, name, catalog.AlphaBlended))
            {
                catalog.AlphaBlended = s;
                catalog.AlphaBlendedScore = alpha;
                catalog.AlphaBlendedParticle = particle;
            }
        }

        /// <summary>
        /// Whether the new candidate beats the current best. A tie is settled by ordinal name
        /// comparison — the order of <c>Resources.FindObjectsOfTypeAll</c> is not guaranteed,
        /// so **settling a tie by first-come means a different shader wins on each launch**
        /// (i.e. an appearance that does not reproduce).
        /// </summary>
        private static bool Better(int score, int bestScore, string name,
                                   UnityEngine.Shader best)
        {
            if (score == NoScore) return false;
            if (score < bestScore) return true;
            if (score > bestScore) return false;
            if (best == null) return true;
            return string.CompareOrdinal(name, best.name) < 0;
        }

        /// <summary>
        /// How worthwhile it is to borrow this one (**lower is better**).
        /// <see cref="NoScore"/> means "do not borrow".
        ///
        /// <paramref name="particle"/> says whether it may be counted as particle family; it
        /// is false at the tier of "translucent but not particle family".
        ///
        /// ★ Within a tier, names starting with <c>Custom/</c> go to the back. CS's own
        ///   shaders sometimes assume per-instance data supplied by the engine (the
        ///   distinction in the class doc), so putting one on a plain <c>Material</c> is that
        ///   much more likely not to come out as expected.
        /// </summary>
        private static int ScoreFor(string lower, ShaderPreference preference, out bool particle)
        {
            bool additive = lower.Contains("additive");
            bool alphaBlended = lower.Contains("alpha blended") || lower.Contains("alphablended");
            bool isParticle = lower.Contains("particle");

            // ★ "unlit" alone is no evidence of translucency ("Unlit/Color" is opaque).
            //   Standard's transparency settings are not applied to a borrowed shader, so
            //   picking up an opaque one here gives you **a coloured opaque slab**.
            //   "Unlit/Transparent" is caught by the "transparent" test below.
            bool transparent = lower.Contains("transparent");

            bool wantAdditive = preference == ShaderPreference.Additive;

            int tier;
            if (isParticle && additive) tier = wantAdditive ? 0 : 1;
            else if (isParticle && alphaBlended) tier = wantAdditive ? 1 : 0;
            else if (additive || alphaBlended) tier = 2;
            else if (isParticle) tier = 3;
            else if (transparent) tier = 4;
            else
            {
                // Do not borrow an opaque shader. Putting one on only gives you a flock of
                // slabs, and dropping Standard into transparent mode is more watchable.
                particle = false;
                return NoScore;
            }

            particle = tier <= 3;
            return tier * 2 + (lower.StartsWith("custom/", StringComparison.Ordinal) ? 1 : 0);
        }

        // ── Logging the inventory ─────────────────────────────────────

        /// <summary>
        /// States once what exists in this environment. **Caps how many are listed**
        /// (this is a diagnostic, not a dump. 12 lines at most).
        /// Particle-family and translucent-looking names come first, so the answer is in the
        /// first few lines.
        /// </summary>
        private static void LogInventory(Catalog catalog)
        {
            bool standardFindable = UnityEngine.Shader.Find("Standard") != null;

            Log.Info("shader inventory: " + catalog.DistinctCount
                     + " distinct shader(s) on " + catalog.MaterialCount
                     + " loaded material(s); Shader.Find(\"Standard\") = "
                     + (standardFindable ? "ok" : "NULL")
                     + " (a name present in the shipped assets does not mean Shader.Find "
                     + "resolves it - Unity strips shaders that are not in the build)");

            if (catalog.Names == null || catalog.Names.Count == 0) return;

            catalog.Names.Sort(CompareInventory);

            int shown = catalog.Names.Count < InventoryLogMax
                ? catalog.Names.Count : InventoryLogMax;

            var line = new StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                if (line.Length > 0) line.Append(" | ");
                line.Append(catalog.Names[i]);

                if ((i + 1) % NamesPerLine == 0 || i == shown - 1)
                {
                    Log.Info("shader inventory: " + line);
                    line.Length = 0;   // .NET 3.5 has no StringBuilder.Clear
                }
            }

            if (catalog.Names.Count > shown)
            {
                Log.Info("shader inventory: ... and " + (catalog.Names.Count - shown)
                         + " more (particle / transparent names are listed first)");
            }
        }

        /// <summary>Particle-family and translucent-looking names first, then ordinal order.</summary>
        private static int CompareInventory(string a, string b)
        {
            int ra = InterestRank(a);
            int rb = InterestRank(b);
            if (ra != rb) return ra - rb;
            return string.CompareOrdinal(a, b);
        }

        private static int InterestRank(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("particle") || lower.Contains("additive")
                || lower.Contains("alpha") || lower.Contains("transparent")
                || lower.Contains("unlit") || lower.Contains("standard"))
            {
                return 0;
            }
            return 1;
        }
    }
}
