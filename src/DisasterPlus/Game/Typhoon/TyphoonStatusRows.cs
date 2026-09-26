using ColossalFramework.UI;
using DisasterPlus.Core.Typhoon;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The rows showing the state of the typhoon itself (position, heading, strength,
    /// radii, phase and the landfall forecast, plus vanilla's rainfall and cloud cover).
    /// **Main thread only.**
    ///
    /// Both creating rows and putting text into them go through
    /// <see cref="TyphoonRows"/>. **There is not a single <c>UILabel</c> creation or
    /// <c>.text</c> assignment in this file.**
    ///
    /// ── The four promises kept here (design doc §7) ───────────────────────
    ///
    /// 1. **No per-row provenance markers.** As a rule all of ④'s numbers are this mod's
    ///    own, and the two heading lines were taken out on 2026-08-22
    ///    (<see cref="TyphoonPanel"/>'s doc). The <b>one exception</b> is the rainfall and
    ///    cloud rows, and those alone go through
    ///    <see cref="TyphoonRows.SetMeasured"/>. **There are only two
    ///    <c>SetMeasured</c> calls in this file.**
    /// 2. **"Minutes to landfall" may be shown.** ④ holds the track deterministically, so
    ///    it is a certain value (①'s weather forecast panel judges occurrence with a
    ///    random draw, so it cannot show the same thing). <b><c>TyphoonLandfallNote</c>
    ///    always spells out that difference alongside it.</b>
    /// 3. **Do not show m/s.** ④'s "strength" is shown only as the 0-255 intensity and an
    ///    ASCII bar. That is why <see cref="TyphoonProfile"/> has no method returning
    ///    m/s, and do not make one here either.
    /// 4. **Do not turn unreadable values into numbers.** When the weather cannot be read,
    ///    rainfall and cloud cover are "cannot be read", not "0", and in that case they
    ///    get no <c>[measured]</c> marker either (we do not claim a value we could not
    ///    read as one of vanilla's measurements).
    /// </summary>
    internal static class TyphoonStatusRows
    {
        /// <summary>rad → deg.</summary>
        private const float DegreesPerRadian = 57.29578f;

        /// <summary>Maximum intensity. Used as the bar's denominator
        /// (<c>DisasterData.m_intensity</c> is a byte).</summary>
        private const float MaxIntensity = 255f;

        private static UILabel _stateLabel;
        private static UILabel _centreLabel;
        private static UILabel _strengthLabel;
        private static UILabel _radiusLabel;
        private static UILabel _phaseLabel;
        private static UILabel _landfallLabel;
        private static UILabel _rainLabel;
        private static UILabel _cloudLabel;

        /// <summary>Once, when the panel is built. The rows always exist; what varies is
        /// whether there is anything in them.</summary>
        internal static void Build(UIPanel p, ref float y)
        {
            // "No typhoon", "not read yet", "cannot be read" and "why it could not be
            // raised" all come out here. **Reserve enough height to wrap** — the refusal
            // is a sentence of English, and in a non-wrapping row it gets cut off and the
            // reason disappears.
            // ★★ **The permanent explanations have been taken out** (2026-08-22, at the
            //    owner's request: "I think the many explanations on the typhoon tab are
            //    not needed either"). The content lives in the diagnostics dump in
            //    <c>TyphoonFeature.WriteDiagnostics</c>. What is left is only **the
            //    current state** and **the "why is this not happening" rows**, and the
            //    latter only take up space when it really is not happening.

            _stateLabel = TyphoonRows.AddRow(p, "State", ref y, 40f);

            _centreLabel = TyphoonRows.AddRow(p, "Centre", ref y);
            _strengthLabel = TyphoonRows.AddRow(p, "CoreStrength", ref y);
            _radiusLabel = TyphoonRows.AddRow(p, "Radius", ref y);
            _phaseLabel = TyphoonRows.AddRow(p, "Phase", ref y);
            _landfallLabel = TyphoonRows.AddRow(p, "Landfall", ref y);

            // ★ These two rows, and only these, are vanilla measurements.
            _rainLabel = TyphoonRows.AddMeasuredRow(p, "Rain", ref y);
            _cloudLabel = TyphoonRows.AddMeasuredRow(p, "Cloud", ref y);
        }

        /// <summary>Every frame while the panel is showing. <paramref name="s"/> may be
        /// null.</summary>
        internal static void Refresh(TyphoonSnapshot s)
        {
            if (s == null)
            {
                // Do not give "we have not read once yet" and "we read and could not read
                // it" the same wording (a discipline ① and ② established). The former
                // happens quite normally if you stay paused right after a load.
                TyphoonRows.SetPlain(_stateLabel, Strings.TyphoonWaiting);
                ClearTyphoonRows();
                ClearWeatherRows();
                return;
            }

            if (!s.Valid)
            {
                TyphoonRows.SetPlain(_stateLabel, Strings.TyphoonUnavailable);
                ClearTyphoonRows();
                ClearWeatherRows();
                return;
            }

            // The weather can be read whether or not there is a typhoon. Show it even
            // when there is none.
            RefreshWeatherRows(s);

            if (!s.Active)
            {
                TyphoonRows.SetPlain(_stateLabel, InactiveText(s));
                ClearTyphoonRows();
                return;
            }

            TyphoonRows.SetPlain(_stateLabel, "");
            RefreshTyphoonRows(s);
        }

        /// <summary>
        /// The one line shown when there is no typhoon. **It makes "nothing is happening"
        /// and "it could not be raised" tellable apart.**
        ///
        /// The priority order is "the placement cursor is armed (waiting for a point)" >
        /// "the request has just been made (waiting for the next sim tick)" > "the prefab
        /// cannot be read (i.e. not one can be raised in this environment)" > "refused,
        /// with a reason" > "simply not happening". We look at
        /// <see cref="TyphoonHub.PendingRequest"/> **to prevent the misunderstanding
        /// where a double press raises two**; by design there is a one-tick delay between
        /// the request and the typhoon appearing (plan §5.4).
        /// </summary>
        private static string InactiveText(TyphoonSnapshot s)
        {
            // ★ While the cursor is armed, "click the map". Without this the panel still
            //   reads "no typhoon" right after the tile is pressed, and **the player
            //   presses the tile again before pointing anywhere**.
            if (TyphoonPlacementTool.IsActive) return Strings.TyphoonPlaceHint;

            if (TyphoonHub.PendingRequest.Kind != TyphoonRequest.None) return Strings.TyphoonWaiting;

            // Design doc §6: if it cannot be read, do nothing rather than guess.
            // **Do not hide that fact.**
            if (!s.Prefab.Usable) return Strings.TyphoonPrefabUnreadable;

            // The refusal is one sentence of diagnostic English
            // (TyphoonSnapshot.Refusal). There is no translation for it, but not showing
            // it is worse — it is the only clue to "why it could not be raised".
            if (!string.IsNullOrEmpty(s.Refusal))
            {
                return Strings.TyphoonInactive + "  (" + s.Refusal + ")";
            }

            return Strings.TyphoonInactive;
        }

        private static void RefreshTyphoonRows(TyphoonSnapshot s)
        {
            TyphoonRows.SetPlain(_centreLabel,
                Strings.TyphoonCentre + ": (" + s.Centre.X.ToString("F0")
                + ", " + s.Centre.Z.ToString("F0") + ")    "
                // The degree sign is a language-independent symbol, so we do not make a
                // key for it (writing "deg" would leave a single English word on a
                // Japanese screen).
                + Strings.TyphoonHeading + ": " + DegreesOf(s.HeadingRadians).ToString("F0") + "°");

            // ★ **Do not show m/s** (design doc §7-3). Only the 0-255 intensity and an
            //   ASCII bar. The bar reuses TyphoonProfile.BarOf (ten steps, fixed ASCII)
            //   against the intensity as a fraction. That is just a way of drawing [0,1]
            //   as a ten-step bar; it brings no notion of wind speed with it.
            TyphoonRows.SetPlain(_strengthLabel,
                Strings.TyphoonCoreStrength + ": " + s.Intensity + " / 255    "
                + TyphoonProfile.BarOf(s.Intensity / MaxIntensity));

            // The radii come from the prefab values. If they could not be read they are
            // 0, but in that case there is no typhoon in the first place
            // (TyphoonPrefabFacts.Usable). To be safe, do not show a 0 as "radius 0 m".
            if (s.StormRadius > 0f)
            {
                TyphoonRows.SetPlain(_radiusLabel,
                    Strings.TyphoonStormRadius + ": " + s.StormRadius.ToString("F0") + " m    "
                    + Strings.TyphoonGaleRadius + ": " + s.GaleRadius.ToString("F0") + " m");
            }
            else
            {
                TyphoonRows.SetPlain(_radiusLabel,
                    Strings.TyphoonStormRadius + ": " + Strings.TyphoonUnavailable);
            }

            string phase = PhaseWordOf(s.Phase);
            TyphoonRows.SetPlain(_phaseLabel,
                phase == null ? "" : Strings.TyphoonPhaseLabel + ": " + phase);

            RefreshLandfallRows(s);
        }

        /// <summary>
        /// The landfall forecast. **Do not mix the three states** (design doc §7-2 /
        /// §7-4): already over land / so many minutes away / it will pass out at sea.
        /// Express the last two as "0 minutes" and two completely different states end up
        /// looking identical.
        /// </summary>
        private static void RefreshLandfallRows(TyphoonSnapshot s)
        {
            string body;
            if (s.OverLand) body = Strings.TyphoonLandfallNow;
            else if (s.LandfallKnown)
            {
                // ★ F0. The estimate of the landfall frame is only ever stepped in
                //   256-frame units (about 5.6 in-game minutes), so 0.1-minute precision
                //   is precision we do not have (whole-project review; the doc on
                //   TyphoonController.LandfallStepFrames).
                body = s.MinutesToLandfall.ToString("F0") + " " + Strings.TyphoonMinutes;
            }
            else body = Strings.TyphoonNoLandfall;

            TyphoonRows.SetPlain(_landfallLabel, Strings.TyphoonLandfall + ": " + body);
        }

        /// <summary>
        /// **The two rows in ④ allowed to claim <c>[measured]</c>.** This is the only
        /// place that calls <see cref="TyphoonRows.SetMeasured"/>.
        ///
        /// When it cannot be read we <b>do not attach <c>[measured]</c></b> — that would
        /// be claiming a value we failed to read as one of vanilla's measurements.
        /// </summary>
        private static void RefreshWeatherRows(TyphoonSnapshot s)
        {
            if (!s.WeatherReadable)
            {
                TyphoonRows.SetPlain(_rainLabel,
                    Strings.TyphoonRainRow + ": " + Strings.TyphoonUnavailable);
                TyphoonRows.SetPlain(_cloudLabel,
                    Strings.TyphoonCloudRow + ": " + Strings.TyphoonUnavailable);
                return;
            }

            TyphoonRows.SetMeasured(_rainLabel,
                Strings.TyphoonRainRow + ": " + s.Rain.ToString("F2"));
            TyphoonRows.SetMeasured(_cloudLabel,
                Strings.TyphoonCloudRow + ": " + s.Cloud.ToString("F2"));
        }

        /// <summary>
        /// No word is assigned to <see cref="TyphoonPhase.Idle"/>. Give it a name and it
        /// looks like "one of the phases in progress" (the same decision as ②'s
        /// <c>RefreshPhaseRow</c>).
        /// </summary>
        private static string PhaseWordOf(TyphoonPhase phase)
        {
            switch (phase)
            {
                case TyphoonPhase.Approaching: return Strings.TyphoonPhaseApproaching;
                case TyphoonPhase.Peak: return Strings.TyphoonPhasePeak;
                case TyphoonPhase.Passing: return Strings.TyphoonPhasePassing;
                case TyphoonPhase.Gone: return Strings.TyphoonPhaseGone;
                default: return null;
            }
        }

        private static float DegreesOf(float radians)
        {
            return radians * DegreesPerRadian;
        }

        private static void ClearTyphoonRows()
        {
            TyphoonRows.SetPlain(_centreLabel, "");
            TyphoonRows.SetPlain(_strengthLabel, "");
            TyphoonRows.SetPlain(_radiusLabel, "");
            TyphoonRows.SetPlain(_phaseLabel, "");
            TyphoonRows.SetPlain(_landfallLabel, "");
        }

        private static void ClearWeatherRows()
        {
            TyphoonRows.SetPlain(_rainLabel, "");
            TyphoonRows.SetPlain(_cloudLabel, "");
        }

        /// <summary>
        /// On level unload. **Only drops the references** (the objects themselves go with
        /// the panel's GameObject). Carry them over and you write into destroyed labels
        /// in the next city.
        /// </summary>
        internal static void Destroy()
        {
            _stateLabel = null;
            _centreLabel = null;
            _strengthLabel = null;
            _radiusLabel = null;
            _phaseLabel = null;
            _landfallLabel = null;
            _rainLabel = null;
            _cloudLabel = null;
        }
    }
}
