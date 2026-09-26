namespace DisasterPlus.Core.Diagnostics
{
    /// <summary>
    /// Bit flags for the log channels.
    ///
    /// These bit positions are a public contract saved into the .cgs, and are treated as
    /// frozen. Never repack or reorder the values. Even when a channel is retired, keep its
    /// bit and merely drop it from the UI.
    ///
    /// Note: Assembly-CSharp (the game itself) also has a completely different LogChannel
    /// type in the global namespace. If you add `using DisasterPlus.Core.Diagnostics;` to a
    /// file under Game/, C# name resolution prefers types from the enclosing namespaces
    /// (including the global namespace) over the ones brought in by a using, so a careless
    /// `using` silently resolves to the game's LogChannel rather than this type, and you
    /// find out via CS0117 (no such member). When referring to it from Game/, fully qualify
    /// it as `DisasterPlus.Core.Diagnostics.LogChannel`
    /// (a worked example: src/DisasterPlus/Game/Mod.cs).
    /// </summary>
    public static class LogChannel
    {
        public const int General    = 1;
        public const int FireWhirl  = 2;
        public const int Forecast   = 4;
        public const int Earthquake = 8;
        public const int Typhoon    = 16;
        public const int Volcano    = 32;
        public const int Diagnostics = 64;

        /// <summary>
        /// The default mask. Only General is enabled.
        ///
        /// Never make it 0. Existing calls to Log.Diag(key, msg) without a channel are
        /// treated as General, so setting it to 0 would silently remove the diagnostic
        /// logging we get today and break the on-hardware verification steps that
        /// docs/playtest-checklist.md refers to.
        /// </summary>
        public const int DefaultMask = General;

        /// <summary>True if every bit of channel is set in mask.</summary>
        public static bool IsEnabled(int channel, int mask)
        {
            if (channel == 0) return false;
            return (mask & channel) == channel;
        }
    }
}
