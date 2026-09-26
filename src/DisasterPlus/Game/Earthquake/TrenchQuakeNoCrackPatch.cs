using DisasterPlus.Core.Common;
using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **A trench earthquake does not crack the ground.** Sim thread only.
    ///
    /// ── Report from the game (2026-08-22) ────────────────────────────────
    ///
    /// &gt; Trench earthquakes are producing a fault too (the terrain deformation at the
    /// &gt; epicentre). A trench quake's epicentre is out at sea, so please let the
    /// &gt; shaking happen but do not produce the fault.
    ///
    /// Quite right: **a plate-boundary rupture happens tens of kilometres below the sea
    /// floor**. It leaves no fissure you can see at the surface (an inland shallow or
    /// fault quake does).
    ///
    /// ── ★★ What to stop (confirmed in the IL) ───────────────────────────
    ///
    /// The ground is cracked in exactly one place: the <c>DisasterHelpers.MakeCrack</c>
    /// call inside <c>EarthquakeAI.SimulationStep</c> (IL_03AD).
    ///
    /// <b>That is the <i>only</i> thing to stop.</b> The same <c>SimulationStep</c> also
    /// calls <c>DestroyBuildings</c> (IL_0298), <c>DestroyNetSegments</c> (IL_02B6),
    /// <c>SplashWater</c> (IL_024F) and <c>DetectDisaster</c>. The request was to keep
    /// the shaking and the damage, so not one of those is touched.
    ///
    /// ★★ <b>Zeroing <c>m_crackLength</c> / <c>m_crackWidth</c> is not the way.</b>
    ///   Inside that same method those two also set <b>the length of the band over which
    ///   damage is spread</b> (at IL_017E / IL_018A the value multiplied by the intensity
    ///   drives the destruction loop that follows), so zeroing them
    ///   <b>wipes out every kind of damage except the shaking</b>.
    ///   On top of that <c>IsStillClearing</c>, <c>CanAffectAt</c>,
    ///   <c>GetMinimumEdgeDistance</c>, <c>GetPosition</c> and <c>UpdateHazardMap</c> all
    ///   read the same fields, so rewriting the prefab
    ///   <b>breaks any vanilla earthquake running at the same time</b>.
    ///
    /// ── Why two patches are needed ──────────────────────────────────────
    ///
    /// <c>MakeCrack</c> is <c>static</c>, so **it cannot tell which disaster the call is
    /// for** (its arguments are just a position, a width and a depth). Hence
    ///
    /// <code>
    /// Prefix on EarthquakeAI.SimulationStep   → raise "we are inside a trench quake"
    /// Prefix on DisasterHelpers.MakeCrack     → if it is raised, skip the original
    /// Postfix on EarthquakeAI.SimulationStep  → always lower it again
    /// </code>
    ///
    /// ★ The flag is not <c>[ThreadStatic]</c>. <c>SimulationStep</c> is only ever
    ///   called from the sim thread (vanilla's own convention), so a plain <c>static</c>
    ///   is enough. <b>A Postfix runs even when an exception is thrown</b> (Harmony's
    ///   default), so the flag cannot get stuck up and leave
    ///   <b>vanilla earthquakes unable to crack the ground either</b>.
    /// </summary>
    [HarmonyPatch(typeof(EarthquakeAI), "SimulationStep",
        new[] { typeof(ushort), typeof(DisasterData) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class TrenchQuakeStepPatch
    {
        /// <summary>
        /// Whether we are right now inside a trench earthquake's <c>SimulationStep</c>.
        /// Only <see cref="TrenchQuakeNoCrackPatch"/> reads it.
        /// </summary>
        internal static bool InTrenchQuake;

        /// <summary>How many cracks have been suppressed so far (for diagnostics).</summary>
        internal static int SuppressedCracks;

        public static void Prefix(ushort disasterID)
        {
            InTrenchQuake = TrenchQuakeSlot.IsTrenchQuake(disasterID);
        }

        public static void Postfix()
        {
            // ★★ **Always lower it.** Left raised, the next vanilla earthquake to run
            //    would not crack the ground either.
            InTrenchQuake = false;
        }
    }

    /// <summary>
    /// Skips the method that cracks the ground, but only during a trench earthquake.
    /// The whole story is in <see cref="TrenchQuakeStepPatch"/>'s class doc.
    /// </summary>
    [HarmonyPatch(typeof(DisasterHelpers), "MakeCrack",
        new[] { typeof(Vector2), typeof(Vector2), typeof(float), typeof(float) })]
    public static class TrenchQuakeNoCrackPatch
    {
        /// <summary>Returning false skips the original (Harmony's Prefix contract).</summary>
        public static bool Prefix()
        {
            if (!TrenchQuakeStepPatch.InTrenchQuake) return true;

            TrenchQuakeStepPatch.SuppressedCracks++;

            // ★ This runs every frame, so log only the first time.
            if (TrenchQuakeStepPatch.SuppressedCracks == 1)
            {
                Log.Info("trench earthquake: the terrain crack is suppressed on purpose "
                         + "(a megathrust ruptures tens of km below the sea floor; it does "
                         + "not open a fissure you can see). The shaking and the damage "
                         + "are unaffected");
            }

            return false;
        }
    }
}
