namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// <b>Is this cell "open sea"?</b> The layer that does not touch the engine.
    ///
    /// ── Why this was lifted into Core ─────────────────────────────────
    ///
    /// This one formula got <b>rewritten three times in three days</b>. Each time, looking
    /// at only one half of it meant mistaking some other bit of terrain for sea
    /// (2026-08-31, cross-checks 5 and 6):
    ///
    /// <list type="bullet">
    /// <item>The version that looked at <b>the seabed height alone</b> … answered "sea"
    ///   for polder land ringed by dykes, and for craters. They sit below sea level and
    ///   yet are <b>dry</b>. Put a 3.8 km radius water source there and the draining
    ///   circle is only 160 m across, so <b>water you can never get rid of stays there
    ///   forever</b>.</item>
    /// <item>The version that looked at <b>the water column alone</b> … answered "sea" for
    ///   rivers, for high-altitude lakes, and for <b>a town flooded by the previous
    ///   tsunami</b>. The second tsunami spreads its circle over that and suddenly floods
    ///   land lower than the target — the same failure, entered through the back door.</item>
    /// <item>The version that rejected rivers <b>by water-surface height</b> … answered
    ///   "not sea" for <b>the entire open ocean</b> during a storm surge (because the
    ///   surface rises). The player gets told "no deep sea within 2,304 m" — while looking
    ///   straight at the sea.</item>
    /// </list>
    ///
    /// ★★ **So the formula is pinned here and held down by tests.**
    ///   The Game layer's job is only to pull three numbers out of the arrays and pass
    ///   them in here.
    /// </summary>
    public static class SeaCell
    {
        /// <summary>
        /// Is it open sea?
        ///
        /// <code>
        /// column &gt;= 2 m                    … there really is water (rejects dry hollows)
        /// and seabed &lt;= sea level - min depth … it is deep enough sea (rejects rivers, flooded land)
        /// </code>
        ///
        /// ★★ <b>The seabed alone decides the depth.</b> Demand a minimum depth of the
        ///   water column and the open ocean becomes "not sea" while the water draws back.
        ///   This is a fourth way in, separate from the three in the class doc: there the
        ///   open ocean was rejected by its <b>surface height</b>, here by the
        ///   <b>thickness of the column</b>.
        ///
        /// ★ The seabed does not move, so <b>the sea stays sea even in the middle of a
        ///   storm surge</b>.
        /// </summary>
        /// <param name="terrainUnits">Seabed elevation (1/64 m).</param>
        /// <param name="columnUnits">The thickness of the water sitting on top of it (1/64
        /// m).</param>
        /// <param name="seaUnits">Normal sea level (1/64 m).</param>
        /// <param name="minDepthUnits">The minimum depth we require (1/64 m).</param>
        public static bool IsOpenSea(int terrainUnits, int columnUnits,
                                     int seaUnits, int minDepthUnits)
        {
            // ★★ **Do not demand "depth" of the water column. Only ask whether it is there.**
            //    (2026-08-31, cross-check 7)
            //
            //    Demand the minimum depth of the water column itself and <b>the open ocean
            //    becomes "not sea" while the water draws back</b> — a mirror image of the
            //    mistake we made with the storm surge (the third failure in this class).
            //    The depth is decided by <b>the seabed height</b>. The seabed does not move
            //    with the waves.
            //
            //    The water column is looked at <b>solely to reject dry hollows</b>.
            if (columnUnits < PresenceUnits) return false;
            return terrainUnits <= seaUnits - minDepthUnits;
        }

        /// <summary>
        /// The thickness at which we accept "there is water" (in 1/64 m units, i.e. 2 m).
        /// **It is not a depth** — depth is decided by the seabed (see the ★★ in
        /// <see cref="IsOpenSea"/>).
        ///
        /// ★ 2 m sits <b>between the two failures</b>.
        ///   Too small and rainwater pooled in a deep hollow reads as "sea" (which wrecks
        ///   the save). Too large and the open ocean reads as "not sea" while the water
        ///   draws back (which is a lying message). The former is the heavier of the two,
        ///   so the value leans that way.
        /// </summary>
        public const int PresenceUnits = 128;
    }
}
