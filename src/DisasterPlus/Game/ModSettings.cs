using ColossalFramework;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Persistent settings.
    ///
    /// The file name must not be "DisasterPlus", the same as the mod and assembly name.
    /// Every launch would produce "An element with the same key already exists ... Deleting",
    /// the settings would be wiped, the mod would be treated as an error, and even publishing
    /// to the Workshop would break.
    ///
    /// The saved keys and values are treated as a public contract. Integer values derived from
    /// enums have fixed meanings, and numbers are never closed up when an entry is retired
    /// (an existing player's .cgs value would come to mean something else).
    /// </summary>
    public static class ModSettings
    {
        public const string FileName = "DisasterPlusSettings";

        /// <summary>Who owns the earthquake damage calculation. 0 = leave it to the conflicting
        /// mod, 1 = Disaster + owns it.
        /// This number is written to the .cgs and is a public contract. Do not change the
        /// meaning of a value or close the numbering up.</summary>
        public const int EarthquakeOwnerOther = 0;
        public const int EarthquakeOwnerSelf = 1;

        private static bool _ready;

        public static SavedBool FireWhirlEnabled;
        public static SavedInt DetectRadius;
        public static SavedInt DetectCount;

        /// <summary>
        /// Whether to show the trench earthquake tile. **ON by default.**
        /// Switch it off and there is no way left to raise an earthquake that brings a tsunami
        /// with it (vanilla's earthquakes come with no tsunami — <c>TsunamiChain</c>).
        /// </summary>
        public static SavedBool TrenchQuakeEnabled;

        /// <summary>
        /// Whether to take vanilla's (the ND DLC's) tornado out of random spawning.
        /// **ON by default.**
        /// Has no effect on the fire whirl's vortex (<c>VanillaTornadoSuppressor</c>).
        /// </summary>
        public static SavedBool NoVanillaTornado;
        public static SavedInt MaxLifetimeMinutes;
        public static SavedInt SpreadStrength;
        public static SavedInt MinSeparation;
        public static SavedBool IntensityUnlock;
        public static SavedInt EarthquakeDamageOwner;
        public static SavedBool OverlayEnabled;
        public static SavedInt OverlayHotkey;
        public static SavedInt LogChannelMask;
        public static SavedBool ForecastEnabled;

        /// <summary>
        /// ★★ **Retired saved keys (the eight: forecastButtonX/Y, earthquakeButtonX/Y,
        ///     typhoonButtonX/Y and volcanoButtonX/Y).**
        ///
        /// The ①②④⑤ buttons are now placed inside vanilla's disaster panel, and the panel's
        /// own autolayout decides their positions (<c>DisasterPanelBar</c>). So there is not
        /// one place left that reads these eight.
        ///
        /// **The declarations stay all the same.** The .cgs keys and values are a public
        /// contract, and
        /// - deleting the declarations would leave the keys stranded in the .cgs, so that if
        ///   one of these names came back one day *meaning something else*, the old
        ///   coordinates would be read as the new setting
        /// - it is the same reason as this mod's discipline of never closing up numbers or
        ///   names
        ///
        /// **These eight must not be reused for another meaning.** Give a new setting a new
        /// key name.
        /// </summary>
        public static SavedInt ForecastButtonX;
        public static SavedInt ForecastButtonY;
        public static SavedBool EarthquakeEnabled;
        public static SavedInt EarthquakeButtonX;
        public static SavedInt EarthquakeButtonY;
        public static SavedBool EarthquakeShakeBoost;
        public static SavedBool EarthquakeTsunamiChain;
        public static SavedInt EarthquakeTsunamiDelayMinutes;
        public static SavedBool EarthquakeLongPeriod;
        public static SavedInt EarthquakeLongPeriodStrength;

        /// <summary>
        /// **The strength of the trench earthquake's distant damage (0-10). 0 disables it
        /// completely.**
        ///
        /// ★★ Unlike the long-period setting above (OFF by default), **this is ON (6) by
        ///   default.** Long-period is new damage that "collapses buildings vanilla would have
        ///   left standing", whereas this <b>fixes a disaster this mod itself created (the
        ///   trench quake) failing to produce the damage it ought to have</b> (2026-09-02, the
        ///   owner: "the trench earthquake does too little earthquake damage").
        ///   Make it OFF by default and the shortfall we fixed stays unfixed by default.
        ///
        /// ★ It has <b>no effect whatsoever</b> on fault earthquakes or vanilla's
        ///   (see the <c>TrenchQuakeDistantDamage</c> class doc).
        /// </summary>
        public static SavedInt EarthquakeTrenchDamageStrength;

        /// <summary>
        /// **Second layer. The synthetic seismogram (P wave, S wave, coda)**. OFF by default.
        ///
        /// Switching it ON does two things at once:
        ///   1. one more line, <c>[Disaster + model]</c>, appears on the waveform graph
        ///      (vanilla's line stays)
        ///   2. the camera shake takes the shape of the synthetic seismogram instead of
        ///      vanilla's two sine waves
        ///
        /// ★ **Do not take it off OFF-by-default.** <c>EarthquakeShakeBoost</c> can be ON by
        ///   default because at intensity 55 (vanilla's default) its addition is **exactly 0**,
        ///   i.e. the behaviour is bit-for-bit identical to vanilla
        ///   (<c>ShakeWaveform.IntensityFactor</c>); this one has no such escape — the
        ///   synthetic seismogram has a different shape from vanilla at every intensity.
        ///   It gets the same treatment as <c>EarthquakeLongPeriod</c>.
        /// </summary>
        public static SavedBool EarthquakeSeismogram;
        public static SavedBool TyphoonEnabled;
        public static SavedInt TyphoonButtonX;
        public static SavedInt TyphoonButtonY;
        public static SavedInt TyphoonIntensity;
        public static SavedBool TyphoonWindDamage;

        /// <summary>
        /// Whether lightning flashes inside the typhoon's cloud. **ON by default.**
        ///
        /// ★ This is <b>our own rendering</b> (<c>TyphoonBoltFx</c>).
        ///   Vanilla's lightning from the sky <b>never happens at all</b>, because the rain is
        ///   held at 0.8 (<c>TyphoonWeather.MaxRainWithoutLightning</c>).
        /// </summary>
        public static SavedBool TyphoonLightning;
        public static SavedInt TyphoonWindStrength;
        public static SavedBool TyphoonFloodEnabled;
        public static SavedInt TyphoonFloodStrength;
        public static SavedBool TyphoonSouthernHemisphere;
        /// <summary>
        /// **Retired keys (the accompanying tornado). There is not one place left that reads
        /// them.**
        ///
        /// The feature that attached vanilla's tornado disaster to the typhoon was removed.
        /// In response to the owner's instruction — "produce several instances of tornado
        /// damage without producing a tornado" — ④ creates no actual tornado and only puts out
        /// <c>TyphoonGust</c>'s local damage areas.
        ///
        /// **The declarations stay all the same**, for the same reason as the
        /// <c>ForecastButtonX</c> doc: the .cgs keys and values are a public contract.
        /// **These two must not be reused for another meaning** — the new settings
        /// (<see cref="TyphoonGustEnabled"/> / <see cref="TyphoonGustStrength"/>) have been
        /// given new key names.
        /// </summary>
        public static SavedBool TyphoonTornadoes;
        public static SavedInt TyphoonTornadoCount;

        /// <summary>Whether to produce tornado-strength local damage (**no actual tornado is created**).</summary>
        public static SavedBool TyphoonGustEnabled;

        /// <summary>Its strength, 0-10. 0 disables it completely.</summary>
        public static SavedInt TyphoonGustStrength;
        public static SavedBool TyphoonCloudEnabled;
        public static SavedBool TyphoonVanillaCloudBoost;

        /// <summary>
        /// The storm visuals (driving spray, and citizens and cars being blown about).
        ///
        /// ★ **It touches neither buildings, nor roads, nor trees.** The spray is main-thread
        ///   rendering only, and the blowing about is <c>DisasterHelpers.AddWind</c> (citizens
        ///   and vehicles only).
        ///   That is why it is a separate knob from wind damage
        ///   (<see cref="TyphoonWindDamage"/>) — "I don't want the damage but I do want to see
        ///   the storm" can be chosen, and so can the opposite.
        /// </summary>
        public static SavedBool TyphoonStormFx;

        /// <summary>
        /// The sound of the typhoon's wind. **The same path as ⑤'s eruption sound**
        /// (<c>AudioManager.EffectGroup</c>), so the player's effects volume slider and mute
        /// are applied by the game.
        /// ④ plays **just one** (it does not hog a seat in the <c>EffectGroup</c>).
        /// </summary>
        public static SavedBool TyphoonStormSound;
        public static SavedBool VolcanoEnabled;
        public static SavedInt VolcanoButtonX;
        public static SavedInt VolcanoButtonY;

        /// <summary>
        /// The volcano's form. **It is written to the .cgs and is a public contract, so the
        /// numbering is never closed up** (the same values as
        /// <c>DisasterPlus.Core.Volcano.VolcanoForm</c>).
        ///
        /// ★ The field is called <c>VolcanoShapeSetting</c> because it would otherwise clash
        ///   with Core's type name <c>VolcanoShape</c> (which is the profiles of the three
        ///   forms themselves). **Do not change the saved key string "volcanoShape".**
        /// </summary>
        public static SavedInt VolcanoShapeSetting;

        // ── ★★ Notes on retired keys (2026-08-22) ───────────────────
        //
        //   volcanoRadius / volcanoHeight … the volcano's radius and final height (m).
        //
        // Following the owner's remark — "this makes the scale adjustment meaningless, so
        // please fix these at the recommended settings" — **we stopped making the same
        // quantity be decided by two knobs**. The size is decided by the disaster panel's
        // intensity slider alone, with the recommended value per form as the baseline
        // (<c>VolcanoShape.DefaultRadiusOf</c> / <c>DefaultHeightOf</c>; see the
        // <c>VolcanoSizeScale</c> class doc).
        //
        // ★★ <b>Do not recycle these two keys for another meaning.</b>
        //   The .cgs already holds the metre values the player chose, and assigning the same
        //   name to a different quantity means **the old value is read with the new meaning**
        //   (a setting's saved value is a public contract. This mod has walked into that once
        //   already).
        //   They are merely no longer read, so there is no need to delete them from the .cgs.

        /// <summary>
        /// How many metres ahead of the uplift front the clearing (destruction) front runs (m).
        /// At 0 it would mean "raise a cell in the same tick it was cleared", leaving no slack.
        /// </summary>
        public static SavedInt VolcanoClearingLeadMetres;

        /// <summary>
        /// The strength of the relief on the mountainside (%). **At 0, today's smooth cone**;
        /// 100 is the per-form default; the ceiling is
        /// <c>VolcanoRelief.MaxStrengthUnit</c> (150).
        ///
        /// Only this one knob is exposed because the character of the relief (the number of
        /// gullies, the wavelength, the roughness) is decided per form by the table in
        /// <c>Core/Volcano/VolcanoRelief</c>, and all the player decides is "how strongly to
        /// apply it".
        /// **Do not add more knobs.**
        /// </summary>
        public static SavedInt VolcanoReliefStrength;

        /// <summary>
        /// The in-game minutes spent on the uplift. <c>UpliftSchedule.TotalTicksFor</c> trims
        /// it at the ceiling of "the summit moves at least 1/64 m per tick", so an excessive
        /// value can never make it stop silently.
        /// </summary>
        public static SavedInt VolcanoUpliftMinutes;

        /// <summary>
        /// Whether to draw the eruption plume (T7). **Switch it off and the uplift and the
        /// lava carry on unchanged** — drawing the eruption is a main-thread-only feature and
        /// changes not one piece of game state.
        /// </summary>
        public static SavedBool VolcanoEruptionFx;

        /// <summary>
        /// Whether to draw the lightning inside the plume (volcanic lightning). **It can be
        /// switched off separately from drawing the plume** — some people dislike the flashes,
        /// and making them switch off the plume as well would be heavy-handed.
        /// If <c>VolcanoEruptionFx</c> is off, nothing is drawn even when this is true
        /// (the lightning exists only inside the plume. <c>VolcanoCraterFx</c>).
        /// </summary>
        public static SavedBool VolcanoLightningFx;

        /// <summary>
        /// Whether to play the sound of the eruption. **Switch it off and the uplift, the lava
        /// and the plume all carry on unchanged** — sound is a main-thread-only feature and
        /// changes not one piece of game state.
        ///
        /// ★ The volume itself does not live here. **The player's effects volume slider and
        ///   mute apply directly** (see the <c>VolcanoEruptionAudio</c> class doc), so a
        ///   second volume knob would leave nobody able to tell which one is in effect.
        /// </summary>
        public static SavedBool VolcanoEruptionSound;

        /// <summary>
        /// The number of lava flows issuing from the crater (T8). **0 disables it completely**
        /// (no lava and no ignition). The ceiling is clamped on the consuming side by
        /// <c>VolcanoLava.MaxFlows</c>.
        /// </summary>
        public static SavedInt VolcanoLavaFlows;

        /// <summary>
        /// Whether to set fire along the lava's path (T8). **Switch it off and the lava still
        /// flows** (it becomes purely visual).
        /// </summary>
        public static SavedBool VolcanoLavaFire;

        /// <summary>
        /// Whether to draw the lava surface (T9). **Switch it off and the lava still flows,
        /// still scorches the ground and still sets buildings on fire** — drawing is a
        /// main-thread-only feature and changes not one piece of game state.
        /// </summary>
        public static SavedBool VolcanoLavaRender;

        /// <summary>
        /// Whether to produce the band of dust running down the slope (the stand-in for a
        /// "pyroclastic flow").
        /// **This is not a reproduction of a pyroclastic flow** — the game has no pyroclastic
        /// flow effect at all, and what is produced is the dust from a building collapse sent
        /// down the lava's path (see the <c>VolcanoPyroclasticFx</c> class doc).
        /// **Switching it off changes nothing about the eruption or the lava** (the band
        /// destroys nothing).
        /// </summary>
        public static SavedBool VolcanoPyroclasticFx;

        /// <summary>
        /// Whether volcanic earthquakes (swarms plus tremor) shake the camera. **ON by default.**
        ///
        /// ★ The reason it can be ON by default differs from ②'s <c>EarthquakeShakeBoost</c>.
        ///   That one <b>replaces the camera shake of vanilla's earthquakes</b>, so changing
        ///   its default would change the vanilla experience of anyone who installs the mod.
        ///   ⑤'s volcano is **⑤'s own phenomenon, which the player raised themselves**, and
        ///   it would be stranger for it not to shake.
        ///
        /// ★ Switching it off merely stops the shaking. **It never destroyed a single building
        ///   to begin with** (see the <c>VolcanoTremorShake</c> class doc).
        /// </summary>
        public static SavedBool VolcanoQuake;

        /// <summary>The saved values for the form (a public contract). The same numbers as <c>VolcanoForm</c>.</summary>
        public const int VolcanoShapeShield = 0;
        public const int VolcanoShapeStrato = 1;
        public const int VolcanoShapeDome = 2;

        public static void Ensure()
        {
            if (_ready) return;

            if (GameSettings.FindSettingsFileByName(FileName) == null)
            {
                GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
            }

            FireWhirlEnabled      = new SavedBool("fireWhirlEnabled", FileName, true, true);
            // ★★ **The keys were renamed (2026-08-22).** Report from the game: "fire whirls
            //    happen far too often. Make the range that triggers them 3*3 times bigger."
            //
            //    Simply rewriting the default <b>does nothing for anyone already playing</b> —
            //    a SavedInt's default only applies when the key is absent from the .cgs, so a
            //    player with 150/12 saved would stay on the old (too frequent) values.
            //    So the keys themselves are new. The old keys are declared below, retired.
            //
            //    3× the radius and 9× the building count = **the density per unit area stays
            //    the same, and only the scale of fire required goes up 9×**. Real fire whirls
            //    likewise only stand up in a city-scale conflagration.
            DetectRadius          = new SavedInt("fwDetectRadius2", FileName, 450, true);
            DetectCount           = new SavedInt("fwDetectCount2", FileName, 108, true);

            // ★★ Stops vanilla's (the DLC's) tornado. Report from the game:
            //    "there's a bug where the DLC tornado spawns and never goes away.
            //      Please stop vanilla tornadoes from spawning."
            //    The fire whirl itself calls CreateDisaster directly and is unaffected
            //    (see the VanillaTornadoSuppressor class doc).
            NoVanillaTornado      = new SavedBool("fwNoVanillaTornado", FileName, true, true);

            // ★★ The trench earthquake (2026-08-22, the owner's request). **This is the only
            //    earthquake that brings a tsunami**; vanilla's fault earthquakes do not.
            TrenchQuakeEnabled    = new SavedBool("eqTrenchQuake", FileName, true, true);
            MaxLifetimeMinutes    = new SavedInt("fwMaxLifetime", FileName, 10, true);
            SpreadStrength        = new SavedInt("fwSpreadStrength", FileName, 3, true);
            MinSeparation         = new SavedInt("fwMinSeparation", FileName, 300, true);
            // OFF by default when the conflicting mod (NDR) is present. It performs the same
            // unlock, so we do not do it twice (spec 3.2 / 3.3(a)). It can still be switched
            // ON by hand.
            //
            // A SavedBool's default only applies while the key is absent from the .cgs, so a
            // player who has already chosen keeps their value and no migration is needed. It
            // also avoids the trap of deciding on a new key's .exists (which is false for
            // everyone).
            IntensityUnlock       = new SavedBool("intensityUnlock", FileName, !ModCompat.NdrPresent, true);
            EarthquakeDamageOwner = new SavedInt("eqDamageOwner", FileName, EarthquakeOwnerOther, true);
            // The diagnostic overlay. OFF by default (a developer feature, not shown to
            // ordinary players).
            OverlayEnabled        = new SavedBool("diagOverlayEnabled", FileName, false, true);
            OverlayHotkey         = new SavedInt("diagOverlayHotkey", FileName, (int)KeyCode.F11, true);
            LogChannelMask        = new SavedInt("diagLogChannels", FileName,
                                                  DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask, true);

            ForecastEnabled  = new SavedBool("forecastEnabled", FileName, true, true);
            // ★ Retired keys. Nothing reads them any more, but a key is a public contract, so
            //   the declarations stay (see the ForecastButtonX doc. Do not reuse them for
            //   another meaning).
            ForecastButtonX  = new SavedInt("forecastButtonX", FileName, -1, true);
            ForecastButtonY  = new SavedInt("forecastButtonY", FileName, -1, true);

            EarthquakeEnabled = new SavedBool("earthquakeEnabled", FileName, true, true);
            // ★★ **Two retired keys (2026-08-22).** The fire whirl's spawn conditions moved to
            //    fwDetectRadius2 / fwDetectCount2 (see the doc above).
            //    **Do not recycle these keys for another meaning.**
            new SavedInt("fwDetectRadius", FileName, 150, true);
            new SavedInt("fwDetectCount", FileName, 12, true);

            // ★ Retired keys. Treated exactly like ForecastButtonX/Y (see that doc).
            EarthquakeButtonX = new SavedInt("earthquakeButtonX", FileName, -1, true);
            EarthquakeButtonY = new SavedInt("earthquakeButtonY", FileName, -1, true);
            // It can be ON by default because at intensity 55 (vanilla's default) the addition
            // is exactly 0, and CameraShakeBooster then writes nothing at all to m_cameraShake
            // — that is, on a default earthquake the behaviour is bit-for-bit identical to
            // vanilla (pinned by ShakeWaveform.IntensityFactor and its unit tests).
            EarthquakeShakeBoost = new SavedBool("eqShakeBoost", FileName, true, true);

            // ★ The second layer is always OFF by default (the common rule of the plan's
            //    "second layer — adding"). Enable behaviour that does not exist in vanilla by
            //    default and the player has no way of realising that "a tsunami arrives by
            //    itself after an earthquake" comes from a mod.
            //    EarthquakeShakeBoost above can be ON by default because at the default
            //    intensity its addition is exactly 0, i.e. bit-for-bit identical to vanilla;
            //    this one has no such escape.
            EarthquakeTsunamiChain = new SavedBool("eqTsunamiChain", FileName, false, true);
            // In-game minutes. The range 5-120 is enforced by the slider (a .cgs value is a
            // public contract, so an out-of-range value is not discarded but used as-is — the
            // delay is merely long, not a broken value).
            EarthquakeTsunamiDelayMinutes = new SavedInt("eqTsunamiDelay", FileName, 30, true);

            // ★ Second layer. This one is likewise always OFF by default (the same reason as
            //    EarthquakeTsunamiChain above). It is heavier than the tsunami, moreover —
            //    it **collapses buildings vanilla would have left standing**.
            //    Enable it by default and the player has no way of realising that a tower
            //    block came down because of a mod (see the LongPeriodDamage class doc).
            EarthquakeLongPeriod = new SavedBool("eqLongPeriod", FileName, false, true);
            // 0-10. 0 disables it completely (LongPeriodResponse.ExtraCollapseChance returns
            // exactly 0). The range is enforced by the slider, but a .cgs value is a public
            // contract, so an out-of-range value is not discarded; the consuming side clamps it.
            EarthquakeLongPeriodStrength = new SavedInt("eqLongPeriodStrength", FileName, 3, true);
            // 0-10. 0 disables it completely (DistantDamage returns exactly 0).
            // ★ Default 6 (see this field's doc). A .cgs value is a public contract, so an
            //   out-of-range value is not discarded; the consuming side clamps it.
            EarthquakeTrenchDamageStrength =
                new SavedInt("eqTrenchDamageStrength", FileName, 6, true);

            // ★ The third second-layer item. OFF by default (see the
            //    ModSettings.EarthquakeSeismogram doc).
            //    While it is OFF, the recording, the drawing and the camera shake are
            //    **not one bit different from today**.
            EarthquakeSeismogram = new SavedBool("eqSeismogram", FileName, false, true);

            // ④ typhoon. Simply displaying the panel does nothing unless ④ raises something,
            // so enabling it can be ON by default (the same treatment as ②'s
            // EarthquakeEnabled). A typhoon is only ever raised by an explicit player action
            // (T3).
            TyphoonEnabled = new SavedBool("typhoonEnabled", FileName, true, true);
            // ★ Retired keys. Treated exactly like ForecastButtonX/Y (see that doc).
            TyphoonButtonX = new SavedInt("typhoonButtonX", FileName, -1, true);
            TyphoonButtonY = new SavedInt("typhoonButtonY", FileName, -1, true);
            // The typhoon's intensity. ① has already unlocked it to 255 (IntensityUnlock). The
            // range 10-255 is enforced by the slider, but a .cgs value is a public contract, so
            // an out-of-range value is not discarded; the consuming side
            // (TyphoonController.ClampIntensity) clamps it.
            // The game's own storm is 55. The default of 120 was chosen as clearly stronger
            // than that without being as extreme as the ceiling of 255.
            TyphoonIntensity = new SavedInt("typhoonIntensity", FileName, 120, true);

            // ★ Wind damage is ON by default. The reason this call differs from ②'s second
            //    layer (the tsunami chain, long-period) is in the TyphoonWind class doc —
            //    a typhoon is raised explicitly by the player, so what happened cannot be
            //    misattributed. Design doc §4.3 also specifies ON by default.
            TyphoonWindDamage = new SavedBool("typhoonWind", FileName, true, true);
            // 0-10. 0 disables it completely (WindDamageModel.CollapseChance returns exactly 0).
            // The range is enforced by the slider, but a .cgs value is a public contract, so
            // the consuming side clamps it.
            TyphoonWindStrength = new SavedInt("typhoonWindStrength", FileName, 3, true);

            // ★ The dangerous semicircle (which side of the direction of travel is stronger).
            //    **The default is the northern hemisphere = the right.**
            //    In a real typhoon the stronger side is the one where the rotation of the
            //    vortex and the movement add together, which in the northern hemisphere is to
            //    the right of travel and in the southern hemisphere to the left (see the
            //    TrackBias class doc).
            //    The default of false (i.e. the northern hemisphere) is because most CS maps
            //    are built assuming a northern-hemisphere city, not for any physical reason.
            TyphoonSouthernHemisphere =
                new SavedBool("typhoonSouthernHemisphere", FileName, false, true);

            // ★ ON by default (the same reason as wind damage). But this is **the only feature
            //    that touches state which gets burnt into the save**, so the restore path is
            //    called from three places (on end, on unload and on save) (see the
            //    TyphoonFlood class doc).
            // ★★ Lightning inside the cloud (2026-08-25, the owner's instruction: "have
            //    lightning come out of the typhoon's cloud from time to time").
            //    **Vanilla's lightning is sealed off by the rain ceiling.**
            TyphoonLightning    = new SavedBool("typhoonLightning", FileName, true, true);

            TyphoonFloodEnabled = new SavedBool("typhoonFlood", FileName, true, true);
            TyphoonFloodStrength = new SavedInt("typhoonFloodStrength", FileName, 3, true);

            // ★★ **Two retired keys.** The accompanying tornado was removed (see the doc
            //    above). Only the declarations remain; nothing reads them.
            //    **Do not reuse them for another meaning.**
            TyphoonTornadoes = new SavedBool("typhoonTornado", FileName, false, true);
            TyphoonTornadoCount = new SavedInt("typhoonTornadoCount", FileName, 1, true);

            // ★ Tornado-strength local damage. **These are new keys** (the retired keys were
            //    not recycled). ON by default — the same reason as wind damage: a typhoon is
            //    raised explicitly by the player, so what happened cannot be misattributed.
            //    A strength of 0 disables it completely.
            TyphoonGustEnabled = new SavedBool("typhoonGust", FileName, true, true);
            TyphoonGustStrength = new SavedInt("typhoonGustStrength", FileName, 3, true);

            // ★ The cloud is ON by default. **A purely visual feature that changes not one
            //    byte of game state** (it merely issues Graphics.DrawMesh on the main thread).
            //    Switching it off leaves the other five elements running unchanged (see the
            //    independence described in the TyphoonCloud class doc).
            TyphoonCloudEnabled = new SavedBool("typhoonCloud", FileName, true, true);
            // Makes vanilla's sky-dome clouds thicker and faster. **It may not exist in some
            // environments** (DLC, graphics settings. IL findings document §C-2, PARTIAL).
            // If it is absent, give up quietly.
            TyphoonVanillaCloudBoost = new SavedBool("typhoonCloudBoost", FileName, true, true);

            // ★ The storm visuals are ON by default. **They move not one byte in the direction
            //    of damaging game state** (the spray is drawing only, and the blowing about
            //    affects only citizens and vehicles).
            //    ★★ A saved key is a public contract. Do not close up existing keys; append at
            //    the end.
            TyphoonStormFx = new SavedBool("typhoonStormFx", FileName, true, true);

            // ★ The wind sound is ON by default. **Until then ④ played no sound at all.**
            //    ★★ A saved key is a public contract. Do not close up existing keys; append at
            //    the end.
            TyphoonStormSound = new SavedBool("typhoonStormSound", FileName, true, true);

            // ★ The ⑤ volcano is ON by default. **Do not disable it on the grounds that the
            //    DLC is absent** — ⑤ does not need Natural Disasters (design doc §1.4).
            //    And ⑤ never fires by itself (it only begins once the player has pointed at a
            //    spot and acknowledged that it is irreversible), so being ON by default never
            //    silently changes the terrain.
            VolcanoEnabled = new SavedBool("volcanoEnabled", FileName, true, true);
            // ★ Retired keys. Treated exactly like ForecastButtonX/Y (see that doc).
            VolcanoButtonX = new SavedInt("volcanoButtonX", FileName, -1, true);
            VolcanoButtonY = new SavedInt("volcanoButtonY", FileName, -1, true);

            // ★ The saved value is a public contract. Do not close up the numbering
            //    0=shield / 1=strato / 2=lava dome.
            //    An out-of-range value is dropped to the default (strato) by
            //    VolcanoShape.FormOf.
            VolcanoShapeSetting = new SavedInt("volcanoShape", FileName, VolcanoShapeStrato, true);
            // How many metres ahead of the uplift front the clearing front runs. At 0 it would
            // mean "raise a cell in the same tick it was cleared", leaving no slack.
            VolcanoClearingLeadMetres = new SavedInt("volcanoClearLead", FileName, 96, true);

            // The strength of the relief on the mountainside (%). At 0, today's smooth cone.
            // ★ This is a new key. Neither the name nor the default of any existing key was
            //   changed (the .cgs is a public contract, and neither numbers nor strings are
            //   closed up).
            VolcanoReliefStrength = new SavedInt("volcanoRelief", FileName, 100, true);

            // The in-game minutes spent on the uplift. UpliftSchedule.TotalTicksFor trims it
            // at the ceiling of "the summit moves at least 1/64 m per tick".
            VolcanoUpliftMinutes = new SavedInt("volcanoUpliftMinutes", FileName, 30, true);

            // Whether to draw the eruption plume. Switching it off does not stop the uplift
            // (drawing is a main-thread-only feature).
            VolcanoEruptionFx = new SavedBool("volcanoEruptionFx", FileName, true, true);
            // Volcanic lightning. ON by default (flashes inside the plume are one of the
            // highlights of an eruption).
            VolcanoLightningFx = new SavedBool("volcanoLightningFx", FileName, true, true);

            // Whether to produce the band of dust running down the slope. ON by default.
            // ★ **This is a new key. Not one name or default of an existing key was changed**
            //   (the .cgs is a public contract, and neither numbers nor strings are closed up).
            VolcanoPyroclasticFx = new SavedBool("volcanoPyroclasticFx", FileName, true, true);

            // Whether to play the sound of the eruption. ON by default.
            // ★ **This is a new key. Not one name or default of an existing key was changed**
            //   (the .cgs is a public contract, and neither numbers nor strings are closed up).
            //   In an environment without the bundled wav, leaving it ON simply results in
            //   silence, so ON by default is fine.
            VolcanoEruptionSound = new SavedBool("volcanoEruptionSound", FileName, true, true);

            // The number of flows issuing from the crater. 0 disables it completely (no lava
            // and no ignition).
            VolcanoLavaFlows = new SavedInt("volcanoLavaFlows", FileName, 4, true);
            // Whether to set fire along the lava's path. Switching it off still leaves the
            // lava flowing (it becomes purely visual).
            VolcanoLavaFire = new SavedBool("volcanoLavaFire", FileName, true, true);
            // Whether to draw the lava surface. Switching it off still leaves the lava flowing
            // (drawing is a main-thread-only feature).
            VolcanoLavaRender = new SavedBool("volcanoLavaRender", FileName, true, true);

            // Whether volcanic earthquakes (swarms plus tremor) shake the camera. ON by default.
            // ★ **This is a new key. Not one name or default of an existing key was changed**
            //   (the .cgs is a public contract, and neither numbers nor strings are closed up).
            VolcanoQuake = new SavedBool("volcanoQuake", FileName, true, true);

            MigrateEnableFlagsIntoStrength();

            _ready = true;
        }

        /// <summary>
        /// <b>Folds the "enabled" checkbox and "strength 0" into one operation.</b>
        ///
        /// ── The owner's remark (2026-09-02) ────────────────────────────
        ///
        /// &gt; Parts of the Options panel UI overlap, which makes them awkward to use
        ///
        /// Wind damage, local damage, flooding and long-period all had **both a checkbox and
        /// a "0 disables it" slider for the same feature**.
        /// With two ways to switch something off, putting only one of them back leaves it
        /// <b>on but having no effect</b>, and the screen gives no reason why.
        ///
        /// The knob is consolidated into a single slider. The checkbox came off the settings
        /// screen.
        ///
        /// ── ★★ Migrate without changing a single existing setting ─────────────────
        ///
        /// A <c>.cgs</c> value is a public contract, so <b>keys are neither deleted nor closed
        /// up</b>. Instead <b>the meaning is moved exactly once</b>:
        ///
        /// <code>
        /// the checkbox was off -> set the strength to 0 (preserving the fact that it was off)
        /// then pin the checkbox to true -> nothing happens on any later run
        /// </code>
        ///
        /// That makes **every combination behave the same as today**:
        ///
        /// <list type="bullet">
        /// <item>checkbox ON, strength 3 → unchanged (in effect)</item>
        /// <item>checkbox OFF, strength 3 → strength 0 (stays off. <b>It is never enabled
        ///   behind the player's back</b>)</item>
        /// <item>checkbox ON, strength 0 → unchanged (stays off)</item>
        /// </list>
        ///
        /// ★★ Long-period was <b>OFF by default</b> (because it collapses buildings vanilla
        ///   would leave standing). Even in a brand-new environment <c>eqLongPeriod</c>
        ///   defaults to false, so passing through here drops the strength to 0 —
        ///   <b>OFF by default is preserved exactly</b>.
        /// </summary>
        private static void MigrateEnableFlagsIntoStrength()
        {
            Fold(EarthquakeLongPeriod, EarthquakeLongPeriodStrength);
            Fold(TyphoonWindDamage, TyphoonWindStrength);
            Fold(TyphoonFloodEnabled, TyphoonFloodStrength);
            Fold(TyphoonGustEnabled, TyphoonGustStrength);
        }

        /// <summary>
        /// The migration for one pair. **Idempotent** — on any later run the flag is set, so
        /// it does nothing.
        /// </summary>
        private static void Fold(SavedBool enabled, SavedInt strength)
        {
            if (enabled.value) return;

            strength.value = 0;
            enabled.value = true;
        }

        /// <summary>Transfers the settings into Core's config object. Core knows nothing of SavedInt.</summary>
        public static FireWhirlConfig ToFireWhirlConfig()
        {
            Ensure();
            var c = FireWhirlConfig.Defaults();
            c.DetectRadius = DetectRadius.value;
            c.DetectCount = DetectCount.value;
            c.MaxLifetimeMinutes = MaxLifetimeMinutes.value;
            c.MinSeparation = MinSeparation.value;
            c.SpreadStrength = SpreadStrength.value;
            return c;
        }
    }
}
