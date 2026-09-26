using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <b>Takes vanilla's (the ND DLC's) tornado out of the random draw.</b> Sim thread
    /// only.
    ///
    /// ── The owner's instruction (2026-08-22) ───────────────────────────────
    ///
    /// &gt; There is a bug where the DLC's tornado spawns and never goes away.
    /// &gt; Please make the vanilla tornado stop spawning.
    ///
    /// ── ★★ No Harmony patch is needed (confirmed in the IL) ───────────────
    ///
    /// There is exactly one place random disasters are drawn,
    /// <c>DisasterManager.FindRandomDisasterInfo</c>, and inside it is a <b>weighted
    /// draw</b>:
    ///
    /// <code>
    /// total = Σ (unlocked DisasterInfo).m_finalRandomProbability       // IL_0036-003F
    /// if (total == 0) return null                                      // IL_004B-0052
    /// pick  = SimulationManager.m_randomizer.Int32(total)              // IL_0053-0063
    /// ...pick one by accumulating the weights
    /// </code>
    ///
    /// So setting <c>m_finalRandomProbability</c> to 0 <b>removes that disaster from the
    /// draw's candidates entirely</b>.
    ///
    /// ★★ <b>The game writes that value in exactly one place.</b>
    ///   Sweeping the IL of every method with <c>docs/tools/findwriters.ps1</c> gives:
    ///
    /// <code>
    ///   WRITE  DisasterManager::InitializeProperties
    ///   read   DisasterManager::FindRandomDisasterInfo
    /// </code>
    ///
    ///   <c>InitializeProperties</c> runs <b>once, on level load</b>. So <b>writing 0 once
    ///   after the load holds for the rest of the session</b> — there is no need to repaint
    ///   it every tick and no need for a patch.
    ///   (**Do not decide this by guesswork.** Writing a value that gets recomputed every
    ///   frame just once is the classic failure where nothing happens but you believe it
    ///   worked.)
    ///
    /// ── The fire whirl is not stopped ──────────────────────────────────────
    ///
    /// ③ (the fire whirl) calls <c>DisasterManager.CreateDisaster(out id, info)</c>
    /// <b>directly</b> to create its tornado disaster (<c>FireWhirlSpawner</c>). That never
    /// goes through the draw even once, so it is unaffected by this type.
    /// **The only thing stopped is "tornadoes the game raises by itself".**
    ///
    /// ── Putting it back ────────────────────────────────────────────────────
    ///
    /// We keep a note of the original values for when the setting is turned off. Without
    /// it, tornadoes would never happen again even after you turn it off —
    /// <b>never quietly leave behind a change the settings cannot undo.</b>
    /// There is no need to write the values back on level unload (the next load has
    /// <c>InitializeProperties</c> recompute them), but the notes are discarded.
    /// </summary>
    public static class VanillaTornadoSuppressor
    {
        /// <summary>The prefabs we stopped, with their original weights. <b>So that it can
        /// be put back.</b></summary>
        private static readonly Dictionary<DisasterInfo, int> _original =
            new Dictionary<DisasterInfo, int>();

        private static bool _logged;

        /// <summary>Whether this type is currently stopping the random spawn.</summary>
        public static bool Suppressing { get { return _original.Count > 0; } }

        /// <summary>How many prefabs we stopped (for the diagnostics).</summary>
        public static int SuppressedCount { get { return _original.Count; } }

        /// <summary>What happened most recently (for the diagnostics). **Never silently do
        /// nothing.**</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// **Sim thread.** Stops or restores to match the setting. It is idempotent, so it
        /// is fine to call every tick (in practice it writes nothing unless the state has
        /// changed).
        /// </summary>
        public static void Apply(bool suppress)
        {
            try
            {
                if (suppress) Suppress();
                else Restore();
            }
            catch (System.Exception e)
            {
                Detail = "the vanilla tornado could not be suppressed ("
                         + e.GetType().Name + ")";
                if (!_logged)
                {
                    _logged = true;
                    Log.Error("vanilla tornado suppression failed", e);
                }
            }
        }

        private static void Suppress()
        {
            if (_original.Count > 0) return;   // already stopped

            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            if (count <= 0)
            {
                Detail = "no DisasterInfo prefabs are loaded yet";
                return;
            }

            int found = 0;
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info = PrefabCollection<DisasterInfo>.GetLoaded(i);
                if (info == null) continue;
                if (!(info.m_disasterAI is TornadoAI)) continue;

                found++;
                // ★ **Note down the ones that are already 0 too.** Without that, at
                //   restore time there is no telling "it was 0 to begin with" from "we
                //   never touched it".
                _original[info] = info.m_finalRandomProbability;
                info.m_finalRandomProbability = 0;
            }

            if (found == 0)
            {
                // The ND DLC is not owned, for instance. **That is not an anomaly**, so we
                // simply say so.
                Detail = "no TornadoAI prefab is loaded; nothing to suppress "
                         + "(Natural Disasters DLC not present?)";
                return;
            }

            Detail = found + " TornadoAI prefab(s) removed from the random disaster draw";
            Log.Info("vanilla tornadoes suppressed: " + Detail
                     + ". The fire whirl still spawns its own vortex (it calls "
                     + "CreateDisaster directly and never goes through the draw)");
        }

        private static void Restore()
        {
            if (_original.Count == 0) return;

            foreach (KeyValuePair<DisasterInfo, int> pair in _original)
            {
                // ★ Unity's fake-null. A prefab reference can die as you move in and out
                //   of cities.
                if (pair.Key == null) continue;
                pair.Key.m_finalRandomProbability = pair.Value;
            }

            Log.Info("vanilla tornadoes restored to the random disaster draw ("
                     + _original.Count + " prefab(s))");
            _original.Clear();
            Detail = "restored";
        }

        /// <summary>
        /// **Call this on level unload.** It only discards the notes; it does not write
        /// the values back — the next load has
        /// <c>DisasterManager.InitializeProperties</c> recompute them, so going off to
        /// write into a dead prefab reference is the more dangerous option.
        /// </summary>
        public static void Forget()
        {
            _original.Clear();
            Detail = null;
            // _logged is not reset (it is a fact about this environment).
        }
    }
}
