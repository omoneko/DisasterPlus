using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using ICities;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Saves the fire whirls that are alive.
    ///
    /// Only the logical state is saved; particles, effects and vehicle references are rebuilt.
    /// Save the appearance and the runtime references too and all you create are bugs that
    /// depend on load order.
    ///
    /// The version is written first, and the reading side branches on if (version >= N) for
    /// each added block. Old saves can be read with defaults.
    ///
    /// A note on load order: OnLoadData is called inside LoadingManager.LoadSimulationData and
    /// finishes before LoadingExtensionBase.OnLevelLoaded (DisasterPlusLoading.OnLevelLoaded,
    /// which calls FireWhirlRegistry.Clear()).
    /// So calling FireWhirlRegistry.RestoreFromSave directly here would have it wiped out by
    /// the Clear() that follows. The list to restore is parked here for the time being, and
    /// DisasterPlusLoading.OnLevelLoaded pulls it out with TakePendingRestore() after Clear()
    /// and applies it.
    /// </summary>
    public class DisasterPlusSerialization : ISerializableDataExtension
    {
        private const string DataId = "DisasterPlus.FireWhirl";

        /// <summary>
        /// 2: added the Manual (raised by hand) flag.
        ///
        /// Without saving it, a hand-raised whirl turns into an automatic one after loading,
        /// and since there is no fire around it, it dies as soon as the grace period for the
        /// condition break runs out.
        /// "It vanishes by itself if you save and load" would be a fault with no visible
        /// cause, so it is saved.
        /// </summary>
        private const int CurrentVersion = 2;

        /// <summary>
        /// The list awaiting restore, read in OnLoadData. A holding place until OnLevelLoaded
        /// pulls it out after Clear().
        /// Only written and read inside the main thread's load processing (no sim tick is
        /// running during a load).
        /// </summary>
        private static List<SavedFireWhirl> _pendingRestore;

        private ISerializableData _data;

        public void OnCreated(ISerializableData serializedData) { _data = serializedData; }
        public void OnReleased() { _data = null; }

        public void OnSaveData()
        {
            // ★ A water source's m_target is burnt into the save by
            //   WaterSimulation.Data.Serialize (④'s IL findings document §D-4). So that a
            //   save with the mod removed is not left with an overflowing river,
            //   **always put it back before saving**. It goes before the fire whirl saving —
            //   so that the structure itself guarantees "an early return cannot skip the
            //   restore" (which is why it sits above the if (_data == null) below).
            RestoreFloodedRiversForSave();

            // ★★ ②'s tsunami comes off for the same reason. That one is a water source the
            //   mod <b>made itself</b>, so if it is left behind it does not go away even when
            //   the mod is removed (see the <c>TsunamiRing</c> class doc §3).
            LiftTsunamiSourceForSave();

            // ★ The weather override is burnt into the save for the same reason
            //   (WeatherManager+Data.Serialize writes m_targetRain / m_targetCloud /
            //   m_forceWeatherOn. Measured from the IL in overall review I2). As with the
            //   water source, **lower it before saving and put it back with AddAction**.
            //   It sits above the early return for the same reason.
            LowerTyphoonWeatherForSave();

            if (_data == null) return;

            var views = FireWhirlRegistry.Snapshot();

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CurrentVersion);
                w.Write(views.Count);

                for (int i = 0; i < views.Count; i++)
                {
                    var v = views[i];
                    w.Write(v.DisasterId);
                    w.Write(v.Center.X);
                    w.Write(v.Center.Y);
                    w.Write(v.Center.Z);
                    w.Write(v.Radius);
                    w.Write(v.BurningCount);
                    w.Write(v.ElapsedMinutes);
                    w.Write(v.Manual);          // version 2 onwards
                }

                _data.SaveData(DataId, ms.ToArray());
            }

            Log.Info("saved " + views.Count + " fire whirls");
        }

        /// <summary>
        /// Puts the river water levels ④ has raised back, **before vanilla writes the water
        /// source array**.
        ///
        /// **Delay the re-application with <c>SimulationManager.AddAction</c>.** Put it
        /// straight back here (or in a <c>finally</c>) and you would raise it again before
        /// vanilla writes the array, and the leak returns —
        /// **a mod's <c>OnSaveData</c> runs before vanilla's array write** is a fact this
        /// project settled the hard way, having once shipped a temporary-flag leak.
        ///
        /// <c>SimulationManager.AddAction(System.Action)</c> is a public instance method
        /// returning an <c>AsyncAction</c> (measured from the IL in ④ Task 8). The delegate
        /// passed runs on **the sim thread**, so it does not break <c>TyphoonFlood</c>'s
        /// threading contract.
        ///
        /// **Do not fail the save itself** by throwing here. At worst the water level stays
        /// lowered, and the save is written correctly (in the un-flooded state).
        /// </summary>
        private static void RestoreFloodedRiversForSave()
        {
            try
            {
                var raised = TyphoonFlood.SnapshotAndRestoreForSave();
                if (raised == null || raised.Count == 0) return;

                // When sInstance is null, Singleton<T>.instance runs FindObjectOfType and
                // new GameObject, so check exists first.
                if (!Singleton<SimulationManager>.exists) return;

                Singleton<SimulationManager>.instance.AddAction(delegate
                {
                    TyphoonFlood.ReapplyAfterSave(raised);
                });
            }
            catch (System.Exception e)
            {
                Log.Error("could not lower the typhoon's raised water sources before saving; "
                          + "the save may contain a flooded river", e);
            }
        }

        /// <summary>
        /// Lowers the weather override ④ is holding **before vanilla writes
        /// <c>WeatherManager+Data</c>**, and puts it back with <c>AddAction</c> once the save
        /// is done.
        ///
        /// The shape is the same as <see cref="RestoreFloodedRiversForSave"/>, and so is the
        /// reason (a mod's <c>OnSaveData</c> runs before vanilla's write. Put it straight back
        /// and the leak returns). **Do not leave the restore to the next sim tick's
        /// <c>TyphoonWeather.Drive</c>** — if the save happens while paused, the pause guard
        /// stops that, so the rain alone stays gone until the pause is lifted.
        ///
        /// **Do not fail the save itself** by throwing here.
        /// </summary>
        private static void LowerTyphoonWeatherForSave()
        {
            try
            {
                bool wasDriving = TyphoonWeather.SuspendForSave();
                if (!wasDriving) return;

                // When sInstance is null, Singleton<T>.instance runs FindObjectOfType and
                // new GameObject, so check exists first.
                if (!Singleton<SimulationManager>.exists) return;

                Singleton<SimulationManager>.instance.AddAction(delegate
                {
                    TyphoonWeather.ReapplyAfterSave(true);
                });
            }
            catch (System.Exception e)
            {
                Log.Error("could not lower the typhoon's weather override before saving; "
                          + "the save may restore with the storm's rain still forced on", e);
            }
        }

        /// <summary>
        /// Removes the tsunami water source ② has placed **before vanilla writes the water
        /// source array**, and puts it back with <c>AddAction</c> once the save is done.
        ///
        /// The shape is the same as <see cref="RestoreFloodedRiversForSave"/>, and so is the
        /// reason. The difference is that what ④ touches is the <c>m_target</c> of a water
        /// source <b>that was already on the map</b>, whereas ②'s water source is <b>one the
        /// mod made</b>, so if it is left behind it does not go away even when the mod is
        /// removed.
        ///
        /// **Do not fail the save itself** by throwing here.
        /// </summary>
        private static void LiftTsunamiSourceForSave()
        {
            try
            {
                if (!TsunamiRing.SuspendForSave()) return;

                // When sInstance is null, Singleton<T>.instance runs FindObjectOfType and
                // new GameObject, so check exists first.
                if (!Singleton<SimulationManager>.exists) return;

                Singleton<SimulationManager>.instance.AddAction(delegate
                {
                    TsunamiRing.ReapplyAfterSave();
                });
            }
            catch (System.Exception e)
            {
                Log.Error("could not lift the tsunami's water source before saving; "
                          + "the save may contain a spring that never stops", e);
            }
        }

        public void OnLoadData()
        {
            // Always throw away the leftovers from the previous load. If a stale pending list
            // survived any of the early returns below (_data == null, no blob, version < 1,
            // an exception), then when the next city read has no data of its own
            // TakePendingRestore() would hand over the previous city's list, and fire whirls
            // would spring up in an unrelated city.
            _pendingRestore = null;

            if (_data == null) return;

            byte[] bytes = _data.LoadData(DataId);
            if (bytes == null || bytes.Length == 0) return;

            var restored = new List<SavedFireWhirl>();
            int rejected = 0;

            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var r = new BinaryReader(ms))
                {
                    int version = r.ReadInt32();
                    if (version < 1) return;

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var s = new SavedFireWhirl();
                        s.DisasterId = r.ReadUInt16();
                        float x = r.ReadSingle();
                        float y = r.ReadSingle();
                        float z = r.ReadSingle();
                        s.Center = new Vec3(x, y, z);
                        s.Radius = r.ReadSingle();
                        s.BurningCount = r.ReadInt32();
                        s.ElapsedMinutes = r.ReadSingle();

                        // A version 1 save has no byte for Manual. Do not read it and leave it
                        // at the default of false (read it here and the stream goes out of
                        // step, corrupting every entry after it).
                        if (version >= 2) s.Manual = r.ReadBoolean();

                        if (!IsValid(s))
                        {
                            rejected++;
                            continue;
                        }

                        restored.Add(s);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Error("fire whirl load failed; starting with none", e);
                return;
            }

            if (rejected > 0)
            {
                // Insurance for the case where part of a corrupted blob held NaN/Infinity.
                // In FireWhirlLifecycle.Advance/Evaluate a NaN <= / >= comparison is always
                // false, so letting one through unrejected would disable the absolute
                // lifetime cap (the only safety valve on the spread feedback).
                // This does not happen often (once at load time), so Warn rather than Diag.
                Log.Warn("fire whirl load: rejected " + rejected + " entr" +
                    (rejected == 1 ? "y" : "ies") + " with invalid data (NaN/Infinity/negative radius)");
            }

            // Do not apply it here (Clear() comes later). Let the OnLevelLoaded side pull it out.
            _pendingRestore = restored;
            Log.Info("loaded " + restored.Count + " fire whirls; pending apply");
        }

        /// <summary>
        /// Insurance against corrupted save data. NaN/Infinity slips past FireWhirlLifecycle's
        /// comparisons (NaN &lt;= / &gt;= is always false) and disables the absolute lifetime
        /// cap, so it is rejected here. Core's contract is left unchanged (this is the
        /// deserialiser's responsibility).
        /// </summary>
        private static bool IsValid(SavedFireWhirl s)
        {
            if (float.IsNaN(s.ElapsedMinutes) || float.IsInfinity(s.ElapsedMinutes)) return false;
            if (float.IsNaN(s.Radius) || float.IsInfinity(s.Radius)) return false;
            if (s.Radius < 0f) return false;
            if (float.IsNaN(s.Center.X) || float.IsInfinity(s.Center.X)) return false;
            if (float.IsNaN(s.Center.Y) || float.IsInfinity(s.Center.Y)) return false;
            if (float.IsNaN(s.Center.Z) || float.IsInfinity(s.Center.Z)) return false;
            return true;
        }

        /// <summary>
        /// Called from DisasterPlusLoading.OnLevelLoaded, after FireWhirlRegistry.Clear().
        /// Takes the pending list out and marks the internal pending state consumed (so the
        /// previous one is not carried over to the next load).
        /// Returns null if there is nothing to restore.
        /// </summary>
        public static List<SavedFireWhirl> TakePendingRestore()
        {
            var pending = _pendingRestore;
            _pendingRestore = null;
            return pending;
        }
    }

    /// <summary>
    /// One whirl read back from a save. The vehicle IDs are not saved (they are reattached
    /// after loading).
    /// The Ending (finishing up) flag is not saved — a known limitation by design.
    /// Save part-way through the finishing sequence and it comes back fully alive after loading.
    /// </summary>
    public class SavedFireWhirl
    {
        public ushort DisasterId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public float ElapsedMinutes;

        /// <summary>Whether it was raised by hand (version 2 onwards). Read as false from old saves.</summary>
        public bool Manual;
    }
}
