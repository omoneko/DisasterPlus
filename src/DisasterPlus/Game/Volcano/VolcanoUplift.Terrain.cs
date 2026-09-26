using System;
using ColossalFramework;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The part of the uplift that **actually touches terrain** (writing heights,
    /// <c>UpdateArea</c>, the crater).
    /// It was split out of <c>VolcanoUplift.cs</c> because that file went past the project's
    /// 800-line rule, and **not one character of the content was changed**
    /// (the same shape as <c>VolcanoClearing.Sweep.cs</c> / <c>VolcanoLava.Ignite.cs</c>).
    ///
    /// The main file's class doc holds all of the discipline. The three things to keep here in
    /// particular:
    ///   - <c>UpdateArea</c> **exactly once per tick** (trap 3)
    ///   - **Never call** <c>MakeCrater</c> (the crater is part of the height profile. Report ①)
    ///   - **Never call** <c>Begin/EndUpdateArea</c> (§D-12)
    ///
    /// **Sim thread only.**
    /// </summary>
    public static partial class VolcanoUplift
    {
        /// <summary>
        /// Write **the absolute target at this instant** to every cell within the range the
        /// clearing has reached (trap 2).
        ///
        /// **It only writes; it does not call <c>UpdateArea</c>.** The terrain's discipline is
        /// that nobody reads what was written until the writer calls <c>UpdateArea</c> (§D-12), so
        /// this one goes over the whole area first and the display catches up one tile at a time.
        /// </summary>
        private static bool WriteHeights(VolcanoFootprint footprint)
        {
            ushort[] raw = ReadRawHeights();
            if (raw == null || _baseRaw == null) return false;

            if (_profile == null) return false;

            float centreX = footprint.Centre.X;
            float centreZ = footprint.Centre.Z;
            float activeSquared = _activeRadius * _activeRadius;
            float height = footprint.HeightMetres;

            int written = 0;
            int clipped = 0;
            _dirtyValid = false;
            _dirtyMinX = 0;
            _dirtyMinZ = 0;
            _dirtyMaxX = 0;
            _dirtyMaxZ = 0;

            for (int z = _minZ; z <= _maxZ; z++)
            {
                float worldZ = (z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                float dz = worldZ - centreZ;
                float dz2 = dz * dz;
                if (dz2 > activeSquared) continue;

                int rowRaw = z * RawStride;
                int rowBase = (z - _minZ) * _width;

                for (int x = _minX; x <= _maxX; x++)
                {
                    float worldX = (x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    float dx = worldX - centreX;
                    float d2 = dx * dx + dz2;
                    if (d2 > activeSquared) continue;

                    int cell = rowBase + (x - _minX);

                    // ★★ **It spreads outwards from the summit** (the doc of
                    //    UpliftSchedule.GrowthMetresAt).
                    //    Not profile × progress — that makes the whole mountain swell uniformly.
                    //
                    // ★★ <b>That rule only applies to the cone.</b> (2026-08-22)
                    //    <c>GrowthMetresAt</c> returns 0 for <c>profileMetres &lt;= 0</c>, so
                    //    **the caldera (a negative profile) would not be dug by a single
                    //    millimetre this way.**
                    //    And "the whole thing moves uniformly" is also the correct picture for the
                    //    inflation and the foundering, so write them plainly as
                    //    <c>profile × progress</c>.
                    float grown = _stage == UpliftStage.Cone
                        ? UpliftSchedule.GrowthMetresAt(_profile[cell], height, _progress)
                        : _profile[cell] * _progress;

                    // This is an absolute target, so pass 1 for the progress (grown is already
                    // "the height right now").
                    ushort target = UpliftSchedule.RawTargetAt(_baseRaw[cell], grown, 1f);

                    // ★★ **Count whether it hit the game's height ceiling (1023.98 m).**
                    //    If it does, the summit goes flat there. **Do not present a flat mountain
                    //    silently** — all the player can see is "the height setting is not
                    //    working".
                    //    The reason the ceiling cannot be raised is in the doc of
                    //    <c>UpliftSchedule.CeilingClipped</c>.
                    if (UpliftSchedule.CeilingClipped(_baseRaw[cell], grown, 1f)) clipped++;

                    int index = rowRaw + x;
                    // ★ The same "write only when it changed" as vanilla's MakeCrater (§C-8 IL_01E7).
                    if (raw[index] == target) continue;

                    raw[index] = target;
                    written++;

                    // ★ **Flush only the range that actually changed** (class doc).
                    //   The bounding rectangle counted here becomes the target of UpdateArea
                    //   directly.
                    if (!_dirtyValid)
                    {
                        _dirtyValid = true;
                        _dirtyMinX = x;
                        _dirtyMaxX = x;
                        _dirtyMinZ = z;
                        _dirtyMaxZ = z;
                    }
                    else
                    {
                        if (x < _dirtyMinX) _dirtyMinX = x;
                        if (x > _dirtyMaxX) _dirtyMaxX = x;
                        if (z < _dirtyMinZ) _dirtyMinZ = z;
                        if (z > _dirtyMaxZ) _dirtyMaxZ = z;
                    }
                }
            }

            _cellsWrittenLastTick = written;
            _ceilingClippedCells = clipped;
            return true;
        }

        /// <summary>
        /// <c>UpdateArea</c> **the range not yet on screen, exactly once per tick** (trap 3).
        ///
        /// The procedure:
        ///   1. Union the rectangle rewritten this tick (<see cref="_dirtyValid"/>) into the
        ///      rectangle not yet fully flushed.
        ///   2. If the accumulated rectangle can be emitted in one go
        ///      (<c>TileSplit.FitsSinglePass</c>), emit it as is and **empty the accumulator**.
        ///      The first half of the uplift goes through here, so
        ///      **the whole changed area reaches the screen every tick**.
        ///   3. If it cannot, go round the tiles one at a time and empty it once a lap is done.
        ///
        /// **What <c>TileAt</c> / <c>ExpandForPass</c> return is "the rectangle to pass straight
        /// through".** Do not add the margin again here — add it and you get
        /// 103×103 = 10609 cells, crossing the 10000 threshold and flushing every time
        /// (the class doc of <c>TileSplit</c>).
        ///
        /// <c>surface</c> / <c>zones</c> are false because those are the same arguments vanilla's
        /// <c>MakeCrater</c> / <c>MakeCrack</c> use (§C-8 IL_021C).
        /// </summary>
        private static void FlushPending()
        {
            int tMinX, tMinZ, tMaxX, tMaxZ;
            if (!_flush.Next(_dirtyValid, _dirtyMinX, _dirtyMinZ, _dirtyMaxX, _dirtyMaxZ,
                             out tMinX, out tMinZ, out tMaxX, out tMaxZ))
            {
                return;
            }

            // ★ Pass the returned rectangle through **as is**. Do not add the margin again
            //   (add it and it crosses the 10000 threshold and mid-batch flushes every time).
            TerrainModify.UpdateArea(tMinX, tMinZ, tMaxX, tMaxZ, true, false, false);
        }

        // ★★ CarveCrater (one call to DisasterHelpers.MakeCrater) used to live here.
        //    It was deleted once the crater became part of the height profile (2026-08-22, live
        //    report ①).
        //    **Do not put it back** — putting it back makes the hollow come into being at "the end
        //    of the uplift" again, and adds one more forced flush. The shape is in
        //    Core/Volcano/VolcanoCrater.
    }
}
