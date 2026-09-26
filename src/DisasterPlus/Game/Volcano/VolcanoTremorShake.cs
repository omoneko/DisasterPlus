using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **The volcanic-earthquake shake.** Adds the ground motion produced by
    /// <c>Core/Volcano/VolcanicTremor</c> to the camera. **Main thread only, every frame.**
    ///
    /// ── how it is kept apart from ② (2026-08-22, the owner's request "volcanic earthquakes") ──
    ///
    /// | What was borrowed | From where |
    /// |---|---|
    /// | The technique of **adding** to <c>CameraController.m_cameraShake</c> | ②'s <c>CameraShakeBooster</c> |
    /// | The content of the shaking (swarm + tremor) | **⑤'s own** (in Core, like <c>VolcanoRelief</c>) |
    ///
    /// **It calls not one line of ②'s code and looks at not one of ②'s settings.**
    ///
    ///   - **The volcano shakes even with <c>eqShakeBoost</c> / <c>eqSeismogram</c> both OFF.**
    ///     Those are off by default because they <b>replace vanilla's earthquake camera shake</b>,
    ///     whereas ⑤'s volcano is ⑤'s own phenomenon that the player triggered themselves
    ///   - Conversely, even with ② ON, ⑤ never once evaluates ②'s composite seismogram. If both
    ///     happen at once they **add up** (<c>m_cameraShake</c> is a field consumed additively;
    ///     see the IL measurements in ②'s class doc). That is correct — erupt during an earthquake
    ///     and both should shake
    ///
    /// ★ <b>It triggers not one vanilla <c>EarthquakeAI</c>.</b> That carves fault cracks into the
    ///   map, which would cut across the very terrain cells ⑤ is writing a mountain into.
    ///
    /// ★ <b>It destroys not one building.</b> ⑤ has already <b>levelled the whole area under the
    ///   volcano</b> (the clearing, <c>VolcanoClearing</c>), so adding damage that is "small and
    ///   only near the mountain" would mean **destroying a place where nothing is standing any
    ///   more**. Destroying beyond the mountain is not "only near the mountain".
    ///   So this is <b>shaking only</b>: it calls neither <c>BuildingAI.CollapseBuilding</c> nor
    ///   <c>DisasterHelpers</c> (and obviously writes not one byte of
    ///   <c>Building.m_fireIntensity</c>).
    ///
    /// ── the evidence that nothing lingers (settled by ② in IL) ─────────────────────────────
    ///
    /// <b>The very last line</b> of <c>CameraController.LateUpdate</c> is
    /// <c>m_cameraShake = Vector3.zero</c>. The consume-and-reset runs every frame, so stop adding
    /// and it is certain to be gone on the next frame. Which is exactly why we
    /// **keep adding every frame**.
    ///
    /// ── the per-frame cost ─────────────────────────────────────────────────────────────────
    ///
    /// Around a dozen <c>Sin</c> / <c>Exp</c> calls and one <c>Vector3</c>. **Zero bytes of heap
    /// allocation, zero lines of log.** On frames where nothing is erupting it exits in the first
    /// three lines.
    /// </summary>
    public static class VolcanoTremorShake
    {
        /// <summary>
        /// The maximum shake displacement. <b>0.7×</b> ②'s vanilla theoretical maximum
        /// (<c>ShakeWaveform.MaxDisplacement</c> = 0.6). A volcanic earthquake feels strong up
        /// close, but <b>it is not a main-shock fault earthquake</b>.
        /// In the offline measurement (<c>docs/images/volcano/tremor-waveform.png</c>) the ground
        /// motion peaks at 0.55 for an activity of 1, so the real maximum directly under the
        /// crater is <c>0.55 × 0.42 ≒ 0.23</c> — **just under 40 % of vanilla's main shock**.
        /// </summary>
        private const float MaxDisplacement = VolcanoTremorActivity.DisplacementGain;

        /// <summary>
        /// How far the shaking carries (as a multiple of the mountain's radius). **Outside it is
        /// exactly 0** — shake the far side of the city and it reads not as "an earthquake is
        /// happening" but as "the screen is broken".
        /// </summary>
        private const float ReachRadiusFactor = VolcanoTremorActivity.ReachRadiusFactor;

        /// <summary>The phase offset used for the opposing component (radians). It makes the two horizontal axes look independent.</summary>
        private const float CrossPhaseSeconds = 0.37f;

        /// <summary>The ratio of vertical motion. Smaller than the horizontal, as in a real earthquake.</summary>
        private const float VerticalRatio = 0.45f;

        /// <summary>The clock (seconds). **Advanced the same way as the vanilla effect clock** (it stops when paused).</summary>
        private static float _clockSeconds;

        private static float _lastActivity;
        private static float _lastAdded;
        private static bool _errorLogged;

        /// <summary>
        /// The reference to <c>CameraController</c>. Under the same discipline as ②, it is held as
        /// **one reference, not an array**, checked every time with Unity's <c>== null</c> (which
        /// also catches fake-null) and looked up again if it has gone.
        /// </summary>
        private static CameraController _controller;

        private static Camera _mainCamera;

        /// <summary>The most recent activity level (for diagnostics). 0 means "not shaking".</summary>
        public static float ActivityUnit { get { return _lastActivity; } }

        /// <summary>The magnitude of the displacement actually added in the last frame (for diagnostics).</summary>
        public static float LastAdded { get { return _lastAdded; } }

        /// <summary>
        /// **Main thread.** Call on level unload and when turned off in the settings.
        /// It touches no Unity object; it only drops the references. Idempotent.
        /// </summary>
        public static void Reset()
        {
            _clockSeconds = 0f;
            _lastActivity = 0f;
            _lastAdded = 0f;
            _controller = null;
            _mainCamera = null;
            // _errorLogged is not reset (it is a fact about the build of the game).
        }

        /// <summary>**Main thread, every frame.**</summary>
        public static void Update(VolcanoSnapshot snapshot)
        {
            _lastAdded = 0f;

            try
            {
                Step(snapshot);
            }
            catch (System.Exception e)
            {
                // A per-frame path. Sound it loudly once, then drop to per-key throttling.
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano tremor shake failed", e);
                }
                else
                {
                    Log.Diag("volcanoTremor", "tremor shake failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoSnapshot snapshot)
        {
            float activity = ActivityFor(snapshot);
            _lastActivity = activity;

            if (!(activity > 0f))
            {
                _clockSeconds = 0f;
                return;
            }

            // ★ If the player has turned the shaking off, this feature does not exist
            //   (handled the same way as vanilla's disaster shake. ② stops on the same condition).
            if (!Singleton<DisasterManager>.exists) return;
            if (Singleton<DisasterManager>.instance.m_disableCameraShake) return;

            // ★ Not Time.deltaTime. It stops when paused and follows the game speed.
            float dt = VolcanoVanillaFx.EffectTimeDelta();
            if (dt > 0f) _clockSeconds += dt;
            if (dt <= 0f) return;

            Camera cam = ResolveCamera();
            if (cam == null) return;

            CameraController controller = ResolveController();
            if (controller == null) return;

            // ★★ **Measure from the centre of the affected range, not from the crater.**
            //    <c>VolcanoSnapshot.VentWorld</c> stays at <c>(0,0,0)</c> until the eruption (more
            //    precisely, the uplift) begins (it is only filled once <c>VolcanoEruption.Tick</c>
            //    has been called).
            //    Measure from that and **the clearing stage's shaking attenuates away with the
            //    distance from the origin** — "it shakes before the eruption" disappears entirely.
            Vec3 centre = snapshot.Footprint.Centre;
            Vector3 eye = cam.transform.position;
            float dx = eye.x - centre.X;
            float dz = eye.z - centre.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            float reach = snapshot.Footprint.RadiusMetres * ReachRadiusFactor;
            float attenuation = VolcanicTremor.AttenuationAt(distance, reach);
            if (!(attenuation > 0f)) return;

            uint seed = DeterministicRandom.Hash(
                unchecked((uint)Mathf.RoundToInt(centre.X)),
                unchecked((uint)Mathf.RoundToInt(centre.Z)));

            float a = VolcanicTremor.DisplacementAt(seed, _clockSeconds, activity);
            float b = VolcanicTremor.DisplacementAt(seed, _clockSeconds + CrossPhaseSeconds,
                                                    activity);

            float gain = MaxDisplacement * attenuation;
            var added = new Vector3(a * gain, b * gain * VerticalRatio, b * gain);

            if (added.x == 0f && added.y == 0f && added.z == 0f) return;

            controller.m_cameraShake += added;
            _lastAdded = added.magnitude;
        }

        /// <summary>
        /// The current activity level. **It is decided by the phase** (the snapshot is what sim
        /// wrote, and not one byte of it is rewritten here).
        /// </summary>
        private static float ActivityFor(VolcanoSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Valid) return 0f;
            if (!snapshot.Footprint.Valid) return 0f;
            if (!ModSettings.VolcanoEnabled.value) return 0f;
            if (!ModSettings.VolcanoQuake.value) return 0f;

            // ★ There is exactly one mapping table, in <see cref="VolcanoTremorActivity"/>.
            //   The sim side (the recording onto the seismograph) **must look at the same table** —
            //   write it separately and you get "the screen shakes but nothing appears in the
            //   record".
            return VolcanoTremorActivity.For(snapshot.Phase, snapshot.ProgressUnit,
                                             snapshot.EruptionIntensityUnit,
                                             snapshot.LavaCoolUnit);
        }

        private static Camera ResolveCamera()
        {
            // Camera.main in Unity 5.6 is a tag search, so do not call it every frame
            // (the same shape as ②'s CameraShakeBooster).
            if (_mainCamera != null) return _mainCamera;
            _mainCamera = Camera.main;
            return _mainCamera;
        }

        private static CameraController ResolveController()
        {
            if (_controller != null) return _controller;
            _controller = SceneObjects.FindInScene<CameraController>();
            return _controller;
        }
    }
}
