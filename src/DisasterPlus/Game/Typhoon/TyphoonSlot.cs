using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// The **single vanilla disaster slot** ④ holds. <b>Sim thread only.</b>
    ///
    /// ── Why this was split out of <see cref="TyphoonController"/> ─────────
    ///
    /// ④ has two jobs of entirely different character. One is **④'s own model** (the
    /// track, the intensity envelope, the landfall decay, the phase, the landfall
    /// forecast), which is arithmetic riding on the pure functions in
    /// <c>Core/Typhoon</c>, and getting it wrong only ever means "the typhoon moves
    /// oddly". The other is **writing into vanilla's disaster buffer**, where getting it
    /// wrong means <b>scribbling over somebody else's disaster slot</b>,
    /// <b>eating one slot for ever</b> or <b>dragging an unrelated disaster around</b> —
    /// all breakages with no exception (traps 1 and 2, and ID reuse).
    ///
    /// This type holds only the latter. **In ④, the code that touches
    /// <c>DisasterManager</c> / <c>DisasterData</c> / <c>DisasterAI</c> /
    /// <c>DisasterInfo</c> is here and nowhere else.** However many new elements T7 to T10
    /// add to ④, this boundary must not move.
    ///
    /// ── Trap 1: <c>SelfTrigger</c> (③ actually shipped this) ──────────────
    ///
    /// <c>ThunderStormAI.StartDisaster</c> looks at <c>m_flags &amp; 64</c> at
    /// <c>IL_000E</c> and **returns immediately** if it is not set (IL facts document
    /// §A-1). In that case <c>m_activationFrame</c> stays 0 and <c>Significant(256)</c> is
    /// never set, so <c>IsStillEmerging</c> is true for ever, the surrounding buildings
    /// never call <c>DetectDisaster</c>, and **nothing shows in the hazard map or in the
    /// notifications at all**. Not one exception is raised. ③ shipped this, and it was
    /// only found during ②'s review.
    ///
    /// **So setting the flag is not enough on its own.** <see cref="Begin"/> observes
    /// <c>m_activationFrame != 0</c> immediately after <c>StartNow</c>. If a future
    /// refactor deletes the assignment to <c>m_flags</c>, we will notice at runtime
    /// without fail.
    ///
    /// ── Trap 2: <c>CreateDisaster</c>'s return value ──────────────────────
    ///
    /// Disasters are capped at 256. On failure <c>CreateDisaster</c> **returns false and
    /// gives out <c>disasterIndex = 0</c>** (no exception. Earthquake §E-1). Write without
    /// looking at it and you **scribble over somebody else's disaster slot**.
    /// <see cref="Create"/> always looks at the return value.
    ///
    /// ── Trap 3: <c>m_activationFrame</c>'s default (we divided by zero in the game) ──
    ///
    /// The default <c>StartDisaster</c> writes is
    /// <c>m_startFrame + m_emergingDuration</c>, and the Thunderstorm prefab's
    /// <c>m_emergingDuration</c> is <b>8192</b> (measured). Leave it as it is and
    /// <c>ThunderStormAI.GetFireSpreadProbability</c>'s
    /// <c>1500 / (8 + (num &gt;&gt; 10))</c> <b>divides by zero</b>
    /// (the facts and the arithmetic are in
    /// <see cref="DisasterPlus.Core.Typhoon.VanillaFireSpread"/>).
    /// On top of that, activation would come after the lifetime has run out, so the
    /// typhoon stays Emerging all its life.
    /// <see cref="Begin"/> pulls the activation frame back to the start frame.
    ///
    /// ── IDs get reused ───────────────────────────────────────
    ///
    /// Disaster IDs are reused after release. Keep writing <c>m_targetPosition</c> after
    /// the slot has turned into some other disaster and **④ drags an unrelated disaster
    /// around** (the same accident ③'s <c>FireWhirlPinner</c> guards against by rejecting
    /// false positives on distance). <see cref="TryGetBuffer"/> checks ownership against
    /// four conditions every tick.
    ///
    /// ── Look at <c>Singleton&lt;T&gt;.exists</c> first ──────────────────────
    ///
    /// <c>Singleton&lt;T&gt;.instance</c> runs <c>FindObjectOfType</c> and
    /// <c>new GameObject</c> when <c>sInstance</c> is null, which makes it a **main thread
    /// only API**; step on it from the sim thread and you crash (the same note is on
    /// <see cref="TsunamiChain"/>).
    /// </summary>
    internal static class TyphoonSlot
    {
        /// <summary>
        /// Once the remaining time falls below this, re-advance the activation frame
        /// (frames).
        /// **Never make it 0** — at 0 we would be rewriting "on the tick after it ran
        /// out", and the host would be dead for that one tick.
        /// </summary>
        private const uint KeepAliveMarginFrames = 512u;

        private static ushort _id;
        private static uint _activationFrame;

        /// <summary>
        /// The <c>m_randomSeed</c> of the disaster we grabbed. **This is the key that
        /// identifies ownership of the slot** (the doc on <see cref="TryGetBuffer"/>).
        /// It is the value <c>DisasterManager.CreateDisaster</c> put in, and **no AI ever
        /// rewrites it.**
        /// </summary>
        private static ulong _randomSeed;

        /// <summary>The same disaster's <c>m_infoIndex</c>. We look at it together with the
        /// seed, as a pair.</summary>
        private static ushort _infoIndex;

        /// <summary>Whether an exception has been reported once through <c>Log.Error</c>.
        /// Not reset by <see cref="Forget"/> (it is a fact about the game build, not
        /// per-city state).</summary>
        private static bool _errorLogged;

        /// <summary>The index of the disaster slot we hold. 0 means "we have none".</summary>
        internal static ushort Id { get { return _id; } }

        /// <summary>
        /// The disaster's <c>m_activationFrame</c>. It is <b>the value
        /// <see cref="Begin"/> pulled back to the start frame</b>, not
        /// <c>StartDisaster</c>'s default (<c>m_startFrame + m_emergingDuration</c>)
        /// (trap 3).
        /// It is the cheapest key for spotting that the slot has been reused, and it is
        /// also **the origin from which the lightning budget estimates vanilla's share**
        /// (<c>LightningBudget.VanillaRampCount</c>).
        /// </summary>
        internal static uint ActivationFrame { get { return _activationFrame; } }

        /// <summary>
        /// Take one disaster slot. **It does not start yet.**
        ///
        /// It is in two stages because the seed for ④'s track is <b>the disaster ID
        /// itself</b> (design doc §4.1, "it must be reproducible from the same save").
        /// Until the ID is fixed the centre is not fixed, and until the centre is fixed we
        /// cannot build the coordinates to pass to <see cref="Begin"/>.
        /// </summary>
        internal static bool Create(out string refusal)
        {
            refusal = null;

            var info = FindStormInfo();
            if (info == null)
            {
                refusal = "no ThunderStormAI prefab (Natural Disasters DLC?)";
                return false;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                refusal = "DisasterManager is not available";
                return false;
            }

            ushort id;
            // ★ Trap 2: always look at the return value. On false, id = 0, and writing
            //    there anyway **scribbles over somebody else's disaster slot** (cap 256).
            if (!Singleton<DisasterManager>.instance.CreateDisaster(out id, info))
            {
                refusal = "CreateDisaster returned false (disaster buffer full?)";
                Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyFull", refusal);
                return false;
            }

            _id = id;
            _activationFrame = 0u;

            // ★★ **Take down the ownership key here** (the doc on
            //    <see cref="TryGetBuffer"/>). It must be the value straight after
            //    <c>CreateDisaster</c> put it in — read it later and, if the slot had
            //    already been taken by somebody, we would memorise "the taker's key" as
            //    our own.
            DisasterData[] created = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
            _randomSeed = created[id].m_randomSeed;
            _infoIndex = created[id].m_infoIndex;

            return true;
        }

        /// <summary>
        /// Write the initial state into the slot we took, start it with <c>StartNow</c>,
        /// and **check on the spot that <c>SelfTrigger</c> really took effect** (trap 1).
        ///
        /// <paramref name="pos"/> goes in as the centre before clamping (y is ignored) and
        /// comes back as the value actually written, after
        /// <c>ClampDisasterTarget</c> and the terrain height. The caller uses that y as
        /// "the height of the centre".
        ///
        /// On failure it releases the slot and calls <see cref="Forget"/>, so the caller
        /// does not have to write any cleanup.
        /// </summary>
        internal static bool Begin(ref Vector3 pos, float angle, byte intensity, out string refusal)
        {
            refusal = null;

            // ★ Do not roll two different failures into one message (whole-project review
            //   I3). Call "we have not taken a slot yet" "DisasterManager is not
            //   available" and whoever reads the diagnostics goes chasing the absence of a
            //   Singleton that is not absent at all.
            if (_id == 0)
            {
                refusal = "Begin was called without a disaster slot (Create did not run "
                        + "or did not succeed)";
                Forget();
                return false;
            }

            if (!Singleton<DisasterManager>.exists)
            {
                refusal = "DisasterManager is not available";
                Forget();
                return false;
            }

            var manager = Singleton<DisasterManager>.instance;
            var buffer = manager.m_disasters.m_buffer;
            if (buffer == null || _id >= buffer.Length)
            {
                refusal = "the disaster index is out of range";
                // ★★ **Give the slot we took back before bowing out** (whole-project
                //    review I3). We only get here after Create succeeded, so ④ has one
                //    disaster slot reserved. Forget without giving it back and that slot
                //    is never released by anybody for the lifetime of the city, and stays
                //    in the disaster list too (the same reason the SelfTrigger watchdog
                //    below calls Abandon).
                //    ReleaseDisaster itself rejects out-of-range indices (Abandon holds
                //    the exception).
                Abandon(manager, _id);
                Forget();
                return false;
            }

            var info = buffer[_id].Info;
            if (info == null || info.m_disasterAI == null)
            {
                refusal = "the new disaster slot has no ThunderStormAI";
                Abandon(manager, _id);
                Forget();
                return false;
            }

            var ai = info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);                        // public
            pos.y = SampleHeight(pos);                              // treated as StartDisaster does (§A-1)

            buffer[_id].m_targetPosition = pos;
            buffer[_id].m_angle = angle;
            buffer[_id].m_intensity = intensity;
            // ★ Trap 1: without this, StartDisaster returns immediately at IL_000E (class doc).
            buffer[_id].m_flags |= DisasterData.Flags.SelfTrigger;

            // StartDisaster is protected. Straight after CreateDisaster, m_flags is only
            // Created(1), so StartNow's "if & 60 is 0" gate always lets us through (§E-3).
            ai.StartNow(_id, ref buffer[_id]);

            // ★ Check on the spot that SelfTrigger really took effect. If StartDisaster
            //    got through, m_activationFrame = m_startFrame + m_emergingDuration is in
            //    there (§A-1 IL_003F). **Do this check before we overwrite it.**
            if (buffer[_id].m_activationFrame == 0u)
            {
                refusal = "StartDisaster did not schedule an activation frame; "
                        + "the SelfTrigger flag did not take effect";
                Log.Error("typhoon: " + refusal, null);
                Abandon(manager, _id);
                Forget();
                return false;
            }

            // ★★ **Pull the activation frame back to the start frame.** There are two
            //    reasons, and both follow from the design that "④'s typhoon begins right
            //    there the moment you press it".
            //
            //    1. To remove the division by zero
            //       (DisasterPlus.Core.Typhoon.VanillaFireSpread).
            //       The default StartDisaster writes is m_startFrame + m_emergingDuration,
            //       and the Thunderstorm prefab's m_emergingDuration is 8192. Then
            //       ThunderStormAI.GetFireSpreadProbability's
            //       1500 / (8 + (num >> 10)) divides by exactly 0 at num = -8192.
            //       ④ scatters lightning while still Emerging, so the moment one of those
            //       starts a fire, a DivideByZeroException comes out inside vanilla
            //       (seen in the game).
            //
            //    2. Without pulling it back **the typhoon lives and dies as Emerging**.
            //       Both m_emergingDuration and m_activeDuration are 8192, and ④'s
            //       lifetime is only m_activeDuration long. By the time it activates, ④
            //       has already let go, and Deactivate does nothing because the Active
            //       flag is not set.
            //
            //    m_activationFrame == 0 means "nothing is scheduled" (IsStillEmerging's
            //    IL_0015 treats 0 as permanently true), so we never write 0, not even on
            //    frame 0.
            uint activation = DisasterPlus.Core.Typhoon.VanillaFireSpread
                                  .SafeActivationFrame(buffer[_id].m_startFrame);
            buffer[_id].m_activationFrame = activation;

            _activationFrame = activation;
            return true;
        }

        /// <summary>
        /// **Keep the host storm alive.** **Sim thread.**
        ///
        /// ── Why it is needed (2026-08-22, in-game report "the effects disappear almost immediately") ──
        ///
        /// <c>ThunderStormAI.IsStillActive</c> is
        /// <c>(currentFrame - m_activationFrame) &lt; m_activeDuration</c> (measured from
        /// the IL). It dies when <b>the time elapsed since the activation frame</b>
        /// reaches the limit, so <b>re-advancing <c>m_activationFrame</c> to "now" refills
        /// the remaining time</b>.
        ///
        /// ★★ <b>Put it exactly at "now".</b>
        ///   - <c>IsStillActive</c> … true, since <c>0 &lt; m_activeDuration</c>
        ///   - <c>IsStillEmerging</c> … <c>now &lt; now</c> is false (**it does not go back
        ///     to Emerging**)
        ///   - <c>ThunderStormAI.GetFireSpreadProbability</c>'s
        ///     <c>1500 / (8 + (num &gt;&gt; 10))</c> … at <c>num = 0</c> the divisor is 8.
        ///     **No division by zero** (pull it back into the negatives and it becomes 0;
        ///     we produced that once in the game)
        ///
        /// ★ Update <see cref="_activationFrame"/> to the same value. That is the key for
        ///   telling "has the slot been reused by somebody" (<see cref="TryGetBuffer"/>),
        ///   so writing only one of the two makes us **misjudge our own typhoon as "taken
        ///   by somebody else" on the next tick and let go of it**.
        ///
        /// ★ Do not write every tick. Only when the remaining time drops below
        ///   <see cref="KeepAliveMarginFrames"/>.
        /// </summary>
        internal static void KeepAlive(uint currentFrame, uint activeDuration)
        {
            if (_id == 0 || activeDuration == 0u) return;

            DisasterData[] buffer;
            string lost;
            if (!TryGetBuffer(out buffer, out lost)) return;

            uint elapsed = currentFrame - _activationFrame;

            // There is still room. **Do not write.**
            if (elapsed + KeepAliveMarginFrames < activeDuration) return;

            buffer[_id].m_activationFrame = currentFrame;
            _activationFrame = currentFrame;
        }

        /// <summary>
        /// Whether the slot we hold is still ④'s. **Disaster IDs are reused after
        /// release** (class doc). If even one condition fails it returns false and puts
        /// the reason in <paramref name="lostReason"/> — the caller must <b>stop writing
        /// and let go</b>. Do not call vanilla's ending routes (it is not ④'s any more).
        /// </summary>
        internal static bool TryGetBuffer(out DisasterData[] buffer, out string lostReason)
        {
            buffer = null;
            lostReason = null;

            if (!Singleton<DisasterManager>.exists)
            {
                lostReason = "DisasterManager is not available";
                return false;
            }

            var candidate = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
            if (candidate == null || _id == 0 || _id >= candidate.Length)
            {
                lostReason = "the disaster index is out of range";
                return false;
            }

            if ((candidate[_id].m_flags & DisasterData.Flags.Created) == 0)
            {
                lostReason = "the disaster slot was released";
                return false;
            }

            // ★★ **The ownership key is m_randomSeed.** (2026-08-25, found from an
            //    in-game log.)
            //
            //    This used <c>m_activationFrame</c> as the key for a long time.
            //    **The game itself rewrites that.** From an IL scan of every method
            //    (<c>docs/tools/findwriters.ps1</c>):
            //
            //        WRITE  DisasterAI::ActivateDisaster          <- ★ this one
            //        WRITE  ThunderStormAI::StartDisaster
            //        WRITE  EarthquakeAI / SinkholeAI / TornadoAI::StartDisaster
            //        WRITE  Data::Deserialize
            //
            //    In other words <b>the value changes the instant the storm goes from
            //    Emerging to Active</b>, and ④ would misjudge its own disaster as "taken
            //    by somebody else" and let go of it. The in-game log's
            //        DIAG TyLost: the disaster slot was reused by something else
            //    was exactly that, and the whole series of reports about **the cloud
            //    appearing for an instant and only the thunderstorm remaining** all came
            //    out of this one line (the cloud's construction, its seed and the culling
            //    were all irrelevant).
            //
            //    The only things that write <c>m_randomSeed</c> are
            //    <c>DisasterManager.CreateDisaster</c> and loading a save; **no AI touches
            //    it** (same scan). It is 64 bits wide and necessarily changes if the slot
            //    is reused — that is the correct key for "is this the same disaster".
            //
            //    ★ Look at <c>m_infoIndex</c> as well. Even if the seed matches by chance,
            //      if it has become a different kind of disaster it is somebody else's.
            if (candidate[_id].m_randomSeed != _randomSeed
                || candidate[_id].m_infoIndex != _infoIndex)
            {
                lostReason = "the disaster slot was reused by something else";
                return false;
            }

            // get_Info is four instructions with no bounds check, so wrap each element
            // access in try/catch.
            try
            {
                var info = candidate[_id].Info;
                if (info == null || !(info.m_disasterAI is ThunderStormAI))
                {
                    lostReason = "the disaster slot is no longer a thunderstorm";
                    return false;
                }
            }
            catch
            {
                lostReason = "the disaster slot could not be identified";
                return false;
            }

            buffer = candidate;
            return true;
        }

        /// <summary>
        /// The per-tick write (IL facts document §E-1). <paramref name="pos"/> goes in as
        /// the centre before clamping and comes back as the coordinate actually written
        /// (clamped, plus the terrain height).
        ///
        /// Reading the IL for this task showed that <c>ClampDisasterTarget</c> rounds to
        /// **the inside of the unlocked tiles, not the map rectangle** (it looks at
        /// <c>GameAreaManager.IsUnlocked</c> / <c>GetAreaBounds</c> in eight directions).
        /// That is why ④ keeps "the true centre" itself and writes only **a clamped copy**
        /// here. Decide the ending condition from the clamped value and the typhoon sticks
        /// to the edge of the purchased area and never ends.
        /// </summary>
        internal static void WriteTarget(DisasterData[] buffer, ref Vector3 pos,
                                         float angle, byte intensity)
        {
            var ai = buffer[_id].Info.m_disasterAI;
            ai.ClampDisasterTarget(ref pos);
            pos.y = SampleHeight(pos);

            buffer[_id].m_targetPosition = pos;
            buffer[_id].m_angle = angle;
            buffer[_id].m_intensity = intensity;
        }

        /// <summary>
        /// Put it on vanilla's ending route. <c>DeactivateNow</c> is public, and reading
        /// the IL for this task showed that **it does nothing unless <c>Active(8)</c> is
        /// set in <c>m_flags</c>**. If it is set, <c>ThunderStormAI.DeactivateDisaster</c>
        /// runs, and since <c>SelfTrigger</c> is on, <c>m_targetRain = 0</c> /
        /// <c>m_targetCloud = 0</c> are written (§A-1).
        ///
        /// ★ **If we stop it while Emerging, this does nothing.** That is why ④ must put
        ///   back the weather it touched itself (<c>TyphoonWeather.Release</c>).
        ///
        /// **We do not release the disaster slot.** <c>DisasterAI.IsStillClearing</c> (the
        /// base) keeps Clearing while the disaster group's <c>m_refCount &gt; 1</c>, i.e.
        /// while buildings set alight by ④'s lightning remain (§A-1). **The storm not
        /// ending until the fires are out** is the correct behaviour, and afterwards
        /// <c>DisasterManager.SimulationStepImpl</c> calls <c>ReleaseDisaster</c>.
        /// The same reason ②'s <see cref="TsunamiChain"/> judged <c>ReleaseDisaster</c> to
        /// be "available but not to be used" (do not create a disaster with no ending
        /// notification, as seen by another mod that received
        /// <c>OnDisasterStarted</c>).
        /// </summary>
        internal static void Deactivate()
        {
            try
            {
                if (_id != 0 && Singleton<DisasterManager>.exists)
                {
                    var buffer = Singleton<DisasterManager>.instance.m_disasters.m_buffer;
                    if (buffer != null && _id < buffer.Length)
                    {
                        var info = buffer[_id].Info;
                        if (info != null && info.m_disasterAI is ThunderStormAI)
                        {
                            info.m_disasterAI.DeactivateNow(_id, ref buffer[_id]);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon deactivation failed", e);
                }
            }
        }

        /// <summary>Only drops the references. **It does not touch the disaster
        /// slot.**</summary>
        internal static void Forget()
        {
            _id = 0;
            _activationFrame = 0u;
            _randomSeed = 0ul;
            _infoIndex = 0;
        }

        /// <summary>
        /// The cleanup we only reach when the <c>SelfTrigger</c> watchdog fires.
        /// **Normally unreachable.**
        ///
        /// <c>DeactivateNow</c> cannot pack it away. <c>DisasterAI.DeactivateNow</c> does
        /// nothing without <c>m_flags &amp; Active(8)</c>, and the flags at this point are
        /// <c>Created|Emerging</c>. On top of that
        /// <c>ThunderStormAI.IsStillEmerging</c> **returns true permanently** when
        /// <c>m_activationFrame == 0</c> (the <c>brfalse</c> at IL_0015), so this disaster
        /// stays Emerging and **never becomes Finished, and the slot is never released**.
        ///
        /// ②'s <see cref="TsunamiChain"/> judged <c>ReleaseDisaster</c> to be "available
        /// but not to be used". There, the phase advances naturally from <c>m_startFrame</c>
        /// and the IL confirmed it would self-release within 39 in-game hours at worst.
        /// **That condition does not hold here** (the IL establishes that it will not
        /// advance), so we decide differently. Leave it and one of the 256 disaster slots
        /// is eaten for the lifetime of the city, and it stays in the disaster list for
        /// ever.
        /// </summary>
        private static void Abandon(DisasterManager manager, ushort id)
        {
            try
            {
                manager.ReleaseDisaster(id);
            }
            catch (System.Exception e)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("typhoon could not release the stuck disaster slot", e);
                }
            }
        }

        private static float SampleHeight(Vector3 pos)
        {
            if (!Singleton<TerrainManager>.exists) return 0f;
            return Singleton<TerrainManager>.instance.SampleDetailHeight(pos);
        }

        /// <summary>
        /// The disaster prefab carrying <c>ThunderStormAI</c>. **Not cached**
        /// (the same decision as <c>TsunamiChain.FindTsunamiInfo</c>; the scan only runs
        /// at the moment a typhoon is raised).
        /// </summary>
        private static DisasterInfo FindStormInfo()
        {
            try
            {
                return DisasterManager.FindDisasterInfo<ThunderStormAI>();
            }
            catch
            {
                return null;
            }
        }
    }
}
