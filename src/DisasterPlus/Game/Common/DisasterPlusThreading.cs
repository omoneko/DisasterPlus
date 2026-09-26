using ICities;

namespace DisasterPlus.Game
{
    public class DisasterPlusThreading : ThreadingExtensionBase
    {
        /// <summary>
        /// **Called when the mod is removed.**
        ///
        /// ★★ Leave this unimplemented and <b>a city where the mod was disabled mid-tsunami
        ///   is left with a water source</b> (2026-08-31, cross-review). Once the extension
        ///   is released, neither <c>OnAfterSimulationTick</c>, nor <c>OnSaveData</c>, nor
        ///   <c>OnLevelUnloading</c> ever comes again, so there is no way for it to end on
        ///   its own — it is burnt in at the next autosave, a spring that will not go away
        ///   even if the mod is deleted.
        ///
        /// ★ This is also called when an unrelated mod is toggled. The only loss in that case
        ///   is "a wave that was running ends early", so release without hesitation.
        /// </summary>
        public override void OnReleased()
        {
            // ★★ **Clear the reservation too.** (2026-08-31, second review round) Forget this
            //    and a sim tick that was still running would raise the reserved tsunami, after
            //    which there is no means left to release it.
            try { TsunamiChain.Reset(); }
            catch (System.Exception e) { Log.Error("TsunamiChain.Reset on release", e); }

            // ★ Do not put a one-way latch here (see TsunamiRing's ★★: OnReleased also comes
            //   every time you go back to the main menu). Clear the reservation, then release.
            try { TsunamiRing.Reset(); }
            catch (System.Exception e) { Log.Error("TsunamiRing.Reset on release", e); }

            base.OnReleased();
        }

        /// <summary>
        /// Sim thread. Creating and modifying the building, vehicle and disaster buffers
        /// happens only from here. Touch those from the main thread (OnUpdate) and an
        /// IndexOutOfRangeException with no stack trace turns up later, which your own
        /// try/catch will not catch either.
        /// </summary>

        public override void OnAfterSimulationTick()
        {
            FeatureHost.SimulationTick();
        }

        /// <summary>Main thread. Unity objects, rendering and UI only.</summary>
        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            FeatureHost.MainThreadUpdate();
        }
    }
}
