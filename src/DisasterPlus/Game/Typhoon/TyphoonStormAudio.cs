using System;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Typhoon;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>Where the storm sound is coming from. **A type that exists solely so the
    /// diagnostics can name it.**</summary>
    public enum TyphoonAudioSource
    {
        /// <summary>Not tried yet.</summary>
        None,

        /// <summary>The mod's bundled <c>Audio/typhoon-wind.wav</c>. **First choice when
        /// present.**</summary>
        BundledFile,

        /// <summary>Vanilla's tornado travel sound (<c>TornadoAI.m_vortexSound</c>'s
        /// <c>m_clip</c>).</summary>
        VanillaVortex,

        /// <summary>Vanilla's sea ambience (from
        /// <c>AudioProperties.m_ambients</c>).</summary>
        VanillaAmbient,

        /// <summary>Nothing could be played in this environment. **Not a fault** (the
        /// typhoon works exactly as before).</summary>
        Unavailable,
    }

    /// <summary>
    /// The typhoon's wind sound. <b>Main thread only</b> (all of Unity's audio assets
    /// are).
    ///
    /// ── What the owner pointed out (2026-08-22) ───────────────────────────
    ///
    /// &gt; The wind sound is a little underwhelming.
    ///
    /// **④ had not been playing a single sound.** All you could hear was vanilla's rain
    /// ambience (<c>Ambient Rain</c>) and the <c>ThunderClap</c> from lightning; **there
    /// was no wind sound at all** (confirmed by grepping the whole codebase).
    /// "Underwhelming" meant "absent", not "weak", so first we add one.
    ///
    /// ── ★ What to play (decided by measuring the shipped assets) ─────────
    ///
    /// The candidate waveforms were extracted from the assets and measured (band energy
    /// over a 4-second window):
    ///
    /// <code>
    /// Clip                      RMS    20-80  80-250 250-800 800-2.5k 2.5-8k  Steadiness
    /// tornado_travelling       0.227   0.3%   6.2%   65.8%   26.6%    1.1%   0.20-0.28
    /// rumbling_disaster_loop   0.315  43.6%  54.9%    1.5%    0.0%    0.0%   0.27-0.34
    /// earthquake_loop          0.355  60.1%  33.5%    4.7%    1.5%    0.1%   0.34-0.37
    /// ambient_sea              0.012   0.0%   0.8%   59.8%   36.9%    2.4%   0.01-0.02
    /// rain                     0.026   0.0%   0.5%    5.0%   33.7%   54.0%   0.02-0.03
    /// </code>
    ///
    /// <b><c>tornado_travelling</c> is the howl of the wind itself</b> — 66% of it in the
    /// low-mid band (250-800 Hz), steady noise with neither peaks nor troughs, and with
    /// **an RMS of 0.203 at the head against 0.205 at the tail** the loop seam is silent
    /// too. <c>ambient_sea</c> has the same spectral shape but only **one nineteenth** the
    /// RMS, and Unity's <c>AudioSource.volume</c> tops out at 1.0, so <b>it cannot be
    /// lifted</b> — play that as it is and you get precisely the "underwhelming" sound.
    /// <c>rain</c> is 54% in 2.5-8 kHz, i.e. a thin hiss, and adds no weight.
    ///
    /// ★★ <b><c>tornado_travelling</c> is a Natural Disasters asset, and that is
    ///   fine.</b> ④'s host is the <c>ThunderStormAI</c> prefab, and that itself exists
    ///   only in <c>Expansion3Prefabs</c> (= Natural Disasters).
    ///   <b>Any environment that can raise ④'s typhoon necessarily has this sound too.</b>
    ///   Even so we always check for <c>null</c>, and if we cannot get it we fall back to
    ///   the sea ambience, and failing that to silence (not one exception is raised).
    ///
    /// We set <c>pitch</c> below 1 to <b>add weight</b>. Unity's pitch is a
    /// straightforward playback speed, so the 250-800 Hz band that makes up 66% of it
    /// shifts straight down and the howl thickens. The stronger the typhoon the lower it
    /// goes (<see cref="PitchAt"/>).
    ///
    /// ── ★ We borrow only the <c>AudioClip</c>. We build the <c>AudioInfo</c> ourselves ──
    ///
    /// <c>AudioInfo</c> is a <b>shared <c>ScriptableObject</c></b>. Rewrite
    /// <c>m_volume</c> or <c>m_loop</c> and <b>the game's own tornado sound</b> is dragged
    /// along with it, and it stays in memory rather than in the save (the same shape as
    /// <c>§D-5</c> for particles). So we read only <c>m_clip</c> and make our own
    /// <c>AudioInfo</c> with <c>ScriptableObject.CreateInstance</c>
    /// (the same as ⑤'s <c>VolcanoEruptionAudio</c>; <c>AudioInfo</c> is not a
    /// <c>PrefabInfo</c>, so no prefab name is burnt into the save).
    ///
    /// ★★ <b>Do not destroy a borrowed clip in <see cref="Destroy"/>.</b>
    ///   It is not ④'s. Destroy it and <b>the game's own tornado goes silent</b>.
    ///   <see cref="_clipIsOurs"/> holds that distinction.
    ///
    /// ── ★ Play it in 2D ──────────────────────────────────────────
    ///
    /// <c>m_is3D = false</c>. A typhoon is not "a sound source over there" but
    /// <b>a phenomenon you are inside</b>, so we give it no direction.
    /// Instead we <b>set the volume from the wind speed equivalent where the camera is</b>
    /// (<c>TyphoonProfile.WindAt</c> plus <c>SquallLayout.StrengthOf</c> — exactly the same
    /// formula as the spray, so **what you see and what you hear never drift apart**).
    /// Outside the gale radius we do not call <c>AddEvent</c>, i.e. vanilla fades it out
    /// and packs it away.
    ///
    /// ── ★ The player's volume and mute always take effect ────────────────
    ///
    /// We merely feed it into <c>AudioManager.EffectGroup</c> and the game applies the
    /// sound-effects slider and the mute (⑤ IL facts document §H-23).
    /// <b>We create neither an <c>AudioSource</c> nor a <c>GameObject</c> of our own.</b>
    ///
    /// Call <c>AddEvent</c>, not <c>AddPlayer</c>. The latter disappears on any frame
    /// where it is called after <c>UpdatePlayers</c> (same §H-23).
    ///
    /// ★ <b>Once per frame and no more.</b> Queue twice under the same id and two
    ///   <c>PlayerData</c> entries line up, using up one of <c>EffectGroup</c>'s slots
    ///   (<c>m_maxActiveCount = 3</c>) on a single sound.
    ///   <b>That is why ④ plays only one sound</b> — so as not to steal the seats for the
    ///   thunder and collapse sounds.
    ///
    /// ── If you ever want to add a bundled file ────────────────────────────
    ///
    /// Just put <c>Audio/typhoon-wind.wav</c> (44.1 kHz / 16 bit, a recording of a steady
    /// gale, 10 seconds or more) into the mod folder's <c>Audio</c> directory and it takes
    /// priority from the next time a city is loaded (the same route as ⑤'s eruption
    /// sound). **Nothing is bundled at present.**
    /// </summary>
    public static class TyphoonStormAudio
    {
        /// <summary>Where the bundled wav goes (relative to the mod folder). Treated the
        /// same way as ⑤.</summary>
        public const string AudioFolderName = "Audio";

        /// <summary>The bundled wav's filename. **Nothing is bundled at present** (we use
        /// it if it is there).</summary>
        public const string FileName = "typhoon-wind.wav";

        /// <summary>The window cut out of the bundled wav for the loop (seconds). The same
        /// shape as ⑤.</summary>
        private const float LoopStartSeconds = 1.0f;

        private const float LoopLengthSeconds = 8.0f;

        private const float LoopFadeSeconds = 1.0f;

        /// <summary>
        /// The audible range (m). With <c>m_is3D = false</c> no distance attenuation is
        /// applied, but <c>AudioGroup.AddPlayer</c> puts <c>maxDistance</c> straight into
        /// the <c>AudioSource</c>, so we pass a value anyway.
        /// Vanilla's lightning passes 10000.
        /// </summary>
        private const float MaxDistanceMetres = 10000f;

        /// <summary>The volume ratio at a spray strength of 0. **Never make it 0** —
        /// every fluctuation in strength would sound like the audio cutting out.</summary>
        private const float MinVolumeUnit = 0.30f;

        /// <summary><c>AudioInfo.m_fadeLength</c> (seconds). **It must not be 0**
        /// (<c>m_fadeSpeed = 1 / this</c>, so the division goes to infinity).</summary>
        private const float FadeSeconds = 3f;

        /// <summary>The <c>pitch</c> for the weakest typhoon.</summary>
        private const float PitchWeak = 0.92f;

        /// <summary>The <c>pitch</c> for the strongest typhoon. **The lower the
        /// heavier.**</summary>
        private const float PitchStrong = 0.68f;

        /// <summary>
        /// The fixed player ID passed to <c>AddEvent</c>. Vanilla uses
        /// <c>InstanceID.RawData</c> (small positive numbers) and <c>m_effectPlayerID</c>
        /// (negative numbers counting down by 1 from 0), so we put a large positive
        /// constant far from both.
        /// **Make it different from ⑤'s eruption sound (0x7D15A570) too** (they can sound
        /// at the same time).
        /// </summary>
        private const int PlayerId = 0x7D15B004;

        // ── Main-side state (★ not an array; one reference at a time) ──────────

        private static AudioClip _clip;
        private static AudioInfo _info;

        /// <summary>
        /// Whether ④ created <see cref="_clip"/> (i.e. whether we may destroy it).
        /// **Destroy a borrowed clip and the game's own sound goes silent** (class doc).
        /// </summary>
        private static bool _clipIsOurs;

        private static TyphoonAudioSource _source = TyphoonAudioSource.None;

        /// <summary>Whether we have already tried loading in this city. **We do not try
        /// twice, even after a failure.**</summary>
        private static bool _loadAttempted;

        /// <summary>The most recent outcome (**English, for diagnostics**).</summary>
        private static string _detail = "not loaded yet";

        private static bool _errorLogged;
        private static float _lastVolume;

        public static TyphoonAudioSource Source { get { return _source; } }

        /// <summary>The volume most recently passed to <c>AddEvent</c> (0 means we are not
        /// playing).</summary>
        public static float LastVolume { get { return _lastVolume; } }

        /// <summary>
        /// The outcome in one line (**English**). It is the only clue should a future game
        /// update make the sound disappear, so it always names what we borrowed.
        /// </summary>
        public static string Detail { get { return _detail; } }

        /// <summary>
        /// **Main thread, every frame.** It reads nothing but <see cref="TyphoonHub"/>'s
        /// snapshot and changes not one piece of game state.
        ///
        /// On frames with no typhoon, and on frames outside the gale radius, it **does
        /// nothing** (i.e. it stops calling <c>AddEvent</c>). That is the whole
        /// implementation of "stopping cleanly".
        /// </summary>
        public static void Update(TyphoonSnapshot snapshot)
        {
            try
            {
                Step(snapshot);
            }
            catch (Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon storm sound failed", e);
                }
                _detail = "the audio path threw " + e.GetType().Name;
                _lastVolume = 0f;

                // ★★ **Do not call Destroy() here.** That resets _loadAttempted, so the
                //    next frame would load again and fall over with the same exception.
                ReleaseObjects();
            }
        }

        private static void Step(TyphoonSnapshot snapshot)
        {
            _lastVolume = 0f;

            if (snapshot == null || !snapshot.Valid || !snapshot.Active) return;

            var camera = VanillaParticles.CameraInfo();
            if (camera == null) return;

            Vector3 eye = camera.m_position;
            Vec3 centre = snapshot.Centre;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            // ★ Exactly the same formula as the spray. Do not let what you see and what
            //   you hear drift apart.
            float wind = TyphoonProfile.WindAt(distance, snapshot.Intensity,
                                               snapshot.Prefab.StormRadius);
            float strength = SquallLayout.StrengthOf(wind);
            if (!(strength > 0f)) return;

            EnsureClip();
            // ★ Look at the reference itself every frame. If it has been destroyed,
            //   fake-null makes it compare equal to null. We do not rebuild here (loading
            //   happens once per city).
            if (_clip == null || _info == null) return;

            if (!Singleton<AudioManager>.exists) return;
            AudioManager audio = Singleton<AudioManager>.instance;

            AudioGroup group = audio.EffectGroup;
            if (group == null) return;

            float volume = MinVolumeUnit + (1f - MinVolumeUnit) * strength;
            float pitch = PitchAt(snapshot.Intensity);

            // **The player's sound-effects slider and mute do not multiply into this
            // value** — AudioGroup applies those through m_cachedVolume / m_totalVolume.
            audio.AddEvent(group, _info, eye, Vector3.zero,
                           MaxDistanceMetres, volume, pitch, PlayerId);
            _lastVolume = volume;
        }

        /// <summary>
        /// The <c>pitch</c> at intensity <paramref name="intensity"/>.
        /// **The stronger the typhoon the lower, i.e. the heavier.** The range always
        /// stays within <see cref="PitchStrong"/> to <see cref="PitchWeak"/>.
        /// </summary>
        public static float PitchAt(byte intensity)
        {
            float unit = intensity / 255f;
            return PitchWeak + (PitchStrong - PitchWeak) * unit;
        }

        /// <summary>
        /// Prepare one clip and one <c>AudioInfo</c>.
        /// **Once per city only. No retry after a failure.**
        /// </summary>
        private static void EnsureClip()
        {
            if (_loadAttempted) return;
            _loadAttempted = true;

            // 1. The bundled file (first choice when present. **Nothing is bundled at
            //    present**).
            if (LoadBundled()) return;

            // 2. Vanilla's tornado travel sound. **Any environment that can raise ④ has
            //    it** (class doc).
            if (BorrowVanilla(VortexClip(), TyphoonAudioSource.VanillaVortex,
                              "the game's tornado wind loop")) return;

            // 3. The sea ambience. **Measured, it is only one nineteenth the volume of the
            //    tornado loop**, so if we fall this far we say "thin" about ourselves.
            if (BorrowVanilla(AmbientClip(), TyphoonAudioSource.VanillaAmbient,
                              "the game's sea ambience (much quieter than the tornado "
                              + "loop, so the storm will sound thin)")) return;

            _source = TyphoonAudioSource.Unavailable;
            _detail = "no usable wind loop in this build; the typhoon is silent "
                      + "(everything else about it is unaffected)";
            Log.Info("typhoon storm sound: " + _detail);
        }

        /// <summary>The bundled wav (if present). Exactly the same procedure as ⑤'s
        /// eruption sound.</summary>
        private static bool LoadBundled()
        {
            string path = FilePath();
            if (path == null || !File.Exists(path)) return false;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Log.Info("typhoon storm sound: " + FileName + " could not be read ("
                         + e.GetType().Name + "); falling back to a vanilla loop");
                return false;
            }

            WavPcm pcm = WavPcm.Parse(bytes);
            if (!pcm.Valid)
            {
                Log.Info("typhoon storm sound: " + FileName + " could not be parsed ("
                         + pcm.Error + "); falling back to a vanilla loop");
                return false;
            }

            float[] samples = LoopSlice.Build(pcm.Samples, pcm.Channels, pcm.SampleRate,
                                              LoopStartSeconds, LoopLengthSeconds,
                                              LoopFadeSeconds);
            int frames = samples.Length / pcm.Channels;
            if (frames <= 0) return false;

            AudioClip clip = AudioClip.Create("DisasterPlus_TyphoonWind",
                                              frames, pcm.Channels, pcm.SampleRate, false);
            if (clip == null) return false;

            if (!clip.SetData(samples, 0))
            {
                UnityEngine.Object.Destroy(clip);
                return false;
            }

            if (!Wrap(clip, true))
            {
                UnityEngine.Object.Destroy(clip);
                return false;
            }

            _source = TyphoonAudioSource.BundledFile;
            _detail = "loaded " + FileName + " (" + pcm.SampleRate + " Hz, " + pcm.Channels
                      + " ch, " + pcm.LengthSeconds.ToString("F1") + " s source -> "
                      + (frames / (float)pcm.SampleRate).ToString("F1") + " s loop)";
            Log.Info("typhoon storm sound: " + _detail);
            return true;
        }

        /// <summary>
        /// Borrow one vanilla clip. **We build the <c>AudioInfo</c> ourselves** (we do not
        /// touch a single byte of the source's shared <c>ScriptableObject</c>. Class doc).
        /// </summary>
        private static bool BorrowVanilla(AudioClip clip, TyphoonAudioSource source,
                                          string what)
        {
            if (clip == null) return false;
            if (!Wrap(clip, false)) return false;

            _source = source;
            _detail = "borrowed " + what + " (\"" + clip.name + "\", "
                      + clip.length.ToString("F1") + " s, looped, 2D)";
            Log.Info("typhoon storm sound: " + _detail);
            return true;
        }

        /// <summary>
        /// Wrap the clip in ④'s own <c>AudioInfo</c>.
        /// <see cref="Destroy"/> destroys the clip only when <paramref name="ours"/> is
        /// true (a borrowed one is left alone).
        /// </summary>
        private static bool Wrap(AudioClip clip, bool ours)
        {
            AudioInfo info = ScriptableObject.CreateInstance<AudioInfo>();
            if (info == null) return false;

            info.name = "DisasterPlus_TyphoonWind";
            info.m_clip = clip;
            info.m_volume = 1f;      // the real volume comes from AddEvent's argument and the SFX slider
            info.m_pitch = 1f;       // pitch is also passed every frame as an AddEvent argument
            info.m_fadeLength = FadeSeconds;
            info.m_loop = true;
            // ★ 2D. A typhoon is not "a sound source over there" but a phenomenon you are
            //   inside.
            info.m_is3D = false;
            info.m_randomTime = false;
            info.m_variations = null;

            _clip = clip;
            _info = info;
            _clipIsOurs = ours;
            return true;
        }

        /// <summary>
        /// Vanilla's tornado travel sound (<c>TornadoAI.m_vortexSound.m_clip</c>).
        /// **null if the prefab is absent** (an environment without Natural Disasters —
        /// though ④'s typhoon cannot be raised there in the first place. Class doc).
        /// </summary>
        private static AudioClip VortexClip()
        {
            try
            {
                DisasterInfo info = DisasterManager.FindDisasterInfo<TornadoAI>();
                if (info == null) return null;

                var ai = info.m_disasterAI as TornadoAI;
                if (ai == null) return null;

                AudioInfo sound = ai.m_vortexSound;
                return sound != null ? sound.m_clip : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// One sea sound out of vanilla's ambiences. **Picked by name** —
        /// <c>AudioProperties.m_ambients</c> gives us nothing but names to go on.
        /// null if we cannot get one (we fall through to silence).
        /// </summary>
        private static AudioClip AmbientClip()
        {
            try
            {
                if (!Singleton<AudioManager>.exists) return null;

                var properties = Singleton<AudioManager>.instance.m_properties;
                if (properties == null || properties.m_ambients == null) return null;

                AudioClip best = null;
                for (int i = 0; i < properties.m_ambients.Length; i++)
                {
                    AudioInfo info = properties.m_ambients[i];
                    if (info == null || info.m_clip == null) continue;

                    string clipName = info.m_clip.name;
                    if (clipName == "ambient_sea") return info.m_clip;
                    if (best == null && clipName == "ambient_stream") best = info.m_clip;
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The absolute path to the bundled wav. null if the mod folder cannot be
        /// resolved.</summary>
        private static string FilePath()
        {
            try
            {
                string dir = LocaleLoader.ModDirectoryPath();
                if (string.IsNullOrEmpty(dir)) return null;
                return Path.Combine(Path.Combine(dir, AudioFolderName), FileName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// **Main thread.** Pack the sound away. Idempotent.
        /// **Call on level unload and when the sound is switched off in the settings.**
        ///
        /// ★ Neither the <c>AudioSource</c> nor the <c>GameObject</c> belongs to this mod,
        ///   so we do not touch them (we do not call <c>Stop()</c> either). Stop calling
        ///   <c>AddEvent</c> and vanilla fades it over <see cref="FadeSeconds"/> and
        ///   releases it.
        /// </summary>
        public static void Destroy()
        {
            ReleaseObjects();
            _loadAttempted = false;
            _source = TyphoonAudioSource.None;
            _detail = "not loaded yet";
            _lastVolume = 0f;
        }

        /// <summary>
        /// Let go of the Unity objects we hold. **It does not reset "we already tried"**
        /// (the exception path calls this one). Idempotent.
        /// </summary>
        private static void ReleaseObjects()
        {
            // Destroy info first. PlayerData / AudioPlayer matching goes through
            // Object.op_Equality, so the moment it becomes fake-null it stops matching and
            // is sent for release on that frame.
            if (_info != null) UnityEngine.Object.Destroy(_info);
            _info = null;

            // ★★ **Do not destroy a borrowed clip** (the game's own tornado would go
            //    silent).
            if (_clipIsOurs && _clip != null) UnityEngine.Object.Destroy(_clip);
            _clip = null;
            _clipIsOurs = false;
        }
    }
}
