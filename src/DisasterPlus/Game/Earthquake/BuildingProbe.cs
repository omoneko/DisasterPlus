using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The kind of result** you get from looking for the building under the cursor.
    ///
    /// **It exists for one reason: so "there was no building" and "we could not look" do
    /// not wear the same face.** Both used to come out as <see cref="BuildingMargin.None"/>,
    /// which the display side could not tell apart, so it wrote "there is no building
    /// under the cursor" — a failed read appearing with the face of a meaningful zero,
    /// exactly the breakage this feature forbids in every other row (the same reason as
    /// <c>EarthquakeReading.CoverageKnown</c> /
    /// <c>EarthquakeSnapshot.CursorCoverageValid</c>).
    /// </summary>
    public enum BuildingProbeOutcome
    {
        /// <summary>We never looked at all (invalid cursor, no earthquake to work with, etc.).</summary>
        NotProbed,

        /// <summary>We looked and found one building.</summary>
        Found,

        /// <summary>We looked and there was not a single candidate within <c>PickRadius</c>. **This is a measured result.**</summary>
        Empty,

        /// <summary>
        /// We could not look. <c>BuildingManager</c>, its buffers or the grid were
        /// unavailable, or the sweep threw. **Never assert anything about whether a
        /// building is there while in this state.**
        /// </summary>
        Failed,
    }

    /// <summary>
    /// Identifies the one building under the cursor and works out its headroom.
    /// **Sim thread only.**
    ///
    /// ── Why the cursor's building is identified on the sim thread ──────
    ///
    /// <c>BuildingManager.m_buildings.m_buffer</c> and <c>m_buildingGrid</c> are
    /// **owned by the sim thread**. The cursor position, on the other hand, comes from
    /// <c>Input.mousePosition</c> and <c>Camera.main</c>, so it **can only be read on the
    /// main thread**. Hence:
    ///
    /// <code>
    /// main (EarthquakePanel.Tick)
    ///   → work out the intersection with the terrain (the existing TryPickCursorGround)
    ///   → EarthquakeHub.PublishCursor(worldPos, valid)
    /// sim (EarthquakeReader.Read)
    ///   → EarthquakeHub.TakeCursor(out pos)
    ///   → BuildingProbe.ProbeAt(...)  ← only here do we touch the building buffers
    ///   → put it on snapshot.CursorBuilding
    /// main (EarthquakePanel.Refresh)
    ///   → just draw snapshot.CursorBuilding
    /// </code>
    ///
    /// **This costs one tick of lag (up to 1/50 s).** Move the cursor quickly and the
    /// readout follows a frame behind. That is the right price to pay, and the building
    /// buffers must never be read from the main thread to get rid of it. Break this and
    /// an <c>IndexOutOfRangeException</c> with no stack trace turns up later, inside
    /// vanilla's own code, where this mod's try/catch cannot catch it.
    ///
    /// The sweep follows <c>FireWhirlDamage.CollectNearby</c> exactly (cell size 64,
    /// offset 135, clamped to <c>[0,269]</c>, the <c>m_nextGridBuilding</c> chain, and
    /// the <c>guard &gt; 32768</c> safety net).
    /// </summary>
    public static class BuildingProbe
    {
        /// <summary>
        /// Buildings within this distance of the cursor position are candidates (one
        /// building-grid cell). It is not the building's own hit test, so it means
        /// roughly "you are pointing around here".
        /// </summary>
        public const float PickRadius = 64f;

        /// <summary>
        /// The flag condition for being a candidate. **The same mask and the same
        /// comparison as vanilla's first-pass cull in <c>DestroyBuildings</c>** (§A-3):
        ///
        /// <code>if ((m_flags &amp; 524307 /* 0x80013 */) != 1) continue;</code>
        ///
        /// 0x80013 = <c>Created | Deleted | Untouchable | Demolishing</c> (that those
        /// four names really do add up to this value was measured by reflecting over
        /// <c>Assembly-CSharp</c>). So vanilla only considers buildings that have Created
        /// set and none of the other three. Loosen this and we end up declaring "this
        /// will collapse" about buildings vanilla would not even look at.
        ///
        /// **<c>Collapsed</c> (0x400000) is not in this mask.** Vanilla does not exclude
        /// it, and neither must we — exclude it and standing over rubble produces the
        /// **wrong** explanation, "there is no building under the cursor". A collapsed or
        /// burning building is not dropped from the candidates; it is passed to
        /// <see cref="BuildingMargin.Evaluate"/> as <c>alreadyDown</c> and declared on
        /// the conclusion side instead.
        ///
        /// (<c>FireWhirlDamage.CollectMask</c> does exclude Collapsed, because it is
        ///  choosing candidates to set alight — a different purpose.)
        /// </summary>
        private const Building.Flags CandidateMask =
            Building.Flags.Created | Building.Flags.Deleted
            | Building.Flags.Untouchable | Building.Flags.Demolishing;

        /// <summary>Whether an unexpected exception during the sweep has been shouted about once.</summary>
        private static bool _probeErrorLogged;

        /// <summary>
        /// The headroom of the one building under the cursor.
        /// The return value only means anything when <paramref name="outcome"/> is
        /// <see cref="BuildingProbeOutcome.Found"/>.
        ///
        /// **Returning three states is itself the specification** (see
        /// <see cref="BuildingProbeOutcome"/>'s doc). Conflate "we looked and it was
        /// empty" with "we could not look" and a failed read comes out wearing the face
        /// of a measurement: "there is no building under the cursor".
        ///
        /// **It never throws.** An <c>IndexOutOfRangeException</c> on the sim thread
        /// becomes a popup with no stack trace, so every index is guarded by the array's
        /// length.
        /// </summary>
        /// <param name="heightMetres">
        /// The height of the building found (m). **For layer 2 (long-period ground
        /// motion) only**; layer 1's headroom does not use height at all, because vanilla
        /// does not (§A-3 looks only at the distance from <c>m_position</c>).
        /// **0 means "could not be read", not "short"**
        /// (<see cref="BuildingHeight.MetresOf"/>).
        /// </param>
        public static BuildingMargin ProbeAt(Vec3 worldPos, EarthquakeReading quake,
                                             FaultBand band, bool damageModelReplaced,
                                             out BuildingProbeOutcome outcome,
                                             out float heightMetres)
        {
            outcome = BuildingProbeOutcome.NotProbed;
            heightMetres = 0f;
            if (quake == null) return BuildingMargin.None();

            try
            {
                var bm = BuildingManager.instance;
                if (bm == null)
                {
                    outcome = BuildingProbeOutcome.Failed;
                    return BuildingMargin.None();
                }

                var buildings = bm.m_buildings != null ? bm.m_buildings.m_buffer : null;
                var grid = bm.m_buildingGrid;
                if (buildings == null || grid == null)
                {
                    outcome = BuildingProbeOutcome.Failed;
                    return BuildingMargin.None();
                }

                ushort best = FindNearest(buildings, grid, worldPos);
                if (best == 0)
                {
                    // ★ This is the only place that means "we looked and there was
                    //   nothing". The sweep ran to completion.
                    outcome = BuildingProbeOutcome.Empty;
                    return BuildingMargin.None();
                }

                var p = buildings[best].m_position;
                var flags = buildings[best].m_flags;
                bool alreadyDown = (flags & Building.Flags.Collapsed) != Building.Flags.None
                                   || buildings[best].m_fireIntensity != 0;

                // A quantity layer 2 uses. It goes nowhere near layer 1's conclusion
                // (BuildingMargin).
                heightMetres = BuildingHeight.MetresOf(ref buildings[best]);

                outcome = BuildingProbeOutcome.Found;
                return BuildingMargin.Evaluate(
                    best, quake.DisasterId,
                    new Vec2(p.x, p.z), quake.Epicentre.ToVec2(),
                    quake.Intensity, band, alreadyDown, damageModelReplaced);
            }
            catch (System.Exception e)
            {
                // Log.Warn / Log.Error are not throttled. This path runs every sim tick,
                // so shout once and then drop down to the per-key throttle
                // (the same shape as EarthquakeReader._readErrorLogged).
                if (!_probeErrorLogged)
                {
                    _probeErrorLogged = true;
                    Log.Error("earthquake building probe failed", e);
                }
                else
                {
                    Log.Diag("EqProbe", "building probe failed: " + e.GetType().Name);
                }
                outcome = BuildingProbeOutcome.Failed;
                heightMetres = 0f;
                return BuildingMargin.None();
            }
        }

        /// <summary>
        /// The building nearest the cursor within <see cref="PickRadius"/>, or 0 if there
        /// is none.
        /// </summary>
        private static ushort FindNearest(Building[] buildings, ushort[] grid, Vec3 worldPos)
        {
            // The building grid is 270x270 cells of 64 m. Clamp so we never run off the edge.
            int minX = Clamp((int)((worldPos.X - PickRadius) / 64f + 135f));
            int maxX = Clamp((int)((worldPos.X + PickRadius) / 64f + 135f));
            int minZ = Clamp((int)((worldPos.Z - PickRadius) / 64f + 135f));
            int maxZ = Clamp((int)((worldPos.Z + PickRadius) / 64f + 135f));

            var cursor2d = worldPos.ToVec2();
            float bestDistanceSquared = PickRadius * PickRadius;
            ushort best = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = z * 270 + x;
                    if (cell < 0 || cell >= grid.Length) continue;

                    ushort id = grid[cell];
                    int guard = 0;

                    while (id != 0 && id < buildings.Length)
                    {
                        // Vanilla writes `!= 1 -> continue`. Do not tidy it into
                        // something more readable; copy it across comparison operator
                        // and all.
                        if ((buildings[id].m_flags & CandidateMask) == Building.Flags.Created)
                        {
                            var p = buildings[id].m_position;
                            float d2 = cursor2d.DistanceSquaredTo(new Vec2(p.x, p.z));
                            if (d2 < bestDistanceSquared)
                            {
                                bestDistanceSquared = d2;
                                best = id;
                            }
                        }

                        id = buildings[id].m_nextGridBuilding;

                        // Insurance against looping forever on a save whose linked list
                        // is corrupt.
                        if (++guard > 32768) break;
                    }
                }
            }

            return best;
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 269) return 269;
            return v;
        }
    }
}
