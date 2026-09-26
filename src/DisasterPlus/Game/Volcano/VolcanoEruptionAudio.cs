using System;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Whether the eruption sound can be played in this environment. **It carries "usable", not
    /// "resolved"** (the same discipline as <see cref="VolcanoVanillaFacts"/>. ④'s review and
    /// ②'s audit found the defect where a check whose predicate is "did the field resolve"
    /// reports PASS in an environment where the value is unusable).
    ///
    /// It holds nothing but bools and ints, so caching it does not drag in Unity's fake-null
    /// problem. All defaults are false = "not read yet / no longer readable".
    /// </summary>
    public struct VolcanoAudioFacts
    {
        /// <summary>Whether <c>AudioManager</c> was there and we reached <c>EffectGroup</c>.</summary>
        public readonly bool EffectGroupResolved;

        /// <summary>
        /// Whether <c>AudioManager.AddEvent(AudioGroup, AudioInfo, Vector3, Vector3, float, float,
        /// float, int)</c> could be resolved. **It is the only path by which ⑤ makes a sound.**
        /// </summary>
        public readonly bool AddEventResolved;

        /// <summary>
        /// Whether <c>AudioClip.Create(string,int,int,int,bool)</c> and
        /// <c>AudioClip.SetData(float[],int)</c> could be resolved.
        /// **It is the only path for building a clip synchronously.**
        /// </summary>
        public readonly bool ClipApiResolved;

        /// <summary>Whether the bundled wav is in the mod folder. **⑤ works exactly as today without it.**</summary>
        public readonly bool FileFound;

        /// <summary>Size of the bundled wav in bytes (if present).</summary>
        public readonly long FileBytes;

        public VolcanoAudioFacts(bool effectGroupResolved, bool addEventResolved,
                                 bool clipApiResolved, bool fileFound, long fileBytes)
        {
            EffectGroupResolved = effectGroupResolved;
            AddEventResolved = addEventResolved;
            ClipApiResolved = clipApiResolved;
            FileFound = fileFound;
            FileBytes = fileBytes;
        }

        /// <summary>
        /// Whether the path for making a sound holds in this build of the game.
        /// **<see cref="FileFound"/> is not included** — the presence of the file is the player's
        /// business (they may delete it) and not a sign of a game update. Mix it in and you tell
        /// someone who deleted it themselves that "an assumption broke" (do not cry wolf).
        /// </summary>
        public bool Usable
        {
            get { return EffectGroupResolved && AddEventResolved && ClipApiResolved; }
        }
    }

    /// <summary>
    /// The eruption sound. **Main thread only** (all of Unity's audio assets are main).
    ///
    /// ── why we "ride the vanilla path" (measured in IL) ────────────────────────────────────
    ///
    /// Build your own <c>GameObject</c> + <c>AudioSource</c> and **neither the player's volume
    /// slider nor mute has any effect** (it keeps playing at raw volume). Going through every
    /// vanilla disaster sound in IL, the answer was a single road —
    /// **they all feed into <c>AudioManager.EffectGroup</c>**
    /// (the list of callers is in ⑤'s IL facts doc §H-23. ⑤'s docs do not write those type
    /// names — because the guarantee for "⑤ does not touch the disaster-related types" is a grep.
    /// See the class doc of <see cref="VolcanoFeature"/>):
    ///
    /// <code>
    /// AudioManager.AddEvent(EffectGroup, info, position, velocity,
    ///                       maxDistance, volume, pitch, playerID)
    ///     → it only pushes onto m_eventBuffer (guarded by Monitor.TryEnter)
    ///     → AudioManager.LateUpdate drains it and calls EffectGroup.AddPlayer
    ///
    /// AudioManager.Awake:
    ///     m_effectGroup = new AudioGroup(3, new SavedFloat(Settings.effectAudioVolume,
    ///                                                      Settings.gameSettingsFile, …))
    /// end of AudioGroup.UpdatePlayers:
    ///     m_cachedVolume = (float)m_groupVolume      ← the sound-effects slider itself
    ///     m_totalVolume  = m_cachedVolume * master   ← mute goes into master
    /// top of AudioGroup.AddPlayer:
    ///     if (m_totalVolume &lt; 0.01) return;         ← muted or 0 and not one sound is emitted
    ///     m_targetVolume = info.m_volume * volume * m_cachedVolume
    /// </code>
    ///
    /// So as long as you feed into <c>EffectGroup</c>, **the game applies the sound-effects
    /// slider and mute for you**. The 3D distance attenuation (<c>rolloffMode = Linear</c>,
    /// <c>minDistance = 0</c>, <c>maxDistance</c> as passed), Doppler, priority and fading are
    /// all on their side too.
    ///
    /// ★ <b>This mod builds not a single <see cref="AudioSource"/> or <see cref="GameObject"/>.</b>
    ///   <c>AudioManager.ObtainPlayer</c> takes one out of the pool and plugs
    ///   <c>AudioInfo.ObtainClip()</c> (which just returns <c>m_clip</c> as is) into
    ///   <c>source.clip</c>.
    ///   The only Unity objects this mod holds are one <see cref="AudioClip"/> and one
    ///   <see cref="AudioInfo"/>, and both are destroyed by <see cref="Destroy"/>.
    ///
    /// ── <c>AddEvent</c> or <c>AddPlayer</c> ────────────────────────────────────────────────
    ///
    /// Call <c>AddPlayer</c> directly and **on any frame where you happen to call it after
    /// <c>UpdatePlayers</c>, the sound disappears** (<c>m_playerDataCount</c> is reset to 0 at
    /// the end of <c>UpdatePlayers</c>). There is no guarantee whether a mod's update hook runs
    /// before or after vanilla's <c>LateUpdate</c>, so we do not take that bet.
    /// <c>AddEvent</c> only pushes onto <c>m_eventBuffer</c>, and
    /// <c>AudioManager.LateUpdate</c> **always** drains it before <c>UpdatePlayers</c>. On top of
    /// that it is guarded by
    /// <c>Monitor.TryEnter(m_eventBuffer, SYNCHRONIZE_TIMEOUT)</c> and vanilla itself calls it
    /// from the sim thread (the callers are in ⑤'s IL facts doc §H-23).
    /// **Take the side with no bet on either ordering or threading.**
    ///
    /// ── keeping the loop alive, and stopping it ────────────────────────────────────────────
    ///
    /// <c>UpdatePlayers</c> keeps a loop alive purely by whether <b>a <c>PlayerData</c> with a
    /// matching <c>m_id</c> exists on this frame</b>. So:
    ///
    /// <code>
    /// keep it playing = AddEvent with the same id every frame
    /// stop it         = stop calling AddEvent (vanilla then fades it out over m_fadeLength and releases it)
    /// </code>
    ///
    /// An explicit <c>Stop()</c> is not needed, and **must not exist** (the pooled
    /// <c>AudioSource</c> is not this mod's).
    ///
    /// ★ Call it exactly once per frame. Call it twice and two <c>PlayerData</c> are pushed, and
    ///   one sound occupies a slot of <c>EffectGroup</c>'s budget
    ///   (<c>m_maxActiveCount = 3</c>).
    ///   That is why it is called **from the main thread's drawing side**, not from the sim
    ///   thread (which runs several ticks in one frame at speed 3).
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// One <c>AddEvent</c> (<c>SimulationEvent</c> is a struct and <c>FastList.Add</c> allocates
    /// nothing amortised) plus two <c>Vector3</c>.
    /// **Zero bytes of heap allocation, zero lines of log.**
    ///
    /// ── the once-per-city cost ─────────────────────────────────────────────────────────────
    ///
    /// On <b>the frame of the first eruption</b> it reads 6.2 MB, converts it to samples (6 ms
    /// measured offline), and cuts the 10 seconds of the peak into an <c>AudioClip</c>.
    /// **There is a brief hitch.** It is not preloaded on level load because ⑤ is a feature that
    /// only ever starts on a player action, and we do not want to make every city that never
    /// places a volcano pay this cost.
    /// On failure it **does not try twice** (<see cref="_loadAttempted"/>).
    /// </summary>
    public static class VolcanoEruptionAudio
    {
        /// <summary>Where the bundled wav lives (relative to the mod folder). Handled like <c>Locales</c>.</summary>
        public const string AudioFolderName = "Audio";

        /// <summary>The bundled wav's file name. **A fixed name, neither a setting nor a translation.**</summary>
        public const string FileName = "erupting-volcano.wav";

        /// <summary>
        /// The window used for the loop (seconds). **Based on measurement** (the class doc of
        /// <see cref="LoopSlice"/>) — the bundled file is a 36-second recording of one eruption,
        /// and 3–13 seconds is the peak.
        /// </summary>
        private const float LoopStartSeconds = 3.0f;

        private const float LoopLengthSeconds = 10.0f;

        /// <summary>The crossfade at the seam (seconds).</summary>
        private const float LoopFadeSeconds = 1.0f;

        /// <summary>
        /// The audible range (m). Vanilla passes 5000 for tornadoes and 10000 for thunder
        /// (<c>rolloffMode</c> is <c>Linear</c> and <c>minDistance</c> is 0, so the volume falls
        /// off linearly out to here). ⑤ sits in the same band as the tornado.
        /// </summary>
        private const float MaxDistanceMetres = 5000f;

        /// <summary>
        /// The volume ratio at an eruption strength of 0. It is not 0 because ⑤'s strength
        /// fluctuates from one segment to the next — drop it to 0 and the sound seems to cut out
        /// partway through the eruption.
        /// </summary>
        private const float MinVolumeUnit = 0.35f;

        /// <summary>
        /// The factor applied to the loaded waveform (2026-08-22, the owner's request
        /// "the eruption sound is too quiet. Please make it about twice as loud").
        ///
        /// ★★ **Raising the <c>volume</c> passed to <c>AddEvent</c> does not get there.**
        ///   That one is already used all the way up to 1.0 by the eruption strength, and whether
        ///   <c>AudioSource.volume</c> accepts a value above 1 depends on Unity's native-side
        ///   implementation — **it does not appear in IL, so this mod cannot measure it**.
        ///   Do not bet a doubling on something you cannot confirm.
        ///   Make the waveform itself louder and the sound-effects slider, the mute and the
        ///   distance attenuation all keep working as before; the only thing that goes up is the
        ///   loudness.
        ///
        /// A plain doubling clips (the source already peaks at 0.837), so
        /// <see cref="LoudnessBoost"/> doubles only the quiet parts exactly and makes the peak
        /// approach 1 asymptotically (the measurements are in that class's doc).
        /// </summary>
        private const float LoudnessGain = 2f;

        /// <summary>
        /// <c>AudioInfo.m_fadeLength</c> (seconds). <c>PlayerData.m_fadeSpeed = 1 / this</c>, so
        /// **it must not be 0** (the division becomes ∞ and the fade disappears).
        /// When stopped, it fades out over this many seconds and is released.
        /// </summary>
        private const float FadeSeconds = 2f;

        /// <summary>
        /// The fixed player ID passed to <c>AddEvent</c>.
        ///
        /// ★ Pick a value that does not collide with vanilla's ids. Vanilla uses
        ///   <c>InstanceID.RawData</c> (type number × 2^24 + index; in practice a small positive
        ///   number) and the negative numbers <c>m_effectPlayerID</c> produces by **decrementing**
        ///   from 0 one at a time.
        ///   Put a large positive constant far away from both.
        ///   **⑤ only ever has one eruption at a time** (there is one phase machine), so there is
        ///   no need to vary it per volcano.
        /// </summary>
        private const int PlayerId = 0x7D15A570;

        // ── main-side state (★ not an array. One reference each) ────────────────────────────

        private static AudioClip _clip;
        private static AudioInfo _info;

        /// <summary>Whether loading has already been attempted in this city. **Do not try twice, even on failure.**</summary>
        private static bool _loadAttempted;

        /// <summary>The most recent load result (**English, for diagnostics**).</summary>
        private static string _detail = "not loaded yet";

        private static bool _errorLogged;

        /// <summary>
        /// The most recent scan result. **<see cref="ScanAudioFacts"/> writes it on the main
        /// thread, and the diagnostics (sim thread) read it.** The same shape ③, ④ and ⑤ already
        /// use: a struct holding nothing but bools and a long, so caching it does not drag in
        /// Unity's fake-null problem.
        /// </summary>
        private static VolcanoAudioFacts _facts;

        private static bool _factsScanned;

        /// <summary>Whether we hold a clip (for diagnostics).</summary>
        public static bool ClipLoaded { get { return _clip != null && _info != null; } }

        /// <summary>
        /// The most recently scanned facts. **If it has never been scanned these are the defaults
        /// (all false)**, so the reader must check <see cref="FactsScanned"/> first — report
        /// "not scanned" as "there is no path" and the diagnostics are lying.
        /// </summary>
        public static VolcanoAudioFacts LastFacts { get { return _facts; } }

        /// <summary>Whether <see cref="ScanAudioFacts"/> has ever run.</summary>
        public static bool FactsScanned { get { return _factsScanned; } }

        /// <summary>
        /// How the load went, in one line (**English**). It is **the only clue if the file is
        /// ever replaced or deleted**, so always state what happened.
        /// </summary>
        public static string Detail { get { return _detail; } }

        /// <summary>
        /// **Call from the main thread.** A probe that only checks whether the audio path holds
        /// in this environment; it builds not a single Unity object and does not load the clip
        /// (do not drag 6 MB of file I/O into level load).
        /// <see cref="Update"/> gates on **this same expression**.
        ///
        /// ★★ <b>Do not call it from the sim thread.</b> That would drag the blocking I/O of
        ///   <c>File.Exists</c>, and the <c>PluginManager.GetInstances</c> inside
        ///   <see cref="LocaleLoader.ModDirectoryPath"/>, onto the sim thread.
        ///   The diagnostics (<c>IDisasterFeature.WriteDiagnostics</c>) are **a sim-thread
        ///   contract**, so they do not scan; they read the <see cref="LastFacts"/> cache.
        ///   That is why the result is stored in <see cref="_facts"/> here.
        ///
        /// Check <c>Singleton&lt;T&gt;.exists</c> first (when <c>sInstance</c> is null,
        /// <c>instance</c> runs <c>FindObjectOfType</c> and <c>new GameObject</c>).
        /// </summary>
        public static VolcanoAudioFacts ScanAudioFacts()
        {
            bool effectGroup = false;
            bool addEvent = false;
            bool clipApi = false;
            bool fileFound = false;
            long fileBytes = 0L;

            try
            {
                if (Singleton<AudioManager>.exists)
                {
                    effectGroup = Singleton<AudioManager>.instance.EffectGroup != null;
                }
            }
            catch
            {
                effectGroup = false;
            }

            try
            {
                addEvent = typeof(AudioManager).GetMethod(
                    "AddEvent",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[]
                    {
                        typeof(AudioGroup), typeof(AudioInfo), typeof(Vector3), typeof(Vector3),
                        typeof(float), typeof(float), typeof(float), typeof(int)
                    },
                    null) != null;
            }
            catch
            {
                addEvent = false;
            }

            try
            {
                // ★ Specify the parameter types too. Create has an old 6-argument _3D version
                //   (Obsolete) living alongside it, so a name-only lookup grabs the wrong one.
                bool create = typeof(AudioClip).GetMethod(
                    "Create",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool)
                    },
                    null) != null;

                bool setData = typeof(AudioClip).GetMethod(
                    "SetData",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[] { typeof(float[]), typeof(int) },
                    null) != null;

                clipApi = create && setData;
            }
            catch
            {
                clipApi = false;
            }

            try
            {
                string path = FilePath();
                if (path != null && File.Exists(path))
                {
                    fileFound = true;
                    fileBytes = new FileInfo(path).Length;
                }
            }
            catch
            {
                fileFound = false;
                fileBytes = 0L;
            }

            _facts = new VolcanoAudioFacts(effectGroup, addEvent, clipApi, fileFound, fileBytes);
            _factsScanned = true;
            return _facts;
        }

        /// <summary>
        /// **Main thread, every frame.** It reads nothing but the <see cref="VolcanoHub"/>
        /// snapshot and changes not one piece of game state.
        ///
        /// On frames where nothing is erupting it **does nothing** (i.e. it stops calling
        /// <c>AddEvent</c>). That is the whole implementation of "stopping cleanly".
        /// </summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            try
            {
                UpdateStep(snapshot);
            }
            catch (Exception e)
            {
                // ★ Sound it once. This is on the per-frame path, so do not repeat Log.Error.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano eruption audio failed", e);
                }
                _detail = "the audio path threw " + e.GetType().Name;

                // ★★ **Do not call Destroy() here.** That resets <c>_loadAttempted</c> to false,
                //    so the next frame re-reads the 6 MB wav and falls over on the same exception
                //    again — **file I/O every frame**. Release only the objects and leave
                //    "already tried" standing (this city simply gets no sound).
                ReleaseObjects();
            }
        }

        private static void UpdateStep(VolcanoSnapshot snapshot)
        {
            // Not erupting = stop pushing. Vanilla folds it away over FadeSeconds.
            if (snapshot == null || !snapshot.Valid || !snapshot.EruptionActive) return;

            EnsureClip();
            // ★ Examine the references themselves every frame (never put them in an array).
            //   If destroyed, Unity's fake-null makes them compare equal to null. Do not rebuild
            //   here — loading happens once per city and a failure is not retried every frame.
            if (_clip == null || _info == null) return;

            if (!Singleton<AudioManager>.exists) return;
            AudioManager audio = Singleton<AudioManager>.instance;

            AudioGroup group = audio.EffectGroup;
            if (group == null) return;

            // The volume follows the eruption strength (the tornado does the same thing with
            // m_intensity).
            // **The player's sound-effects slider and mute are not applied to this value** —
            // AudioGroup applies those through m_cachedVolume / m_totalVolume.
            float unit = Clamp01(snapshot.EruptionIntensityUnit);
            float volume = MinVolumeUnit + (1f - MinVolumeUnit) * unit;

            Vec3 vent = snapshot.VentWorld;
            var position = new Vector3(vent.X, vent.Y, vent.Z);

            // The crater does not move, so velocity is 0 (no Doppler).
            // pitch stays at 1 — the eruption strength is expressed by volume. Move the loop's
            // playback rate and the fluctuation between segments turns straight into wobbling
            // pitch.
            audio.AddEvent(group, _info, position, Vector3.zero,
                           MaxDistanceMetres, volume, 1f, PlayerId);
        }

        /// <summary>
        /// Read the bundled wav and build one <see cref="AudioClip"/> and one
        /// <see cref="AudioInfo"/>.
        /// **Once per city, and never retried on failure** (do not re-read 6 MB every frame).
        ///
        /// ★ <b>This is the substance of "it works exactly as today without the file".</b>
        ///   Missing, corrupt, no API — each one merely leaves the reason in
        ///   <see cref="_detail"/> and returns quietly; not a single exception gets out.
        /// </summary>
        private static void EnsureClip()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            string path = FilePath();
            if (path == null)
            {
                _detail = "the mod folder could not be resolved, so no eruption sound is loaded";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            if (!File.Exists(path))
            {
                // ★ **Info, not Warn.** Deleting the file is the player's prerogative, and the
                //   result of deleting it is "a silent eruption, exactly as today". Not an anomaly.
                _detail = "no " + FileName + " in the mod's " + AudioFolderName
                          + " folder; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                _detail = FileName + " could not be read (" + e.GetType().Name
                          + "); the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            WavPcm pcm = WavPcm.Parse(bytes);
            if (!pcm.Valid)
            {
                _detail = FileName + " could not be parsed: " + pcm.Error
                          + "; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            // Cut just the peak out of a one-shot recording (the measurements are in the class doc
            // of LoopSlice). If the file has been replaced with one too short to cut, the original
            // waveform comes straight back.
            float[] samples = LoopSlice.Build(pcm.Samples, pcm.Channels, pcm.SampleRate,
                                              LoopStartSeconds, LoopLengthSeconds,
                                              LoopFadeSeconds);
            // ★ Apply it **after** the slicing and the crossfade. Apply it first and the fade
            //   ends up multiplying samples the boost has already altered, and the seam is no
            //   longer smooth.
            float boostedPeak = LoudnessBoost.Apply(samples, LoudnessGain);

            int frames = samples.Length / pcm.Channels;
            if (frames <= 0)
            {
                _detail = FileName + " holds no sample frame; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            AudioClip clip = AudioClip.Create("DisasterPlus_VolcanoEruption",
                                              frames, pcm.Channels, pcm.SampleRate, false);
            if (clip == null)
            {
                _detail = "AudioClip.Create returned nothing; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            if (!clip.SetData(samples, 0))
            {
                UnityEngine.Object.Destroy(clip);
                _detail = "AudioClip.SetData refused the samples; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            // ★ AudioInfo is not a PrefabInfo but a plain ScriptableObject, and ObtainClip()
            //   merely returns m_clip as is (measured in IL).
            //   So it is fine to assemble it at runtime — no prefab name is baked into the save.
            AudioInfo info = ScriptableObject.CreateInstance<AudioInfo>();
            if (info == null)
            {
                UnityEngine.Object.Destroy(clip);
                _detail = "AudioInfo could not be created; the eruption is silent";
                Log.Info("volcano eruption sound: " + _detail);
                return;
            }

            info.name = "DisasterPlus_VolcanoEruption";
            info.m_clip = clip;
            info.m_volume = 1f;      // the real volume comes from AddEvent's argument and the sound-effects slider
            info.m_pitch = 1f;
            info.m_fadeLength = FadeSeconds;
            info.m_loop = true;
            info.m_is3D = true;      // this is the gate for 3D attenuation (spatialBlend = 1)
            info.m_randomTime = false;
            info.m_variations = null;

            _clip = clip;
            _info = info;

            // ★ Report the gain applied and **the peak that actually came out**.
            //   If it is pinned at 1.00, the source has been replaced and is going through the
            //   ceiling (<see cref="LoudnessBoost"/> will not let it clip, but it does tell you
            //   that it is being squashed).
            _detail = "loaded " + pcm.SampleRate + " Hz, " + pcm.Channels + " ch, "
                      + pcm.BitsPerSample + " bit, "
                      + pcm.LengthSeconds.ToString("F1") + " s source -> "
                      + (frames / (float)pcm.SampleRate).ToString("F1") + " s loop, "
                      + "gain x" + LoudnessGain.ToString("F1")
                      + " (peak " + boostedPeak.ToString("F2") + ")";
            Log.Info("volcano eruption sound: " + _detail);
        }

        /// <summary>The absolute path of the bundled wav. null if the mod folder cannot be looked up.</summary>
        private static string FilePath()
        {
            string dir = LocaleLoader.ModDirectoryPath();
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(Path.Combine(dir, AudioFolderName), FileName);
        }

        /// <summary>
        /// **Main thread.** Fold the sound away. Idempotent.
        /// **Call on level unload, and when the sound is turned off in the settings.**
        ///
        /// ★ Neither the <c>AudioSource</c> nor the <c>GameObject</c> is this mod's, so do not
        ///   touch them (do not call <c>Stop()</c> either). We hold only those two objects, and
        ///   neither is a <c>Component</c>, so they do not go down with the <c>GameObject</c> —
        ///   **call <c>Object.Destroy</c> on them ourselves.** Skip this and one clip (several MB)
        ///   is left behind every time the player enters and leaves a city.
        ///
        /// ★ It is possible for a vanilla player to still be pointing at this clip at the moment
        ///   it is destroyed, but <c>UpdatePlayers</c> only touches the clip when
        ///   <c>m_notReady</c> is set, and <c>m_notReady</c> is only set when
        ///   <c>loadState != Loaded</c>.
        ///   A clip from <c>AudioClip.Create</c> + <c>SetData</c> is <c>Loaded</c> from the start,
        ///   so that branch is never entered (measured in IL).
        /// </summary>
        public static void Destroy()
        {
            ReleaseObjects();

            // This is the only place "already tried" is reset. **The next city (or turning the
            // setting back on) should read it again** — the file that was deleted in the previous
            // city may have come back, and it may equally have been deleted since.
            _loadAttempted = false;
            _detail = "not loaded yet";
        }

        /// <summary>
        /// Release only the two Unity objects we hold. **"Already tried" is not reset.**
        /// The exception path calls this one (see the reasoning in <see cref="Update"/>'s catch).
        /// Idempotent.
        /// </summary>
        private static void ReleaseObjects()
        {
            // Destroy info first. PlayerData / AudioPlayer match with Object.op_Equality, so the
            // moment it becomes fake-null it stops matching and gets released on that frame.
            if (_info != null) UnityEngine.Object.Destroy(_info);
            _info = null;

            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
