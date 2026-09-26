using ColossalFramework.UI;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The "what the typhoon brings" section — the rows that show how each element is
    /// behaving right now. **Main thread only.**
    ///
    /// **T7 to T10 add rows to this file.** T7 is wind damage, T8 river flooding, T9 the
    /// clouds and T10 the accompanying tornadoes. **Split it once it passes 800 lines**
    /// (one file per element, leaving this file holding only the order they appear in).
    ///
    /// ── No per-row markers here either (④'s display convention) ──────────
    ///
    /// The numbers in this section come from the ledger ④ keeps itself, and from ceilings
    /// estimated from vanilla's formulae. **None of them is "a value the game calculated
    /// and published".** The provenance is named by the <c>[measured]</c> marker that
    /// goes on the rows it affects, so here we use nothing but
    /// <see cref="TyphoonRows.AddRow(UIPanel,string,ref float)"/> and
    /// <see cref="TyphoonRows.SetPlain"/>.
    /// **Do not call <see cref="TyphoonRows.SetMeasured"/> from this file.**
    ///
    /// ── Show the numbers even when they are 0 ────────────────────────────
    ///
    /// ③ made the mistake of "you could not tell from the diagnostics at all whether
    /// fire spread was running". Whether lightning struck 0 times, or we never scattered
    /// any in the first place, or it was thrown away against a ceiling, all look
    /// identical on screen (nothing happens), so **while a typhoon is running we always
    /// show all four numbers**.
    /// </summary>
    internal static class TyphoonEffectRows
    {
        private static UILabel _lightningLabel;

        /// <summary>A row shown only while we are yielding the whole budget to the host
        /// storm (whole-project review I4).</summary>
        private static UILabel _lightningYieldedLabel;
        private static UILabel _windLabel;
        private static UILabel _floodLabel;
        private static UILabel _floodReasonLabel;
        private static UILabel _gustLabel;
        private static UILabel _cloudNoteLabel;

        /// <summary>Once, when the panel is built.</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // ★★ **The permanent explanations have been taken out** (2026-08-22, at the
            //    owner's request: "I think the many explanations on the typhoon tab are
            //    not needed either"). The content lives in the diagnostics dump in
            //    <c>TyphoonFeature.WriteDiagnostics</c>. What is left is only **the
            //    current state** and **the "why is this not happening" rows**, and the
            //    latter only take up space when it really is not happening.

            TyphoonRows.AddSectionHeader(p, "EffectsHeader", ref y, Strings.TyphoonEffectsHeader);

            _lightningLabel = TyphoonRows.AddRow(p, "Lightning", ref y);

            // ★ The row that says "④ has not produced a single lightning strike".
            //    **Refresh puts the text in and takes it out** — make it permanent and it
            //    reads as "we are yielding" even at an ordinary intensity where we are
            //    not. The height reserves room for wrapping onto four lines.
            _lightningYieldedLabel = TyphoonRows.AddRow(p, "LightningYielded", ref y, 72f);

            _windLabel = TyphoonRows.AddRow(p, "Wind", ref y);

            _floodLabel = TyphoonRows.AddRow(p, "Flood", ref y);

            // The "why did it not flood" row. **The text goes in and out depending on
            // the state** (design doc §7.4). The height reserves room for the explanation
            // to wrap onto three lines.
            _floodReasonLabel = TyphoonRows.AddRow(p, "FloodReason", ref y, 56f);

            _gustLabel = TyphoonRows.AddRow(p, "Gust", ref y);

            // ★ The clouds get no row of their own (you can see whether they are there by
            //    looking at the screen). We show only **the reason they are not** — the
            //    vanilla sky's cloud settings being absent in this environment is a
            //    legitimate state (§C-2, PARTIAL), and staying quiet about it reads as
            //    "④'s clouds are broken".
            _cloudNoteLabel = TyphoonRows.AddRow(p, "CloudNote", ref y, 40f);
        }

        /// <summary>Every frame while the panel is showing. <paramref name="s"/> may be
        /// null.</summary>
        internal static void Refresh(TyphoonSnapshot s)
        {
            if (s == null || !s.Valid || !s.Active)
            {
                // Do not line up zeroes when there is no typhoon ("we never scattered
                // any" and "there were 0 of them" are different things).
                TyphoonRows.SetPlain(_lightningLabel, "");
                TyphoonRows.SetPlain(_lightningYieldedLabel, "");
                TyphoonRows.SetPlain(_windLabel, "");
                TyphoonRows.SetPlain(_floodLabel, "");
                TyphoonRows.SetPlain(_floodReasonLabel, "");
                TyphoonRows.SetPlain(_gustLabel, "");
                TyphoonRows.SetPlain(_cloudNoteLabel, "");
                return;
            }

            // The order is the one Strings.TyphoonLightningRow names in words:
            // in flight / cumulative / how many are left to the host storm / rejected.
            TyphoonRows.SetPlain(_lightningLabel,
                Strings.TyphoonLightningRow + ": "
                + s.LightningInFlight + " / " + s.LightningTotal + " / "
                + s.LightningVanillaReserve + " / " + s.LightningRejected);

            // ★ Name the case "we are yielding everything to the host and ④ has not fired
            //    a single bolt" (whole-project review I4). The test looks only at **the
            //    host's share**, not at the stock on hand (which can be 0 temporarily) —
            //    the former comes back on the next tick, the latter does not come back
            //    until you lower the intensity. The distinction is in the doc on
            //    LightningBudget.YieldsCompletely.
            TyphoonRows.SetPlain(_lightningYieldedLabel,
                LightningBudget.YieldsCompletely(s.LightningVanillaReserve)
                    ? Strings.TyphoonLightningYielded
                    : "");

            RefreshWind(s);
            RefreshFlood(s);
            RefreshGust(s);
            RefreshCloud();
        }

        /// <summary>
        /// The cloud row (T9). **When they are showing it says nothing** — you can see
        /// that by looking at the sky. It gives a reason only when the vanilla sky cloud
        /// boost is unavailable in this environment
        /// (<c>DayNightDynamicCloudsProperties</c> does not exist under some DLC and
        /// graphics settings. §C-2, PARTIAL. **This is not a fault**).
        /// </summary>
        private static void RefreshCloud()
        {
            // ★ Treat the vortex being on screen the same way whether it is drawn with
            //   "puffs" or with the fallback mesh. All this row names is that the vanilla
            //   sky boost is unavailable; which of the two paths is drawing the vortex is
            //   the diagnostics dump's business.
            bool drawing = TyphoonCloud.State == TyphoonCloudState.Puffs
                           || TyphoonCloud.State == TyphoonCloudState.Drawing;

            bool unavailable = ModSettings.TyphoonCloudEnabled.value
                               && ModSettings.TyphoonVanillaCloudBoost.value
                               && drawing
                               && !TyphoonCloud.VanillaBoostApplied;

            TyphoonRows.SetPlain(_cloudNoteLabel,
                unavailable ? Strings.TyphoonCloudUnavailable : "");
        }

        /// <summary>
        /// The row for tornado-grade local damage. **Not one actual tornado is
        /// created.**
        ///
        /// Having <c>0</c> alive is normal (patches are born with gaps between them), so
        /// the cumulative collapse count is what lets you tell "there are none right now"
        /// from "the feature is dead".
        /// </summary>
        private static void RefreshGust(TyphoonSnapshot s)
        {
            if (ModSettings.TyphoonGustStrength.value <= 0
                || ModSettings.TyphoonGustStrength.value <= 0)
            {
                TyphoonRows.SetPlain(_gustLabel, Strings.TyphoonGustRow + ": off");
                return;
            }

            // The order is the one Strings.TyphoonGustRow names in words:
            // patches alive now / collapses in the last sweep / cumulative / buildings
            // the game refused.
            TyphoonRows.SetPlain(_gustLabel,
                Strings.TyphoonGustRow + ": "
                + s.GustActive + " / " + s.GustLastCollapsed + " / "
                + s.GustTotalCollapsed + " / " + s.GustLastRefused);
        }

        /// <summary>
        /// The river flooding row (the five states in design doc §7.4).
        ///
        /// **Giving a reason in the <c>NoSources</c> case is a requirement of this
        /// feature.** If the map in question has no natural water sources then nothing
        /// happening is correct, so leaving the row blank reads as "it is broken".
        /// </summary>
        private static void RefreshFlood(TyphoonSnapshot s)
        {
            if (ModSettings.TyphoonFloodStrength.value <= 0
                || ModSettings.TyphoonFloodStrength.value <= 0)
            {
                TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": off");
                TyphoonRows.SetPlain(_floodReasonLabel, "");
                return;
            }

            switch (s.FloodState)
            {
                case TyphoonFloodState.NoSources:
                    TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": -");
                    // ★ Say "why it is not happening". This is not a fault (design doc
                    //   §7.4).
                    TyphoonRows.SetPlain(_floodReasonLabel, Strings.TyphoonFloodNoSources);
                    return;

                case TyphoonFloodState.Raised:
                    TyphoonRows.SetPlain(_floodLabel,
                        Strings.TyphoonFloodRow + ": " + Strings.TyphoonFloodRaised + " "
                        + s.FloodTouched + " / " + s.FloodNaturalSources
                        + "   +" + s.FloodPeakRiseMetres.ToString("F1") + " m");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;

                case TyphoonFloodState.Restored:
                    TyphoonRows.SetPlain(_floodLabel, Strings.TyphoonFloodRow + ": -");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;

                default:
                    // Idle (the gale radius has not reached anything yet) and Failed (the
                    // reason goes to the diagnostics) both show no row at all.
                    TyphoonRows.SetPlain(_floodLabel, "");
                    TyphoonRows.SetPlain(_floodReasonLabel, "");
                    return;
            }
        }

        /// <summary>
        /// The wind damage row. **It shows the numbers even when 0 collapsed** — so that
        /// "it is not working" and "there are no buildings nearby" can be told apart on
        /// screen (<c>scanned</c> is the clue).
        ///
        /// When the setting is off it does not line up numbers but says it is off
        /// (a line of zeroes reads as "it is running and yet not one building falls").
        /// </summary>
        private static void RefreshWind(TyphoonSnapshot s)
        {
            if (ModSettings.TyphoonWindStrength.value <= 0
                || ModSettings.TyphoonWindStrength.value <= 0)
            {
                TyphoonRows.SetPlain(_windLabel, Strings.TyphoonWindRow + ": off");
                return;
            }

            // The order is the one Strings.TyphoonWindRow names in words:
            // collapses in the last sweep / cumulative / buildings examined / buildings
            // the game refused.
            string text = Strings.TyphoonWindRow + ": "
                + s.WindLastCollapsed + " / " + s.WindTotalCollapsed + " / "
                + s.WindLastScanned + " / " + s.WindLastRefused
                // ★ Which side the dangerous semicircle is on. **The player cannot tell
                //   if we have left and right the wrong way round**, so show it the whole
                //   time wind damage is running.
                + "   " + (ModSettings.TyphoonSouthernHemisphere.value
                    ? Strings.TyphoonDangerousSideLeft
                    : Strings.TyphoonDangerousSideRight);

            // Do not quietly hide the fact that the outer rim has not been checked yet
            // (this happens in very large cities).
            if (s.WindLastCapped) text += "   " + Strings.TyphoonWindCapped;

            TyphoonRows.SetPlain(_windLabel, text);
        }

        /// <summary>
        /// On level unload. **Only drops the references** (the objects themselves go with
        /// the panel's GameObject).
        /// </summary>
        internal static void Destroy()
        {
            _lightningLabel = null;
            _lightningYieldedLabel = null;
            _windLabel = null;
            _floodLabel = null;
            _floodReasonLabel = null;
            _gustLabel = null;
            _cloudNoteLabel = null;
        }
    }
}
