using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Adds to vanilla's camera shake **the part that accounts for intensity and
    /// distance**. **Main thread only, every frame.**
    ///
    /// ── What is being fixed (IL facts doc §A-7) ─────────────────────
    ///
    /// The amplitude in <c>EarthquakeAI.RenderInstance</c> is
    /// <c>0.3 / (1 + dist*0.001)</c>, and **<c>m_intensity</c> is not in the formula at
    /// all**. An earthquake of intensity 25.5 shakes the screen exactly as hard as one of
    /// 5.5. This is the part of the request's "there is no notion of a seismic intensity
    /// distribution" that you feel most directly.
    ///
    /// The fix is **purely additive**. Vanilla's calculation is neither suppressed nor
    /// replaced (no Harmony needed):
    ///
    /// <code>
    /// added = the same wave as vanilla's × (intensity / 55 - 1)
    /// total = vanilla's shake × (intensity / 55)
    /// </code>
    ///
    ///   - At intensity 55 (<c>CreateDisaster</c>'s default) the added part is
    ///     **exactly 0**. In that case this class **writes nothing at all** to
    ///     <c>m_cameraShake</c>, so the behaviour is bit-for-bit identical to vanilla.
    ///     That is what makes it safe to have this on by default.
    ///   - Below 55 it is negative. Weaker earthquakes shake less.
    ///   - The ceiling is two-stage: <see cref="ShakeWaveform.MaxIntensityFactor"/> and
    ///     <see cref="MaxAddedShake"/>. A screen made unusable at intensity 255 is not
    ///     drama, it is a defect.
    ///   - If <c>DisasterManager.m_disableCameraShake</c> is true, **nothing is added**.
    ///     The player has said "do not shake the camera", and that outranks this feature.
    ///
    /// ── Three things needed to stay in phase with vanilla ───────────────
    ///
    ///   1. The time comes from <c>m_referenceFrameIndex</c> and <c>m_referenceTimer</c>
    ///      (the render-side clock), not <c>m_currentFrameIndex</c>. Both are owned by the
    ///      main thread.
    ///   2. The distance is measured **in the same camera space as vanilla's**
    ///      (<c>InverseTransformPoint</c>, then <c>z *= 0.25</c>, then the length).
    ///      Switch to epicentral distance and the same wave gets a different amplitude,
    ///      which distorts the shape. The observer-point version (distance from the
    ///      hypocentre) is the waveform graph's job (Task 8) and is a different thing
    ///      entirely — a different evaluation of the same formula, not an approximation
    ///      (design doc §3.5).
    ///   3. The window is the same too (do nothing when <c>e &lt;= 0</c> or
    ///      <c>e &gt;= m_activeDuration</c>). <c>m_activeDuration</c> is **a prefab value
    ///      nobody has measured yet**, so when it cannot be read
    ///      <see cref="ShakeWaveform.IsShaking"/> returns false and this class adds
    ///      nothing. Add without knowing the window and the shaking carries on after the
    ///      earthquake has ended. **Never hard-code a magnitude.**
    ///
    /// ── Layer 2: swapping in the synthetic seismogram
    ///    (<c>ModSettings.EarthquakeSeismogram</c>) ──
    ///
    /// The answer to request ②, "the shaking is not realistic (the same waveform repeats
    /// over and over)". **Off by default.** When on, this class writes
    ///
    /// <code>
    /// added = synthetic seismogram × (intensity / 55) − vanilla's term
    /// </code>
    ///
    /// Vanilla's term is **what this class computed with vanilla's own formula, at the
    /// same point, over the same window** (the three points above), so the total comes
    /// out as exactly the synthetic seismogram. Neither Harmony nor reflection is needed
    /// — <c>m_cameraShake</c> is a field consumed by addition, so the cancellation can be
    /// written by addition too.
    ///
    /// **Why cancelling is legitimate** (§A-7's application path): in
    /// <c>DisasterManager.EndRenderingImpl</c>, vanilla calls <c>RenderInstance</c> for
    /// **every** disaster with <c>(m_flags &amp; 3) == Created</c>. There is no visibility
    /// test and no distance cut-off, so evaluating under the same conditions here (phase
    /// Emerging|Active, the <c>e</c> window, <c>m_disableCameraShake</c>) corresponds
    /// one-to-one with what vanilla adds. The residual is only the low bits of
    /// <c>Mathf.Sin</c>(float) versus <c>System.Math.Sin</c>(double).
    ///
    /// ★ **The path taken when this is off has not changed by a single instruction.**
    ///   The default route where intensity 55 gives exactly 0 added is still there (and
    ///   that is the only thing that makes it safe to have this feature on by default).
    ///
    /// ── Why nothing is left behind ──────────────────────────────
    ///
    /// The **last line** of <c>CameraController.LateUpdate</c> is
    /// <c>m_cameraShake = Vector3.zero</c> (IL_0281-0287, measured as part of this task).
    /// Consumption and reset run every frame, so stop adding and the offset is certain to
    /// be gone by the next frame. That is exactly why we must **keep adding every
    /// frame** (once per sim tick would only shake 1 frame in n).
    ///
    /// ── Constraints that come with being a per-frame path ───────────────
    ///
    /// No allocations and no logging. <c>Log.Warn</c> / <c>Log.Error</c> are not
    /// throttled, so even on an exception we shout once and then drop down to
    /// <c>Log.Diag</c>'s per-key throttle (the shape established by
    /// <c>EarthquakeReader._readErrorLogged</c>).
    /// </summary>
    public static class CameraShakeBooster
    {
        /// <summary>
        /// The ceiling on the absolute displacement that can be added in one frame.
        /// Vanilla's theoretical maximum is |sin + sin| = 2 × 0.3 = 0.6, so this is twice
        /// that.
        /// </summary>
        private const float MaxAddedShake = 1.2f;

        /// <summary>
        /// The ceiling on **the total the camera receives** when the synthetic seismogram
        /// is swapped in. Three times vanilla's theoretical maximum of 0.6, matching
        /// <c>ShakeWaveform.MaxIntensityFactor</c> (the intensity multiplier's ceiling of
        /// 3.0). **It caps the total, not the added part** — cap the added part and the
        /// cancellation breaks down, leaving vanilla's wave mixed in.
        /// </summary>
        private const float MaxTotalShake = 3f * ShakeWaveform.MaxDisplacement;

        /// <summary>How many earthquakes we remember at once (§E-1: several can run simultaneously).</summary>
        private const int MaxTrackedQuakes = 8;

        /// <summary>
        /// The table that **records the synthetic seismogram's observation distance once
        /// per earthquake**.
        ///
        /// The arrival times of the P and S waves are set by the distance, so measuring
        /// afresh every frame means **the arrival times move just because the camera
        /// moved** (the amplitude jumps mid-shake). We pin the distance from the first
        /// frame evaluated after entering the shaking window as that earthquake's
        /// observation distance, and free the slot once the quake is gone. ID 0 means
        /// "free".
        ///
        /// ★ **The key is the pair (ID, activation frame), not the ID alone.** Indices
        ///   into the disaster buffer are reused as soon as they are freed (§E-1), so
        ///   keying on the ID alone would let a new earthquake inherit the previous one's
        ///   observation distance.
        /// </summary>
        private static readonly ushort[] _trackedIds = new ushort[MaxTrackedQuakes];
        private static readonly uint[] _trackedFrames = new uint[MaxTrackedQuakes];
        private static readonly float[] _trackedDistances = new float[MaxTrackedQuakes];

        /// <summary>
        /// The <c>CameraController</c> reference. **Never test a static cache with a
        /// plain reference comparison** (in ③ we once held a destroyed object and died
        /// silently in the second city). Check it every time with Unity's <c>== null</c>
        /// (which also catches a fake-null) and re-fetch when it fails.
        /// </summary>
        private static CameraController _controllerCache;

        /// <summary>
        /// <c>Camera.main</c> in Unity 5.6 is a tag search, so it is not called every
        /// frame (the same shape as <c>ForecastPanel._mainCameraCache</c> /
        /// <c>EarthquakePanel</c>).
        /// </summary>
        private static Camera _mainCameraCache;

        private static bool _errorLogged;

        private static float _lastAdded;

        /// <summary>
        /// The magnitude of the displacement actually added on the most recent frame.
        /// **It exists purely for the diagnostic display.** In the game there is no other
        /// way to tell "it is working" from "it is adding 0 over and over" (you cannot
        /// tell the difference by eye from the shaking on screen).
        /// </summary>
        public static float LastAdded { get { return _lastAdded; } }

        /// <summary>On level unload. Carry neither references nor numbers across cities.</summary>
        public static void Reset()
        {
            _controllerCache = null;
            _mainCameraCache = null;
            _lastAdded = 0f;
            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                _trackedIds[i] = 0;
                _trackedFrames[i] = 0u;
                _trackedDistances[i] = 0f;
            }
            // _errorLogged is not reset. "It throws" is a fact about the game build this
            // DLL is referencing, not per-city state (the same judgement as
            // EarthquakeReader._readErrorLogged).
        }

        /// <summary>**Every frame, from the main thread.**</summary>
        public static void Update()
        {
            // Clear it first, so that a stale number does not linger in the diagnostics
            // while nothing is being applied.
            _lastAdded = 0f;

            if (!ModSettings.EarthquakeEnabled.value) return;
            if (!ModSettings.EarthquakeShakeBoost.value) return;

            try
            {
                var snapshot = EarthquakeHub.Latest;
                if (snapshot == null || !snapshot.Valid) return;

                // If m_activeDuration (vanilla's display window) could not be read, do
                // nothing. Let this through and the shaking carries on after the
                // earthquake has ended.
                if (!snapshot.Prefab.Resolved || snapshot.Prefab.ActiveDuration == 0u) return;

                var quakes = snapshot.Quakes;
                if (quakes.Count == 0) return;

                // ★ If the player has turned shaking off, this feature does not exist.
                //    Vanilla's own addition stops under the same condition (§A-7's
                //    EndRenderingImpl).
                if (!Singleton<DisasterManager>.exists) return;
                if (Singleton<DisasterManager>.instance.m_disableCameraShake) return;

                if (!SimulationManager.exists) return;
                var sim = SimulationManager.instance;

                // Only from here on do we go looking for Unity objects. On an ordinary
                // frame with no earthquakes at all, we never get past the early returns
                // above.
                var cam = ResolveMainCamera();
                if (cam == null) return;

                var controller = ResolveController();
                if (controller == null) return;

                Vector3 added = Accumulate(quakes, snapshot.Prefab.ActiveDuration, sim, cam);

                // ★ When the added part is exactly 0, **write nothing**. Intensity 55
                //    (vanilla's default) always lands here, so in that case the behaviour
                //    is bit-for-bit identical to vanilla.
                if (added.x == 0f && added.y == 0f && added.z == 0f) return;

                controller.m_cameraShake += added;
                _lastAdded = added.magnitude;
            }
            catch (System.Exception e)
            {
                // A per-frame path. Shout once, then drop to the per-key throttle.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("earthquake camera shake boost failed", e);
                }
                else
                {
                    Log.Diag("EqShake", "camera shake boost failed: " + e.GetType().Name);
                }
            }
        }

        /// <summary>
        /// Sums the added displacement over the earthquakes in progress and caps it at
        /// <see cref="MaxAddedShake"/>.
        ///
        /// It allocates nothing (<c>Vector3</c> is a struct and the list is walked by
        /// index).
        /// </summary>
        private static Vector3 Accumulate(IList<EarthquakeReading> quakes,
                                          uint activeDuration, SimulationManager sim, Camera cam)
        {
            bool seismogram = ModSettings.EarthquakeSeismogram.value;

            // ★ Sweep every frame regardless of whether the setting is on or off. Stop
            //   sweeping while it is off and, when it is next turned on, the previous
            //   earthquake's slots are still occupied.
            ForgetGoneQuakes(quakes);

            Vector3 added = Vector3.zero;

            // Only used when swapping in the synthetic seismogram. target is "the total
            // the camera should receive", vanilla is "what vanilla adds by itself", and
            // we write only the difference.
            Vector3 target = Vector3.zero;
            Vector3 vanilla = Vector3.zero;

            Transform camTransform = cam.transform;

            for (int i = 0; i < quakes.Count; i++)
            {
                var q = quakes[i];

                // The same condition as vanilla's (m_flags & 12) != 0. The phase bits are
                // mutually exclusive (§A-1, plus DisasterPhases' doc and its unit tests),
                // so comparing against the two phases is enough.
                if (q.Phase != EarthquakePhase.Emerging && q.Phase != EarthquakePhase.Active) continue;

                // m_activationFrame == 0 means "not yet decided", not "now". Use it in the
                // subtraction and you get an absurd e (the trap in §A-1).
                if (!q.ActivationScheduled) continue;

                float factor = ShakeWaveform.IntensityFactor(q.Intensity);
                // ★ At intensity 55 we bail out here (only while the synthetic seismogram
                //   is off). None of the trigonometry below runs either, so it is
                //   bit-for-bit identical to vanilla.
                if (!seismogram && factor == 0f) continue;

                long e = (long)sim.m_referenceFrameIndex - q.ActivationFrame + ShakeWaveform.FrameOffset;
                if (!ShakeWaveform.IsShaking(e, activeDuration)) continue;

                float t = e + sim.m_referenceTimer;

                // The same camera-space distance as §A-7 IL_003F. Copied right down to
                // multiplying z by 0.25.
                Vector3 v = camTransform.InverseTransformPoint(
                    new Vector3(q.Epicentre.X, q.Epicentre.Y, q.Epicentre.Z));
                v.z *= 0.25f;
                float cameraDistance = v.magnitude;

                // §A-7 IL_00AA / IL_00F2: the shaking is along one direction, normal to
                // the fault.
                float dirX = -Mathf.Sin(q.AngleRadians);
                float dirZ = Mathf.Cos(q.AngleRadians);

                if (!seismogram)
                {
                    float displacement = ShakeWaveform.DisplacementAt(cameraDistance, t) * factor;
                    if (displacement == 0f) continue;

                    added.x += displacement * dirZ;
                    added.z -= displacement * dirX;
                    continue;
                }

                // ── Layer 2: swap in the synthetic seismogram ────────────────
                // The observation distance is recorded once per earthquake (so that
                // moving the camera does not move the P/S arrival times; see
                // _trackedDistances' doc).
                float observed = ObservationDistance(q.DisasterId, q.ActivationFrame, cameraDistance);

                var model = SeismogramModel.For(
                    DeterministicRandom.Hash(q.DisasterId, q.ActivationFrame), activeDuration);

                // 1 + factor = intensity / 55 (already clamped). At intensity 55 it is
                // exactly unity.
                float wanted = model.DisplacementAt(observed, t) * (1f + factor);
                float replaced = ShakeWaveform.DisplacementAt(cameraDistance, t);

                target.x += wanted * dirZ;
                target.z -= wanted * dirX;
                vanilla.x += replaced * dirZ;
                vanilla.z -= replaced * dirX;
            }

            if (seismogram)
            {
                // ★ What is capped is **the total**, not the added part. Cap the added
                //   part and the cancellation breaks down, leaving the vanilla wave we
                //   meant to remove mixed back in.
                float total = target.magnitude;
                if (total > MaxTotalShake) target *= MaxTotalShake / total;
                return target - vanilla;
            }

            // Several earthquakes can run at once (§E-1), so the cap applies to the total.
            float magnitude = added.magnitude;
            if (magnitude > MaxAddedShake)
            {
                added *= MaxAddedShake / magnitude;
            }
            return added;
        }

        /// <summary>
        /// This earthquake's observation distance. **It remembers the distance from the
        /// first frame it was seen on, as it was.** If the table is full it returns the
        /// current distance without remembering it (only the memory is lost; the shaking
        /// still happens).
        /// </summary>
        private static float ObservationDistance(ushort disasterId, uint activationFrame,
                                                 float current)
        {
            if (disasterId == 0) return current;

            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                if (_trackedIds[i] == disasterId && _trackedFrames[i] == activationFrame)
                {
                    return _trackedDistances[i];
                }
            }

            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                if (_trackedIds[i] != 0) continue;
                _trackedIds[i] = disasterId;
                _trackedFrames[i] = activationFrame;
                _trackedDistances[i] = current;
                return current;
            }

            return current;
        }

        /// <summary>
        /// Frees the slots of earthquakes that are no longer shaking. **Without this the
        /// table stays full** and the next earthquake cannot record its observation
        /// distance (falling back to measuring afresh every frame). This is a per-frame
        /// path, so it allocates nothing (it only walks by index).
        /// </summary>
        private static void ForgetGoneQuakes(IList<EarthquakeReading> quakes)
        {
            for (int i = 0; i < MaxTrackedQuakes; i++)
            {
                ushort id = _trackedIds[i];
                if (id == 0) continue;

                bool alive = false;
                for (int j = 0; j < quakes.Count; j++)
                {
                    var q = quakes[j];
                    if (q.DisasterId != id || q.ActivationFrame != _trackedFrames[i]) continue;
                    alive = q.Phase == EarthquakePhase.Emerging || q.Phase == EarthquakePhase.Active;
                    break;
                }

                if (alive) continue;
                _trackedIds[i] = 0;
                _trackedFrames[i] = 0u;
                _trackedDistances[i] = 0f;
            }
        }

        private static Camera ResolveMainCamera()
        {
            // This is Unity's == null, so a destroyed (fake-null) camera is re-fetched.
            if (_mainCameraCache == null) _mainCameraCache = Camera.main;
            return _mainCameraCache;
        }

        private static CameraController ResolveController()
        {
            if (_controllerCache == null) _controllerCache = SceneObjects.FindInScene<CameraController>();
            return _controllerCache;
        }
    }
}
