using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The rows for the fault band and for each building's headroom (this feature's
    /// centrepiece). **Main thread only.**
    ///
    /// It was split out of <see cref="EarthquakePanel"/> because that file had grown far
    /// past the project's 800-line rule, and **not one character of the content was
    /// changed**. Neither row creation nor assignment to <c>.text</c> appears in this
    /// file; both can only go through <see cref="EarthquakeRows"/> (the guarantee set out
    /// in its class doc).
    ///
    /// Everything here is **layer 1**. It merely reconstructs, from the same seed, the
    /// fixed threshold vanilla draws for each (building, disaster) pair; not one piece of
    /// new physics is added (§A-3).
    /// </summary>
    internal static class EarthquakeDamageRows
    {
        private static UILabel _faultLabel;
        private static UILabel _marginBuildingLabel;
        private static UILabel _marginVerdictLabel;
        private static UILabel _marginBurnLabel;
        private static UILabel _marginNoteLabel;
        private static UILabel _ndrNoteLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            _faultLabel = EarthquakeRows.AddLayer1Row(p, "FaultBand", ref y);

            // The fault band row **always** carries this note (a rule shared across the
            // plan). The positions of the four discs are re-drawn every step, so the band
            // is "where it could hit", not "where it will hit". The text is fixed, so it
            // is put in once here.

            _marginBuildingLabel = EarthquakeRows.AddLayer1Row(p, "MarginBuilding", ref y);
            _marginVerdictLabel = EarthquakeRows.AddLayer1Row(p, "MarginVerdict", ref y);

            // ★ The fire row (whole-feature review M1). This is half of what the request
            //    named when it said "fires and building collapses from the shaking", and
            //    the raw material (the second draw) was in
            //    BuildingMargin.BurnThresholdValue from the start.
            _marginBurnLabel = EarthquakeRows.AddLayer1Row(p, "MarginBurn", ref y, 28f);

            // This note is **always** printed alongside. Next to the row it states both
            // that the verdict covers only the whole-quake disc, and that it was already
            // settled the moment the earthquake began.
            _marginNoteLabel = EarthquakeRows.AddPlainRow(p, "MarginNote", ref y,
                Strings.EarthquakeGlobalDiscOnly, 38f);

            // ★ If NDR is present, state permanently that the collapse and fire verdicts
            //    **cannot be given in this environment** (whole-feature review C2, §E-2).
            //    NDR replaces DisasterHelpers.DestroyBuildings wholesale and swaps the
            //    probability from 0.02 to 0.04, so the ramp this mod reads is not being
            //    executed anywhere. Treated the same way as ①'s ForecastNdrNote.
            if (ModCompat.NdrPresent)
            {
                _ndrNoteLabel = EarthquakeRows.AddPlainRow(p, "NdrNote", ref y,
                    Strings.EarthquakeNdrNote, 38f);
            }
        }

        internal static void Refresh(EarthquakeSnapshot snapshot, EarthquakeReading primary,
                                     bool haveCursor, Vec3 cursor)
        {
            RefreshFaultRow(primary, haveCursor, cursor);
            RefreshMarginRows(snapshot);
        }

        /// <summary>
        /// Clears the values only. **The NDR note is not cleared** — it is a permanent
        /// explanation that no verdict can be given in this environment, not a reading.
        /// </summary>
        internal static void Clear()
        {
            EarthquakeRows.SetPlain(_faultLabel, "");
            EarthquakeRows.SetPlain(_marginBuildingLabel, "");
            EarthquakeRows.SetPlain(_marginVerdictLabel, "");
            EarthquakeRows.SetPlain(_marginBurnLabel, "");
            EarthquakeRows.SetPlain(_marginNoteLabel, "");
        }

        /// <summary>On level unload. Just drop the references (the objects go with the panel).</summary>
        internal static void Destroy()
        {
            _faultLabel = null;
            _marginBuildingLabel = null;
            _marginVerdictLabel = null;
            _marginBurnLabel = null;
            _marginNoteLabel = null;
            _ndrNoteLabel = null;
        }

        /// <summary>
        /// The fault band. Without the prefab values (<c>m_crackLength</c> /
        /// <c>m_crackWidth</c>) the geometry is not determined, so **the whole row is
        /// omitted**. Never restate "we do not know" as "outside" (see
        /// <see cref="FaultBand.Known"/>'s doc).
        /// </summary>
        private static void RefreshFaultRow(EarthquakeReading primary, bool haveCursor, Vec3 cursor)
        {
            EarthquakeRows.SetPlain(_faultLabel, "");

            if (!haveCursor) return;

            // ★ A subsiding earthquake drops no destruction discs (§A-3; they only exist
            //    in the Active branch). "Fault band: inside" is about where damage could
            //    still occur, so the whole row is omitted.
            if (!QuakeSelection.RunsDamage(primary.Phase)) return;

            var band = new FaultBand(primary.Epicentre.ToVec2(), primary.AngleRadians,
                                     primary.CrackLength, primary.CrackWidth);
            if (!band.Known) return;

            EarthquakeRows.SetLayer1(_faultLabel, Strings.EarthquakeFaultBand + ": "
                + (band.Contains(cursor.ToVec2())
                    ? Strings.EarthquakeFaultInside
                    : Strings.EarthquakeFaultOutside));
            // This note is always printed alongside (a rule shared across the plan). The
            // band is "where it could hit", not "where it will hit".
        }

        /// <summary>
        /// **This feature's centrepiece.** Whether the building under the cursor will
        /// fall in this earthquake.
        ///
        /// For each building, vanilla draws a fixed threshold from
        /// <c>new Randomizer(buildingID | (disasterID &lt;&lt; 16))</c> (§A-3). That seed
        /// depends on neither the frame nor the step, and the whole-quake disc's epicentre
        /// does not move, so **the conclusion is already settled the moment the earthquake
        /// begins**. That is the answer to the request's "fires and collapses from the
        /// shaking are probably random", and it is why this row alone can be written as
        /// fact rather than as a prediction.
        ///
        /// The range over which we may assert anything is narrow, though. What is handled
        /// here is only the whole-quake disc (probability = 0.02, centred on the
        /// epicentre); nothing can be said about the four fault discs (probability = 1,
        /// re-positioned every step). **Inside the band, and when the band's geometry
        /// cannot be read, never say "it will not collapse"** — that is guaranteed by the
        /// order of the branches over in <see cref="BuildingMargin.Evaluate"/>, and this
        /// method only writes its conclusion out.
        ///
        /// Every value is from the sim thread's read one tick ago, and **this method never
        /// touches the building buffers** (see <see cref="BuildingProbe"/>'s class doc).
        /// </summary>
        private static void RefreshMarginRows(EarthquakeSnapshot snapshot)
        {
            var margin = snapshot.CursorBuilding;

            // The note only accompanies a row that is actually shown (never leave the
            // note alone below a blank row).
            EarthquakeRows.SetPlain(_marginNoteLabel, "");

            // CursorQuakeId == 0 means "we have not looked yet" — either the cursor is not
            // over terrain, or there is no earthquake running a destruction pass
            // (Active / Emerging).
            // **Never write "there is no building under the cursor" in this state.**
            // It is the same 0 when the cursor IS over a building, so that would be a lie.
            // When there is nothing we can say, say nothing.
            if (snapshot.CursorQuakeId == 0)
            {
                EarthquakeRows.SetPlain(_marginBuildingLabel, "");
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            // ★ Tell "we looked and there was no building" apart from "we could not
            //    look" (whole-feature review I3). It used to print the same "there is no
            //    building under the cursor" whether BuildingManager was unavailable or
            //    the sweep threw — a failed read wearing the face of a measurement,
            //    exactly the breakage this feature forbids in every other row.
            if (snapshot.CursorProbe == BuildingProbeOutcome.Failed)
            {
                EarthquakeRows.SetPlain(_marginBuildingLabel, Strings.EarthquakeProbeFailed);
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            if (!margin.HasBuilding)
            {
                EarthquakeRows.SetLayer1(_marginBuildingLabel,
                    Strings.EarthquakeBuildingUnderCursor + ": " + Strings.EarthquakeNoBuilding);
                EarthquakeRows.SetPlain(_marginVerdictLabel, "");
                EarthquakeRows.SetPlain(_marginBurnLabel, "");
                return;
            }

            // Always name which earthquake the verdict is about. With several running at
            // once, the earthquake the rows above have picked (SelectPrimary) and the one
            // judged here (QuakeSelection.SelectDamaging on the sim side) can differ.
            EarthquakeRows.SetLayer1(_marginBuildingLabel,
                Strings.EarthquakeBuildingUnderCursor + ": #" + margin.BuildingId
                + "   (#" + snapshot.CursorQuakeId + ")");

            EarthquakeRows.SetLayer1(_marginVerdictLabel, VerdictText(margin));
            EarthquakeRows.SetLayer1(_marginBurnLabel, BurnVerdictText(margin));
            EarthquakeRows.SetPlain(_marginNoteLabel, Strings.EarthquakeGlobalDiscOnly);
        }

        /// <summary>
        /// The conclusion, in one row. **The tables in design doc §3.2 and plan 5.2 are
        /// this switch, verbatim.** Do not mix up, here, the states where an assertion is
        /// allowed with the states where it is not.
        /// </summary>
        private static string VerdictText(BuildingMargin margin)
        {
            string current = Strings.EarthquakeCurrentDistance + " "
                             + margin.Distance.ToString("F0") + " m";
            // "It collapses within X m of the epicentre". A building with X <= 0 does not
            // fall to the whole-quake disc even standing on the epicentre (its threshold
            // is 200 or more).
            string within = Strings.EarthquakeCollapseWithin + " "
                            + margin.CollapseWithin.ToString("F0") + " m";

            switch (margin.Verdict)
            {
                case CollapseVerdict.AlreadyDown:
                    return Strings.EarthquakeAlreadyDown;

                case CollapseVerdict.OutOfRange:
                    // The region where vanilla's preRadius cuts the check off entirely.
                    return Strings.EarthquakeOutOfRange + "   (" + current + ")";

                case CollapseVerdict.Unknown:
                    // The fault geometry could not be read. Show the numbers, but give no
                    // verdict.
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictUnknown;

                case CollapseVerdict.DamageModelReplaced:
                    // ★ The destruction code has been replaced by another mod (§E-2).
                    //    **Not even the distance is shown.** "Within X m", derived from
                    //    vanilla's 0.02, is a number nobody is using in that environment,
                    //    and printing it alongside would defeat the point of withholding
                    //    the verdict.
                    return current + "   -> " + Strings.EarthquakeVerdictNdr;

                case CollapseVerdict.InsideFaultZone:
                    // Show the collapse distance, but never say "it will not fall"
                    // (inside the band, the probability = 1 destruction discs decide
                    // separately).
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeFaultBand + ": " + Strings.EarthquakeFaultInside;

                case CollapseVerdict.WillCollapse:
                    return within + " / " + current + "   -> "
                           + Strings.EarthquakeVerdictCollapse;

                case CollapseVerdict.Survives:
                    // "It will not collapse", with respect to the whole-quake disc only.
                    // The only buildings that can reach here are those **outside** the
                    // fault band (the branch order in BuildingMargin.Evaluate guarantees
                    // it).
                    return margin.CollapseWithin > 0f
                        ? within + " / " + current + "   -> " + Strings.EarthquakeVerdictSurvive
                        : current + "   -> " + Strings.EarthquakeVerdictSurviveAnyDistance;

                default:
                    // ★ The default must not be "it will not collapse". If a value is
                    //    added to CollapseVerdict in future and this is not updated, we
                    //    would be silently asserting survival — exactly the breakage this
                    //    feature most wants to avoid. Fall back on "no verdict" for any
                    //    conclusion we do not recognise.
                    return Strings.EarthquakeVerdictUnknown;
            }
        }

        /// <summary>
        /// The fire conclusion, in one row (whole-feature review M1). **Structurally
        /// identical to the collapse row**; only the threshold drawn (the second draw)
        /// and the wording differ (§A-3).
        ///
        /// **Collapse takes precedence.** The IL is <c>else if (hitB &amp;&amp; ...)</c>,
        /// so when the same building is also hit by the collapse, vanilla never reaches
        /// the fire branch (the collapse side takes <c>burnAmount = Round(fB*255)</c>
        /// with it). Hide that ordering and "it will collapse" and "it will catch fire"
        /// appear together, reading as though both will happen.
        /// </summary>
        private static string BurnVerdictText(BuildingMargin margin)
        {
            string within = Strings.EarthquakeBurnWithin + " "
                            + margin.BurnWithin.ToString("F0") + " m";

            string body;
            switch (margin.BurnVerdict)
            {
                case CollapseVerdict.AlreadyDown:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeAlreadyDown;

                case CollapseVerdict.OutOfRange:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeOutOfRange;

                case CollapseVerdict.Unknown:
                    body = within + "   -> " + Strings.EarthquakeVerdictUnknown;
                    break;

                case CollapseVerdict.DamageModelReplaced:
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeVerdictNdr;

                case CollapseVerdict.InsideFaultZone:
                    body = within + "   -> " + Strings.EarthquakeFaultBand + ": "
                           + Strings.EarthquakeFaultInside;
                    break;

                case CollapseVerdict.WillCollapse:
                    body = within + "   -> " + Strings.EarthquakeVerdictBurn;
                    break;

                case CollapseVerdict.Survives:
                    body = margin.BurnWithin > 0f
                        ? within + "   -> " + Strings.EarthquakeVerdictNoBurn
                        : Strings.EarthquakeVerdictNoBurnAnyDistance;
                    break;

                default:
                    // For the same reason as the collapse side, fall back on "no verdict"
                    // for any conclusion we do not recognise.
                    return Strings.EarthquakeBurnLabel + ": " + Strings.EarthquakeVerdictUnknown;
            }

            // If the collapse is settled, state alongside that the fire branch is never
            // reached.
            if (margin.Verdict == CollapseVerdict.WillCollapse)
            {
                body += "\n" + Strings.EarthquakeBurnAfterCollapse;
            }
            return body;
        }
    }
}
