namespace DisasterPlus.Core.Typhoon
{
    using DisasterPlus.Core.Common;

    /// <summary>
    /// <b>Everything needed to draw a typhoon's track onwards into the future.</b> **Engine-free.**
    ///
    /// ── Why it is needed (2026-09-02, the owner's request) ────────────────────────
    ///
    /// &gt; Put a button on the city map that shows the typhoon's track, its gale radius and
    /// &gt; the wind distribution, and make it work.
    ///
    /// "Where it is now" is covered by the snapshot's <c>Centre</c>, but
    /// <b>"where it is going" cannot be had without re-deriving the track itself</b>.
    /// <see cref="TyphoonTrack"/> decides that from four things,
    /// <c>(origin, seed, speed, approachFrames)</c>, so this is the box that carries those
    /// four over to the drawing side.
    ///
    /// ★★ **Drawing happens on the main thread; the track is decided on the sim thread.**
    ///   That is why the drawing code must not read <c>TyphoonController</c>'s statics
    ///   directly (this project's thread-boundary discipline). **Put it on the snapshot and
    ///   carry it across.** It is a set of values never rewritten once made, so a struct
    ///   suffices.
    ///
    /// ★ Only <b>the track</b> can be predicted. The intensity includes landfall decay
    ///   (<see cref="TyphoonTrack.DecayAfter"/>), which depends on "will it pass over land
    ///   from here on", so **we do not claim a future intensity**. The gale-radius circle
    ///   only ever draws "the current radius at the current intensity".
    /// </summary>
    public struct TyphoonTrackPlan
    {
        /// <summary>The spot the player clicked (i.e. the track's reference point).</summary>
        public readonly Vec2 Origin;

        /// <summary>The track's random seed. The bearing and the curvature come out of
        /// this.</summary>
        public readonly uint Seed;

        /// <summary>Travel speed (m/frame). **0 means "the duration could not be read".**</summary>
        public readonly float Speed;

        /// <summary>How many frames until it reaches the clicked spot (the leg in from off the
        /// map).</summary>
        public readonly uint ApproachFrames;

        /// <summary>Lifetime (frames). The far end of how much track to draw.</summary>
        public readonly uint TotalFrames;

        public TyphoonTrackPlan(Vec2 origin, uint seed, float speed,
                                uint approachFrames, uint totalFrames)
        {
            Origin = origin;
            Seed = seed;
            Speed = speed;
            ApproachFrames = approachFrames;
            TotalFrames = totalFrames;
        }

        /// <summary>
        /// Whether the track can be drawn. **When this is false, do not draw a single line** —
        /// a speed of 0 means "the prefab's duration could not be read", not "it is standing
        /// still" (see the doc on <see cref="TyphoonTrack.SpeedFor"/>).
        /// Drawing a line from a value you could not read means drawing a guess on the map.
        /// </summary>
        public bool Usable
        {
            get { return Speed > 0f && TotalFrames > 0u; }
        }

        /// <summary>The centre <paramref name="elapsedFrames"/> after the typhoon was
        /// born.</summary>
        public Vec2 CentreAt(uint elapsedFrames)
        {
            return TyphoonTrack.CentreAt(Origin, Seed, elapsedFrames, Speed, ApproachFrames);
        }

        /// <summary>The bearing of travel (rad) at that same moment.</summary>
        public float HeadingAt(uint elapsedFrames)
        {
            return TyphoonTrack.HeadingAt(Origin, Seed, elapsedFrames, Speed, ApproachFrames);
        }

        /// <summary>
        /// Writes points along the track into <paramref name="into"/>. **It allocates
        /// nothing** (drawing happens every frame, so the caller reuses the array).
        ///
        /// Writes <paramref name="count"/> evenly spaced points from
        /// <paramref name="fromFrames"/> to <paramref name="toFrames"/> and returns how many
        /// were actually written. 0 if <see cref="Usable"/> is false.
        /// </summary>
        public int Sample(Vec2[] into, int count, uint fromFrames, uint toFrames)
        {
            if (into == null || count <= 0) return 0;
            if (!Usable) return 0;
            if (count > into.Length) count = into.Length;
            if (toFrames < fromFrames) return 0;

            if (count == 1)
            {
                into[0] = CentreAt(fromFrames);
                return 1;
            }

            uint span = toFrames - fromFrames;
            for (int i = 0; i < count; i++)
            {
                // ★ Divide in long and come back. span runs to the whole lifetime (tens of
                //   thousands), so computing span * i in uint overflows at large counts.
                uint at = fromFrames + (uint)((long)span * i / (count - 1));
                into[i] = CentreAt(at);
            }

            return count;
        }

        /// <summary>The value to use when nothing could be read. <see cref="Usable"/> comes
        /// out false.</summary>
        public static TyphoonTrackPlan None
        {
            get { return new TyphoonTrackPlan(new Vec2(0f, 0f), 0u, 0f, 0u, 0u); }
        }
    }
}
