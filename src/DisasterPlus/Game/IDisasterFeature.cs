namespace DisasterPlus.Game
{
    /// <summary>
    /// One disaster feature. ① forecast, ② earthquake, ④ typhoon and ⑤ volcano are all meant
    /// to slot in the same way. Mod.cs only walks this list, so adding a feature does not
    /// mean changing existing code.
    /// </summary>
    public interface IDisasterFeature
    {
        string Name { get; }

        /// <summary>Once the city has finished loading. Main thread.</summary>
        void OnLevelLoaded();

        /// <summary>After a simulation tick. Sim thread. Create and modify buffers from here.</summary>
        /// <param name="deltaMinutes">In-game time since the last call (minutes). 0 while paused.</param>
        void OnSimulationTick(uint frameIndex, float deltaMinutes);

        /// <summary>Every frame. Main thread. Touch Unity objects only from here.</summary>
        void OnMainThreadUpdate();

        /// <summary>On city unload. Drop all session state and static caches here.</summary>
        void OnLevelUnloading();

        /// <summary>
        /// Write your own state out as diagnostic lines. Called from the sim thread.
        /// Only called when collection is enabled, so there is no need to fret over the cost.
        ///
        /// Putting this on the interface is deliberate: it makes it impossible to forget the
        /// diagnostics implementation when adding features ② to ⑤.
        /// </summary>
        void WriteDiagnostics(DiagnosticBuilder b);
    }
}
