using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Diagnostic logging. output_log.txt lives at
    /// &lt;Steam&gt;\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt (not under AppData).
    /// Diag throttles per key. Letting it pour out every tick makes the log useless.
    ///
    /// Thread safety: Diag is called from both the sim thread (FireWhirlFeature /
    /// FireWhirlDamage / FireWhirlSpawner / FireWhirlPinner) and the main thread
    /// (IntensityUnlock / DisasterPanelBar / FireWhirlPlacementTool).
    /// System.Collections.Generic.Dictionary is not safe for concurrent reads and writes:
    /// if the sim thread calls TryGetValue while the main thread is inserting a new key and
    /// reallocating the buckets, you get either an exception or an endless loop over a
    /// corrupted bucket chain (i.e. a hang with no stack trace). Every access to _lastDiag
    /// is serialised through the single _diagGate (the same discipline as
    /// FeatureHost._errorGate). Never call into another class while holding this lock
    /// (Debug.Log also happens outside the lock).
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[DisasterPlus] ";

        /// <summary>
        /// Throttle interval for one key (sim frames).
        ///
        /// UnityEngine.Time.realtimeSinceStartup cannot be used as the time source.
        /// UnityEngine.Time is main-thread only, and this Unity build gives no guarantee for
        /// reads from the sim thread. Use SimulationManager.m_currentFrameIndex instead
        /// (measured from the IL: a public instance field on SimulationManager, type
        /// System.UInt32). It is a plain uint field, so reading it from either thread is safe.
        ///
        /// Why 512: measured from the IL, SimulationManager.Update advances the frame as
        ///   m_referenceTimer += Time.deltaTime / Time.fixedDeltaTime * FinalSimulationSpeed
        /// so frames advance at "(1 / fixedDeltaTime) × game speed" per real second.
        /// The value of fixedDeltaTime is set on the Unity project-settings side — there is
        /// not a single call to set_fixedDeltaTime anywhere in Assembly-CSharp (confirmed by
        /// scanning the IL of every method) — so with Unity's default of 0.02 that is
        /// 50 frames per second at normal speed, making 512 frames the equivalent of the
        /// old 10 real seconds. At game speed 2/3 it shortens proportionally, but this only
        /// caps the volume of logging, so it need not match real seconds exactly.
        /// </summary>
        private const uint DiagIntervalFrames = 512;

        private static readonly object _diagGate = new object();
        private static readonly Dictionary<string, uint> _lastDiag = new Dictionary<string, uint>();

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message, System.Exception e)
        {
            Debug.LogError(Prefix + message + (e == null ? "" : " :: " + e));
        }

        /// <summary>
        /// Emits at most once per DiagIntervalFrames for the same key.
        ///
        /// This channel-less form counts as General (design doc 5.1). It delegates to the
        /// overload that takes a channel, so the "General" checkbox on the settings screen
        /// really can silence this path. General is in the default mask, so existing calls
        /// look no different than before.
        /// </summary>
        public static void Diag(string key, string message)
        {
            Diag(DisasterPlus.Core.Diagnostics.LogChannel.General, key, message);
        }

        /// <summary>
        /// Diagnostic logging with a channel. Emits nothing if the mask disables it.
        ///
        /// Do not move existing calls onto feature channels in this phase (design doc 5.2).
        /// Doing so would turn them off by default and break the procedure in
        /// docs/playtest-checklist.md.
        ///
        /// The mask is checked before the throttle. The other way round, a call on a channel
        /// that is OFF would consume the throttle slot, so the first call after switching the
        /// channel ON would be silently dropped.
        /// </summary>
        public static void Diag(int channel, string key, string message)
        {
            if (!DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask())) return;
            if (!ShouldEmit(key)) return;
            Debug.Log(Prefix + "DIAG " + key + ": " + message);
        }

        /// <summary>
        /// Whether Diag on this channel could be emitted with the current settings.
        ///
        /// <see cref="Diag(int, string, string)"/> performs the same check itself, so this
        /// exists **not for correctness, but purely to avoid the cost of evaluating the
        /// arguments**. C# evaluates the arguments fully before the call, so however much
        /// Diag rejects on the inside, the string concatenation and ToString() have already
        /// happened. On a path that calls a channel that is OFF by default (Forecast and the
        /// like) every sim tick, all of that assembly work is wasted (raised in the overall
        /// review).
        ///
        /// The throttle check (<see cref="ShouldEmit"/>) is deliberately not consulted here.
        /// If it were, either this query itself would consume the slot, or the caller would
        /// end up branching on the state of the slot. Diag may well emit nothing even when
        /// this returns true (and that is correct).
        /// </summary>
        public static bool DiagEnabled(int channel)
        {
            return DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask());
        }

        /// <summary>Call on level unload. Never carry throttle state across cities.</summary>
        public static void Reset()
        {
            lock (_diagGate) { _lastDiag.Clear(); }
        }

        private static bool ShouldEmit(string key)
        {
            uint now = CurrentFrame();
            lock (_diagGate)
            {
                uint last;
                // Do the subtraction in uint. If loading a save winds the frame number back,
                // the difference just becomes a huge value; it never falls on the
                // "don't emit" side.
                if (_lastDiag.TryGetValue(key, out last) && now - last < DiagIntervalFrames) return false;
                _lastDiag[key] = now;
                return true;
            }
        }

        private static uint CurrentFrame()
        {
            // When sInstance is null, Singleton<T>.instance runs Object.FindObjectOfType and
            // new GameObject + AddComponent (measured from the IL). Both are main-thread-only
            // APIs, so check exists first to keep the sim thread off them
            // (exists is only a null check on a static field — measured from the IL).
            // Returns 0 if it is not there yet, e.g. on the main menu. In that case each key
            // emits once and is suppressed afterwards, but the goal of not flooding the log
            // still holds.
            if (!SimulationManager.exists) return 0u;
            return SimulationManager.instance.m_currentFrameIndex;
        }

        private static int CurrentMask()
        {
            try
            {
                ModSettings.Ensure();
                return ModSettings.LogChannelMask.value;
            }
            catch
            {
                // Never let logging throw in a situation where the settings are not ready yet.
                return DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask;
            }
        }
    }
}
