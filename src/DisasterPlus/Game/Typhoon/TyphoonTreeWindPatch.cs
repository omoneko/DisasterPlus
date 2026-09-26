using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Shake the trees hard under a typhoon.</b> **Called per tree from the render
    /// thread.**
    ///
    /// ── Why poking the grid was not enough (2026-09-02) ────────────────────
    ///
    /// Tree sway comes out of <c>TreeInstance.RenderInstance</c> as
    ///
    /// <code>
    /// color.a = WeatherManager.GetWindSpeed(position);
    /// materialBlock.SetColor(TreeManager.ID_Color, color);
    /// </code>
    ///
    /// — <b>wind speed carried in the alpha of the colour</b>, which the tree shader
    /// uses as the amount of sway. And the body of <c>GetWindSpeed</c> is
    ///
    /// <code>
    /// exposure = pos.y - m_windGrid[cell].m_totalHeight / 64
    /// return Mathf.Clamp(exposure * 0.02 + 1, 0, 2)
    /// </code>
    ///
    /// ★★ **That trailing <c>Clamp(…, 0, 2)</c> was the whole story.**
    ///   The first attempt lowered <c>m_totalHeight</c> to buy more <c>exposure</c>,
    ///   but <b>however far you lower it the result tops out at 2.0</b> — calm is 1.0,
    ///   so <b>even at the ceiling you only ever get twice the sway</b>. Not enough
    ///   for "make it wilder".
    ///
    /// ── Getting past it from behind ───────────────────────────────────────
    ///
    /// <c>GetWindSpeed</c> is public, so a Harmony Postfix can multiply the result
    /// <b>outside the clamp</b>. Three advantages:
    ///
    /// <list type="bullet">
    /// <item><b>No ceiling.</b> We multiply beyond the 2.0 wall, so it can go as
    ///   strong as we like.</item>
    /// <item><b>Nothing is left in the save.</b> We do not write a single byte of
    ///   <c>m_windGrid</c> — that array is saved by <c>WeatherManager+Data.Serialize</c>,
    ///   and failing to restore it leaves a deranged shelter map in the city. Leave it
    ///   alone and that risk <b>disappears</b>.</item>
    /// <item><b>Wind power is unaffected.</b> Wind turbines go through a different
    ///   method, <c>SampleWindSpeed</c>. Poking the grid strengthened that too.</item>
    /// </list>
    ///
    /// ── Speed constraints ─────────────────────────────────────────────────
    ///
    /// ★★ This is called <b>once per frame for every tree that is drawn</b>. That runs
    ///   into the tens of thousands, so we <b>allocate nothing, resolve no Singleton
    ///   and take no lock</b>. All it looks at is four static floats and a squared
    ///   distance. With no typhoon about, a single read of <c>_active</c> returns
    ///   immediately.
    ///
    /// ★ The state is written by the sim thread through <see cref="SetStorm"/> and read
    ///   by the render thread. It is <c>volatile</c>, but **reading a stale value for
    ///   one frame breaks nothing** (the sway is simply one frame out of date), so no
    ///   lock is needed.
    /// </summary>
    [HarmonyPatch(typeof(WeatherManager), "GetWindSpeed", new[] { typeof(Vector3) })]
    public static class TyphoonTreeWindPatch
    {
        private static volatile bool _active;
        private static float _centreX;
        private static float _centreZ;
        private static float _radiusSquared;
        private static float _gain;

        /// <summary>
        /// The largest multiplier currently applied (diagnostics). 1 means it is doing
        /// nothing.
        /// </summary>
        public static float Gain { get { return _active ? _gain : 1f; } }

        /// <summary>
        /// Tell it where the typhoon is and how strong it is. **Sim thread.** May be
        /// called every tick.
        /// </summary>
        /// <param name="gain">
        /// The multiplier at the centre. 1 passes through untouched. <b>It is applied
        /// outside the 2.0 wall</b>, so 3 means the trees sway up to three times as much
        /// as normal.
        /// </param>
        public static void SetStorm(float centreX, float centreZ, float radiusMetres,
                                    float gain)
        {
            if (radiusMetres <= 0f || gain <= 1f) { Clear(); return; }

            _centreX = centreX;
            _centreZ = centreZ;
            _radiusSquared = radiusMetres * radiusMetres;
            _gain = gain;
            _active = true;
        }

        /// <summary>The typhoon has ended or left the city. **Always call this.**</summary>
        public static void Clear()
        {
            _active = false;
            _gain = 1f;
        }

        /// <summary>
        /// Postfix for <c>WeatherManager.GetWindSpeed(Vector3)</c>.
        /// Applies the multiplier **outside the clamp** (the ★★ in the class doc).
        /// </summary>
        /// <summary>Report a real measurement once, so we never guess aloud at whether
        /// this is working.</summary>
        private static bool _reported;

        public static void Postfix(Vector3 position, ref float __result)
        {
            if (!_active) return;

            if (!_reported)
            {
                _reported = true;
                Log.Info("typhoon: the tree-wind patch is live. Vanilla returned "
                         + __result.ToString("F2") + " here; we are multiplying by up to "
                         + _gain.ToString("F1") + ". **But the batched tree path packs "
                         + "this into a byte as round(wind * 128) clamped to 255 "
                         + "(TreeInstance.PopulateGroupData, IL_00CE-00E5), so the "
                         + "shader can never see more than 1.99.** 2x the calm sway is "
                         + "the game's own ceiling for trees.");
            }


            float dx = position.x - _centreX;
            float dz = position.z - _centreZ;
            float distanceSquared = dx * dx + dz * dz;

            if (distanceSquared >= _radiusSquared) return;

            // ★ Strongest at the centre, easing back to 1x at the rim, so the sway does
            //   not jump at the boundary.
            float t = 1f - distanceSquared / _radiusSquared;
            __result *= 1f + (_gain - 1f) * t;
        }
    }
}
