namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// Detects a run of "the spread test keeps picking buildings, yet not one of them
    /// catches fire".
    ///
    /// Why it is needed: one of the IL assumptions ③'s implementation got wrong was
    /// "writing Building.m_fireIntensity directly sets a building alight", when in reality
    /// nothing consumes that value unless the AI derives from CommonBuildingAI. That mix-up
    /// raised no exception and logged nothing — the spread simply, quietly, did not happen.
    /// An existence check (can BuildingAI.BurnBuilding be resolved?) passes, so the only way
    /// to catch it is the behaviour: "after the call, did nothing burn?".
    ///
    /// It lives in Core as a pure, engine-free counter, pinned down by unit tests.
    /// </summary>
    public class BarrenSpreadTracker
    {
        /// <summary>
        /// How many consecutive rounds of "we tried to set buildings alight and got zero"
        /// count as a fault.
        ///
        /// The spread test runs once per 16 sim frames' worth of in-game time (about 0.35
        /// in-game minutes by default), so one whirl's lifetime (10 minutes by default)
        /// works out at roughly 28 rounds.
        ///
        /// 24 rounds ≒ 8.4 in-game minutes. That amounts to "not one building that should
        /// have been burnable ever caught fire" across the whole life of a single whirl.
        ///
        /// 8 rounds was too short. 8 rounds is 2.8 in-game minutes = under 3 seconds of real
        /// time at speed 1, and at the time the evidence was also polluted with "things
        /// vanilla refuses by design" (rubble, parks, fire stations), so a perfectly healthy
        /// city always tripped it. The evidence side has since been cleaned up by
        /// FireWhirlDamage.CanBurn, so the buildings now counted in attempted are only ones
        /// vanilla ought to accept — meaning even a single empty round is genuinely
        /// suspicious — but there may be circumstances we do not know about (another mod
        /// intercepting BurnBuilding, say), so we pile up plenty of evidence before naming
        /// it.
        ///
        /// Stretching the threshold costs almost nothing. The failure we want to detect (a
        /// regression back to writing m_fireIntensity directly, for instance) means "nothing
        /// ever burns" forever, so whatever count we pick it will still be caught. A false
        /// positive, on the other hand, cries wolf and costs this whole facility its
        /// credibility.
        ///
        /// Note that we count "we actually called BurnBuilding" (attempted), not "it was
        /// picked" (selected). IgnitionSpread.Select excludes buildings that are already
        /// burning, but any that start burning after the selection are rejected on the
        /// FireWhirlDamage.Ignite side, so without attempted we would wrongly flag "a huge
        /// fire where everything nearby is already alight" as a fault.
        /// </summary>
        public const int DefaultThreshold = 24;

        private readonly int _threshold;
        private int _streak;
        private bool _tripped;

        public BarrenSpreadTracker() : this(DefaultThreshold) { }

        public BarrenSpreadTracker(int threshold)
        {
            _threshold = threshold < 1 ? 1 : threshold;
        }

        /// <summary>How many rounds in a row have come up empty.</summary>
        public int Streak { get { return _streak; } }

        /// <summary>Whether the threshold has been reached. It does not come back down
        /// until Record observes an ignition.</summary>
        public bool Tripped { get { return _tripped; } }

        public int Threshold { get { return _threshold; } }

        /// <summary>
        /// Records the result of one round of the spread test.
        /// </summary>
        /// <param name="attempted">The number of buildings BurnBuilding was actually called
        /// on.</param>
        /// <param name="ignited">The number of buildings that were successfully set
        /// alight.</param>
        /// <returns>True only when this record is the one that reaches the threshold (so
        /// that the log is emitted exactly once).</returns>
        public bool Record(int attempted, int ignited)
        {
            if (ignited > 0)
            {
                // It is working. Throw away all the evidence.
                _streak = 0;
                _tripped = false;
                return false;
            }

            // A round with zero attempts is not evidence (there was simply nothing to burn).
            // Nor do we reset. The evidence keeps accumulating across quiet stretches.
            if (attempted <= 0) return false;

            _streak++;
            if (_tripped || _streak < _threshold) return false;

            _tripped = true;
            return true;
        }

        /// <summary>For level unload. Evidence is never carried over between cities.</summary>
        public void Reset()
        {
            _streak = 0;
            _tripped = false;
        }
    }
}
