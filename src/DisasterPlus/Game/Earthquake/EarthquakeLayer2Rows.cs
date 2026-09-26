using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The layer-2 section — the place where only behaviour this mod invented goes.**
    /// Main thread only.
    ///
    /// ── If this gets mixed with layer 1, the feature loses its reason to exist ──
    ///
    /// Everything in layer 1 (the tab contents) is a quantity derived from vanilla's own
    /// formulae and constants, and carries the <c>[measured]</c> prefix. Nothing in this
    /// section is measured — vanilla does not raise a tsunami from an undersea
    /// earthquake, and it does not use building height for the shaking or the damage
    /// (§A-7 / §A-3). Therefore:
    ///
    ///   - rows are always made with
    ///     <see cref="EarthquakeRows.AddLayer2Row(UIPanel,string,ref float)"/>
    ///     (their contents can only be written by <see cref="EarthquakeRows.SetLayer2"/>,
    ///     which always prefixes <c>Strings.SourceModel</c>)
    ///   - the section heading (<c>EarthquakeLayer2Header</c>) declares "behaviour added
    ///     by Disaster + (not present in the base game)"
    ///   - the colour differs from layer 1 too (but **colour alone is not relied on**)
    ///
    /// **This section sits outside the tabs.** That keeps the plan's shared rule —
    /// "layer 2 is built below layer 1, with no rearranging at runtime" — intact even
    /// after the move to tabs (see <see cref="EarthquakePanelTabs"/>'s doc). Whichever
    /// tab you are looking at, the behaviour this mod adds is always in the same place,
    /// below everything layer 1 has to say.
    ///
    /// ── The section folds a block at a time (the vertical budget really has run out) ──
    ///
    /// Layer 2 holds **several independent features** (the tsunami chain and long-period
    /// ground motion). Reserving every row all the time makes the panel more than 150 px
    /// taller for a player who uses none of them, a step closer to
    /// <see cref="EarthquakePanel.ClampToView"/> warning that it is taller than the view.
    /// So **only the rows of blocks whose setting is on are stacked**.
    ///
    /// Repositioning (<see cref="Layout"/>) runs **only at construction and on the frame
    /// where the on/off combination changes**. Run the layout every frame and
    /// <see cref="EarthquakePanel.Relayout"/> keeps rewriting <c>relativePosition</c>, so
    /// the panel drifts about slightly all the time.
    ///
    /// ── The wording (never write up something we have not done as though we had) ──
    ///
    /// ★★ **Rewritten on 2026-08-29.** The DLC's <c>TsunamiAI</c> still cannot raise a
    /// wave anywhere but the map's edge, but **we no longer use TsunamiAI.** We place the
    /// same solver's <c>TYPE_IMPACT</c> (an external force that can go anywhere) at the
    /// hypocentre, so <c>EarthquakeTsunamiFromShore</c> now says the wave spreads out in
    /// circles from the hypocentre. The principle has not changed — never write up
    /// something we have not done as though we had. It was rewritten because it became
    /// true, not as advertising
    /// (docs/superpowers/specs/2026-08-29-tsunami-il-facts.md).
    ///
    /// On the long-period side, <c>EarthquakeLongPeriodNote</c> declares that vanilla
    /// uses building height for neither the shaking nor the damage. Without that one
    /// sentence, players take "the taller it is, the more it shakes" to be how the game
    /// works.
    /// </summary>
    internal static class EarthquakeLayer2Rows
    {
        /// <summary>
        /// Whether to show the tsunami section.
        ///
        /// ★★ **Never decide this from the setting alone.** (2026-08-30, third round of
        ///   verification) <c>EarthquakeTsunamiChain</c> is off by default, but a trench
        ///   earthquake brings a tsunami with it regardless of the setting. Because we
        ///   were hiding it on the setting alone, <b>for the several minutes between the
        ///   click and the wave arriving, nothing appeared on screen at all</b>. Showing
        ///   nothing while somebody waits is read as "it is broken".
        /// </summary>
        private static bool TsunamiRowWanted
        {
            get
            {
                // ★★ **Always show it while the wave is running.** (2026-08-31, fourth
                //    round of verification) <c>LastId</c> becomes 0 the moment the
                //    earthquake's disaster slot is freed, but the wave carries on
                //    travelling for nearly 10 minutes after that. On its own that means
                //    <b>the row disappears before the wave arrives</b> — to somebody
                //    waiting it can only look like "it is over" or "it is broken".
                return TsunamiRing.Running
                       || ModSettings.EarthquakeTsunamiChain.value
                       || TrenchQuakeSlot.LastId != 0;
            }
        }

        /// <summary>The heading. Shown whenever at least one block is shown.</summary>
        private const int BlockHeader = 0;

        /// <summary>The tsunami chain (Task 9). Follows <c>ModSettings.EarthquakeTsunamiChain</c>.</summary>
        private const int BlockTsunami = 1;

        /// <summary>Long-period ground motion (Task 10). Follows <c>ModSettings.EarthquakeLongPeriod</c>.</summary>
        private const int BlockLongPeriod = 2;

        /// <summary>The layout information for one row. <see cref="Step"/> is how far the row advances y.</summary>
        private struct Layer2Row
        {
            internal UILabel Label;
            internal float Step;
            internal int Block;
        }

        private static readonly List<Layer2Row> _rows = new List<Layer2Row>();

        private static UILabel _headerLabel;
        private static UILabel _tsunamiLabel;
        private static UILabel _tsunamiNoteLabel;
        private static UILabel _longPeriodLabel;
        private static UILabel _timeLabel;
        private static UILabel _timeNoteLabel;

        private static bool _built;
        private static bool _tsunamiVisible;
        private static bool _longPeriodVisible;
        private static float _sectionTop;
        private static float _sectionHeight;

        /// <summary>The y where the section starts (i.e. layer 1's bottom edge). Used to compute the panel's height.</summary>
        internal static float SectionTop { get { return _sectionTop; } }

        /// <summary>The height the section currently occupies. 0 when every block is folded away.</summary>
        internal static float VisibleHeight { get { return _sectionHeight; } }

        /// <summary>
        /// Once, when the panel is built. **The rows are always created** (the settings
        /// change at runtime, and creating them and hiding them is simpler and less
        /// fragile than having machinery to create them later).
        /// <paramref name="y"/> advances only by what is actually visible.
        /// </summary>
        internal static void Build(UIPanel root, ref float y)
        {
            _sectionTop = y;
            _rows.Clear();

            float t = y;
            float before = t;

            _headerLabel = EarthquakeRows.AddSectionHeader(root, "Layer2Header", ref t,
                Strings.EarthquakeLayer2Header);
            Record(_headerLabel, BlockHeader, t - before);

            // ★ **Reserve two rows' worth of height (i.e. let it wrap).** One row would
            //    do for the expected-time line alone, but the same label also carries
            //    "there is not enough open sea at the map's edge…" (about 110 characters
            //    in English). Put that in a non-wrapping row and it **gets cut off and
            //    disappears** — and what disappears is the sentence explaining that this
            //    is not a failure, which is the last thing you want cut.
            before = t;
            _tsunamiLabel = EarthquakeRows.AddLayer2Row(root, "Tsunami", ref t, 42f);
            Record(_tsunamiLabel, BlockTsunami, t - before);

            // A permanent explanation of where the wave comes from. **It is not a
            // reading**, so it gets no prefix (treated like
            // Strings.EarthquakeSensorEffect / EarthquakeOverlayLegend).
            // It is only shown when a wave is scheduled or has been raised — leave "the
            // wave will arrive from…" up when we know no wave is coming (NoSea, or no
            // DLC) and we would be explaining the arrival direction of a wave that never
            // arrives.
            before = t;
            _tsunamiNoteLabel = EarthquakeRows.AddPlainRow(root, "TsunamiNote", ref t, "", 56f);
            Record(_tsunamiNoteLabel, BlockTsunami, t - before);

            // Long-period ground motion. Reported for the one building under the cursor.
            //
            // ★ **Reserve three to four rows' worth of height.** 42f was enough for the
            //    body alone (about 110 characters including the prefix), but layer-2
            //    review I1 / M3 added caveats on the end ("not applied until the main
            //    shock", "the sweep was cut off at its cap"). Put that in a non-wrapping
            //    height and it **gets cut off and disappears** — and what disappears is
            //    "this number is not in effect yet", the last thing you want cut.
            before = t;
            _longPeriodLabel = EarthquakeRows.AddLayer2Row(root, "LongPeriod", ref t, 72f);
            Record(_longPeriodLabel, BlockLongPeriod, t - before);

            // **A permanent note.** Without it, "the taller it is, the more likely it is
            // to fall" looks like how the game works.
            before = t;

            // The time-of-day factor (Task 11). **No setting of its own** — the only
            // thing it multiplies is the long-period extra damage, so it goes in the same
            // block as long-period and comes and goes with it.
            before = t;
            _timeLabel = EarthquakeRows.AddLayer2Row(root, "TimeOfDay", ref t);
            Record(_timeLabel, BlockLongPeriod, t - before);

            // A note shown only when the day/night cycle is switched off. **The factor
            // quietly sitting at 1.00 with no explanation is exactly the kind of output
            // this mod hates most** (§F-1).
            before = t;
            _timeNoteLabel = EarthquakeRows.AddPlainRow(root, "TimeOfDayNote", ref t, "", 40f);
            Record(_timeNoteLabel, BlockLongPeriod, t - before);

            _built = true;
            _tsunamiVisible = TsunamiRowWanted;
            _longPeriodVisible = ModSettings.EarthquakeLongPeriodStrength.value > 0;
            Layout();

            y = _sectionTop + _sectionHeight;
        }

        /// <summary>
        /// Records a row and how far it advances y. <see cref="Layout"/> rebuilds the
        /// positions from nothing but this order and these steps, so **the order rows are
        /// added in is the order they appear on screen**.
        /// </summary>
        private static void Record(UILabel label, int block, float step)
        {
            _rows.Add(new Layer2Row { Label = label, Step = step, Block = block });
        }

        /// <summary>On level unload. Just drop the references (the objects go with the panel).</summary>
        internal static void Destroy()
        {
            _rows.Clear();
            _headerLabel = null;
            _tsunamiLabel = null;
            _tsunamiNoteLabel = null;
            _longPeriodLabel = null;
            _timeLabel = null;
            _timeNoteLabel = null;
            _built = false;
            _tsunamiVisible = false;
            _longPeriodVisible = false;
            _sectionTop = 0f;
            _sectionHeight = 0f;
        }

        /// <summary>
        /// Every frame, from the main thread. If <paramref name="snapshot"/> is null or
        /// invalid, only the value rows are blanked (the section itself is decided by the
        /// settings).
        /// </summary>
        internal static void Refresh(EarthquakeSnapshot snapshot)
        {
            if (!_built) return;

            bool tsunami = TsunamiRowWanted;
            bool longPeriod = ModSettings.EarthquakeLongPeriodStrength.value > 0;
            if (tsunami != _tsunamiVisible || longPeriod != _longPeriodVisible)
            {
                _tsunamiVisible = tsunami;
                _longPeriodVisible = longPeriod;
                Layout();
                // Only on the frame where the height changed. Do not rewrite the
                // positions every frame.
                EarthquakePanel.Relayout();
            }

            bool valid = snapshot != null && snapshot.Valid;

            if (tsunami)
            {
                if (valid) RefreshTsunamiRow(snapshot);
                else
                {
                    EarthquakeRows.SetPlain(_tsunamiLabel, "");
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");
                }
            }

            if (longPeriod)
            {
                string text = valid ? EarthquakeLongPeriodText.CursorRow(snapshot) : null;
                EarthquakeRows.SetPlain(_longPeriodLabel, "");
                if (text != null) EarthquakeRows.SetLayer2(_longPeriodLabel, text);

                // The time-of-day row is shown **even with no earthquake**. The factor is
                // decided by the city's clock alone and has nothing to do with whether a
                // quake is happening. This lets somebody who has switched off the
                // day/night cycle check at any time why it stays at 1.00.
                string time = valid ? EarthquakeLongPeriodText.TimeRow(snapshot) : null;
                EarthquakeRows.SetPlain(_timeLabel, "");
                if (time != null) EarthquakeRows.SetLayer2(_timeLabel, time);
                EarthquakeRows.SetPlain(_timeNoteLabel,
                    EarthquakeLongPeriodText.TimeNote(snapshot));
            }
        }

        /// <summary>
        /// Repacks, from the top, only the rows of the visible blocks.
        /// Called **only at construction and on the frame where an on/off changed** (see
        /// the class doc).
        /// </summary>
        private static void Layout()
        {
            bool any = _tsunamiVisible || _longPeriodVisible;
            float y = _sectionTop;

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Label == null) continue;

                bool visible = IsBlockVisible(row.Block, any);
                row.Label.isVisible = visible;
                if (!visible) continue;

                // Move the position only. **Neither label creation nor .text assignment
                // happens here** (so as not to break the guarantee that those two live
                // only inside EarthquakeRows).
                var p = row.Label.relativePosition;
                row.Label.relativePosition = new Vector3(p.x, y);
                y += row.Step;
            }

            _sectionHeight = y - _sectionTop;
        }

        private static bool IsBlockVisible(int block, bool any)
        {
            if (block == BlockHeader) return any;
            if (block == BlockTsunami) return _tsunamiVisible;
            if (block == BlockLongPeriod) return _longPeriodVisible;
            return false;
        }

        /// <summary>
        /// One row per state. **Never write "the wave spreads from the hypocentre"**
        /// (see the class doc).
        ///
        /// <c>NoSea</c> is **not a failure**. On an inland map the sea-side edge stretch
        /// is under 10 cells and nothing happening is the correct outcome (§B-3). So we
        /// state the reason and leave it at that. <c>Failed</c> (the disaster slots being
        /// full, say) is the only state with no row — there is nothing the player can do
        /// about it, and the cause only means anything in the diagnostic dump.
        /// </summary>
        private static void RefreshTsunamiRow(EarthquakeSnapshot snapshot)
        {
            // The note is only attached when a wave is actually coming (cleared by default).
            EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");

            switch (snapshot.TsunamiState)
            {
                case TsunamiChainState.Scheduled:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, PendingText(snapshot));
                    return;

                case TsunamiChainState.Raised:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeTsunamiRaised
                        + RaisedProgress(true) + "   (#" + snapshot.TsunamiQuakeId + ")");
                    return;

                case TsunamiChainState.NoSea:
                    // ★★ **A different line for each reason.** (2026-08-31,
                    //    cross-checked) Saying "this is an inland map" whatever the
                    //    reason was sending somebody refused out in deep water in
                    //    precisely the wrong direction.
                    EarthquakeRows.SetLayer2(_tsunamiLabel, TsunamiRefusalText());
                    return;

                case TsunamiChainState.NoDlc:
                    // Do not add new keys (plan Step 5).
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeNeedsDlc);
                    return;

                default:
                    // ★★ **Right after a trench quake is triggered, say that we are
                    //    waiting.** (2026-08-30, fourth round of verification)
                    //    A trench quake stays Idle for the whole of Emerging, and that
                    //    lasts <b>2 minutes 17 seconds from the click</b>. Leave the row
                    //    blank through that and nothing appears on screen — somebody
                    //    waiting reads that as "it is broken" and clicks again.
                    //    **A blank is not an answer.**
                    if (TrenchQuakeSlot.LastId != 0)
                    {
                        EarthquakeRows.SetPlain(_tsunamiNoteLabel,
                            Strings.EarthquakeTsunamiFromShore);

                        // ★★ **Never put a wave that has already been raised back to
                        //    "expected".** (fifth round of verification) LastId survives
                        //    after the chain stops watching, so writing this naively
                        //    <b>looks like going backwards</b>, from Raised to expected.
                        // ★★ **"Raised" if it is running, "expected" if not.**
                        //    (2026-08-31, fifth round of verification) This used to be
                        //    chosen by <c>StillOwes</c>, but that is also true during the
                        //    <b>2 minutes 17 seconds of Emerging when nothing has been
                        //    raised yet</b>, producing the self-contradictory line
                        //    "expected" with "the wave is travelling" next to it.
                        // ★★ **Once raised, never go back to "expected".**
                        //    (2026-08-31, sixth round of verification) <c>StillOwes</c>
                        //    stays true after the earthquake's phase has ended, so
                        //    choosing on it alone <b>looks like winding back from raised
                        //    to expected</b>.
                        bool live = TsunamiRing.Running;
                        bool done = TsunamiChain.HasRaisedFor(TrenchQuakeSlot.LastId);

                        EarthquakeRows.SetLayer2(_tsunamiLabel,
                            (live || done ? Strings.EarthquakeTsunamiRaised
                                          : Strings.EarthquakeTsunamiSoon)
                            + RaisedProgress(live || done)
                            + "   (#" + TrenchQuakeSlot.LastId + ")");
                        return;
                    }

                    // ★★ **Never put out a blank row.** (fifth round of verification)
                    //    Now that Running has been added to TsunamiRowWanted, we fall
                    //    through to here while the wave is still travelling after the
                    //    earthquake's slot has been freed. Blank it and there is an empty
                    //    gap under the heading.
                    if (TsunamiRing.Running)
                    {
                        EarthquakeRows.SetLayer2(_tsunamiLabel,
                            Strings.EarthquakeTsunamiRaised + RaisedProgress(true));
                        return;
                    }

                    // Idle (including an inland hypocentre) and Failed. No row.
                    EarthquakeRows.SetPlain(_tsunamiLabel, "");
                    return;
            }
        }

        /// <summary>The line matching the reason we were refused (<see cref="TsunamiRing.Refusal"/>).</summary>
        private static string TsunamiRefusalText()
        {
            switch (TsunamiRing.LastRefusal)
            {
                case TsunamiRing.Refusal.NotEnoughRoom:
                    return Strings.EarthquakeTsunamiNoRoom;

                case TsunamiRing.Refusal.Busy:
                    return Strings.EarthquakeTsunamiBusy;

                case TsunamiRing.Refusal.TooWeak:
                    return Strings.EarthquakeTsunamiTooWeak;

                case TsunamiRing.Refusal.NoRoomInGame:
                    return Strings.EarthquakeTsunamiNoSlot;

                case TsunamiRing.Refusal.NotSea:
                default:
                    return Strings.EarthquakeTsunamiNoSea;
            }
        }

        /// <summary>
        /// The <b>moving numbers</b> shown while the wave is being generated.
        ///
        /// ★★ **A line that does not move is read as "it is broken".** (2026-08-31,
        ///   cross-checked) The source runs for 768 water steps, about 14 real minutes,
        ///   and the wave then takes roughly another 10 to arrive. If the screen says
        ///   "tsunami raised" and never changes throughout, it looks frozen to somebody
        ///   waiting — the same hole we fell into once with <c>Idle</c>'s blank row (see
        ///   the comment in the default arm below).
        ///
        ///   What we show is <b>how far the sea is being lifted right now</b> and
        ///   <b>how far along it is</b>. Once the source has finished we say only that
        ///   the wave is travelling — the arrival time depends on the terrain, so we
        ///   print no number that would be a lie.
        /// </summary>
        /// <param name="raised">
        /// Whether the wave has already been raised. **When false, nothing is appended** —
        /// printing "the wave is travelling" while nothing has been raised yet
        /// contradicts the "in N minutes" immediately to its left (2026-08-31, fifth
        /// round of verification).
        /// </param>
        private static string RaisedProgress(bool raised)
        {
            if (!raised) return "";
            if (!TsunamiRing.Running) return "  " + Strings.EarthquakeTsunamiTravelling;

            int total = TsunamiRing.TotalSteps;
            if (total <= 0) return "";

            int done = TsunamiRing.ElapsedSteps;
            if (done > total) done = total;

            // ★★ **Give the time remaining in real time.** (2026-08-31, fourth round of
            //    verification) The "in N minutes" on the row above is in game minutes
            //    (about 4 real seconds), yet it takes over 10 real minutes before the
            //    water actually moves.
            //    One water step = 64 sim frames ≈ 1.07 real seconds.
            float leftMinutes = (total - done) * 64f / 3600f;

            return "  " + (done * 100 / total) + "%  "
                   + (TsunamiRing.OffsetMetres >= 0f ? "+" : "")
                   + TsunamiRing.OffsetMetres.ToString("F0") + " m  "
                   + Strings.EarthquakeTsunamiSourceLeft + " "
                   + leftMinutes.ToString("F0") + " " + Strings.EarthquakeRealMinutes;
        }

        /// <summary>
        /// The time remaining. **Always derive the conversion from
        /// <see cref="FeatureHost.FramesPerMinute"/>** (we once hard-coded the constant
        /// and came out a factor of 4 wrong). When the conversion is unavailable, or the
        /// due time has already passed, say only "expected" instead of a number — never
        /// print a negative time remaining.
        /// </summary>
        private static string PendingText(EarthquakeSnapshot snapshot)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f || snapshot.TsunamiDueFrame <= snapshot.CurrentFrame)
            {
                // ★★ **With no number, do not say "in N minutes".** (sixth round of
                //    verification) The English "Tsunami expected in" is not a sentence
                //    without a number after it, and the screen was showing
                //    "Tsunami expected in   (#12)".
                return Strings.EarthquakeTsunamiSoon + "   (#" + snapshot.TsunamiQuakeId + ")";
            }

            float minutes = (snapshot.TsunamiDueFrame - snapshot.CurrentFrame) / framesPerMinute;
            return Strings.EarthquakeTsunamiPending + ": " + minutes.ToString("F0") + " "
                   + Strings.EarthquakeMinutes + "   (#" + snapshot.TsunamiQuakeId + ")";
        }
    }
}
