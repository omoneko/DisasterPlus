using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using DisasterPlus.Core.Common;
using ICities;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 生存中の火災旋風を保存する。
    ///
    /// 論理状態のみを保存し、パーティクル・エフェクト・車両参照は再構築する。
    /// 見た目や実行時の参照まで保存すると、ロード順に依存するバグを作るだけになる。
    ///
    /// 先頭にバージョンを書き、読み込み側は追加ブロックごとに if (version >= N) で分岐する。
    /// 旧セーブは既定値で読める。
    ///
    /// ロード順の注意: OnLoadData は LoadingManager.LoadSimulationData の中で呼ばれ、
    /// LoadingExtensionBase.OnLevelLoaded（DisasterPlusLoading.OnLevelLoaded、
    /// FireWhirlRegistry.Clear() を呼ぶ）より前に完了する。
    /// そのためここで直接 FireWhirlRegistry.RestoreFromSave を呼ぶと、
    /// 直後の Clear() で消される。復元先のリストは一旦ここに保留し、
    /// DisasterPlusLoading.OnLevelLoaded が Clear() の後に TakePendingRestore() で取り出して適用する。
    /// </summary>
    public class DisasterPlusSerialization : ISerializableDataExtension
    {
        private const string DataId = "DisasterPlus.FireWhirl";

        /// <summary>
        /// 2: Manual（手動発生）フラグを追加。
        ///
        /// 保存しないと、ロード後に手動発生の旋風が自動発生扱いに変わり、
        /// 周囲に火が無いので条件割り込みの猶予だけで消える。
        /// 「セーブ・ロードを挟むと勝手に消える」は原因の見えない不具合になるので保存する。
        /// </summary>
        private const int CurrentVersion = 2;

        /// <summary>
        /// OnLoadData で読み取った復元待ちの一覧。OnLevelLoaded が Clear() の後に取り出すまでの一時置き場。
        /// メインスレッドのロード処理内でのみ書き / 読みされる（ロード中は sim tick が回っていない）。
        /// </summary>
        private static List<SavedFireWhirl> _pendingRestore;

        private ISerializableData _data;

        public void OnCreated(ISerializableData serializedData) { _data = serializedData; }
        public void OnReleased() { _data = null; }

        public void OnSaveData()
        {
            // ★ 水源の m_target は WaterSimulation.Data.Serialize でセーブに焼き付く
            //   （④の IL 事実文書 §D-4）。MOD を外したセーブで川が溢れたままに
            //   ならないよう、**保存の前に必ず戻す**。火災旋風の保存処理より前に置く
            //   ——「早期 return しても戻し損ねない」を構造で保証するため
            //   （下の if (_data == null) より上にある理由）。
            RestoreFloodedRiversForSave();

            // ★★ ②の津波も同じ理由で外す。あちらは MOD が<b>自分で作った</b>
            //   水源なので、残ると MOD を外しても消えない
            //   （<c>TsunamiRing</c> のクラス doc §3）。
            LiftTsunamiSourceForSave();

            // ★★ ④が下げている遮蔽図も同じ理由で戻す。<c>WeatherManager+Data</c> が
            //   <c>m_windGrid</c> を書くので、下げたまま保存すると
            //   その都市の風が狂ったまま残る（<c>TyphoonTreeSway</c> のクラス doc）。
            RestoreWindGridForSave();

            // ★ 天候の上書きも同じ理由でセーブに焼き付く（WeatherManager+Data.Serialize は
            //   m_targetRain / m_targetCloud / m_forceWeatherOn を書く。全体レビュー I2 で
            //   IL 実測）。水源と同じく**保存の前に降ろし、AddAction で戻す**。
            //   早期 return より上に置くのも同じ理由である。
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
                    w.Write(v.Manual);          // version 2 以降
                }

                _data.SaveData(DataId, ms.ToArray());
            }

            Log.Info("saved " + views.Count + " fire whirls");
        }

        /// <summary>
        /// ④が持ち上げている河川の水位を、**バニラが水源配列を書く前に**元へ戻す。
        ///
        /// **再適用は <c>SimulationManager.AddAction</c> で遅らせる。** ここで
        /// （あるいは <c>finally</c> で）すぐ戻し直すと、バニラが配列を書く前に
        /// 持ち上げ直すことになり漏れが再発する ——
        /// **MOD の <c>OnSaveData</c> はバニラの配列書き込みより先に走る**というのは、
        /// 本プロジェクトが一時フラグの漏れで一度出荷して確定させた事実である。
        ///
        /// <c>SimulationManager.AddAction(System.Action)</c> は public instance で
        /// <c>AsyncAction</c> を返す（④ Task 8 で IL 実測）。渡したデリゲートは
        /// **sim スレッド**で走るので、<c>TyphoonFlood</c> のスレッド契約を破らない。
        ///
        /// ここで例外を出して**セーブそのものを失敗させない**。水位が戻ったままに
        /// なるだけで、セーブは正しく（溢れていない状態で）書かれる。
        /// </summary>
        private static void RestoreFloodedRiversForSave()
        {
            try
            {
                var raised = TyphoonFlood.SnapshotAndRestoreForSave();
                if (raised == null || raised.Count == 0) return;

                // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
                // new GameObject を走らせるので、exists で先に確認する。
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
        /// ④が握っている天候の上書きを、**バニラが <c>WeatherManager+Data</c> を書く前に**
        /// 降ろし、セーブが終わってから <c>AddAction</c> で戻す。
        ///
        /// 形は <see cref="RestoreFloodedRiversForSave"/> と同じで、理由も同じである
        /// （MOD の <c>OnSaveData</c> はバニラの書き込みより先に走る。すぐ戻し直すと
        /// 漏れが再発する）。**戻しを次の sim tick の <c>TyphoonWeather.Drive</c> に
        /// 任せない** —— ポーズ中に保存されるとポーズガードがそれを止めるので、
        /// ポーズを解くまで雨だけが消えたままになる。
        ///
        /// ここで例外を出して**セーブそのものを失敗させない**。
        /// </summary>
        private static void LowerTyphoonWeatherForSave()
        {
            try
            {
                bool wasDriving = TyphoonWeather.SuspendForSave();
                if (!wasDriving) return;

                // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
                // new GameObject を走らせるので、exists で先に確認する。
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
        /// ②が置いている津波の水源を、**バニラが水源配列を書く前に**外し、
        /// 保存が終わってから <c>AddAction</c> で戻す。
        ///
        /// 形は <see cref="RestoreFloodedRiversForSave"/> と同じで、理由も同じである。
        /// ただし④が触るのは<b>マップに元から在る</b>水源の <c>m_target</c> なのに対し、
        /// ②の水源は<b>MOD が作ったもの</b>なので、残ると MOD を外しても消えない。
        ///
        /// ここで例外を出して**セーブそのものを失敗させない**。
        /// </summary>
        private static void LiftTsunamiSourceForSave()
        {
            try
            {
                if (!TsunamiRing.SuspendForSave()) return;

                // Singleton<T>.instance は sInstance が null のとき FindObjectOfType と
                // new GameObject を走らせるので、exists で先に確認する。
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

        /// <summary>
        /// ④が下げている遮蔽図（<c>m_windGrid</c>）を、**バニラが書く前に**戻す。
        /// 形も理由も <see cref="RestoreFloodedRiversForSave"/> と同じ。
        ///
        /// ★ 戻しは <c>AddAction</c> で遅らせる。**次の tick に任せてはいけない** ——
        ///   sim スレッドは保存の最中も回っているので、バニラが配列を書く前に
        ///   下げ直してしまう（このファイルの他の 2 つと同じ穴）。
        /// </summary>
        private static void RestoreWindGridForSave()
        {
            try
            {
                if (!TyphoonTreeSway.SuspendForSave()) return;

                if (!Singleton<SimulationManager>.exists) return;

                Singleton<SimulationManager>.instance.AddAction(delegate
                {
                    TyphoonTreeSway.ReapplyAfterSave();
                });
            }
            catch (System.Exception e)
            {
                Log.Error("could not restore the wind grid before saving; the save may "
                          + "carry a lowered shelter map", e);
            }
        }

        public void OnLoadData()
        {
            // 前回ロード分の残骸を必ず捨てる。ここより下のどの早期 return でも
            // （_data == null、blob 無し、version < 1、例外）古い保留が残ったままだと、
            // 次に読む都市が今回データを持っていない場合に TakePendingRestore() が
            // 前回都市の一覧を渡してしまい、無関係な都市に火災旋風が湧く。
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

                        // version 1 のセーブには Manual のバイトが無い。読まずに既定の false のままにする
                        // （ここで読むとストリームがずれて以降の全エントリが壊れる）。
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
                // 壊れた blob の一部が NaN/Infinity を持っていた場合の保険。
                // FireWhirlLifecycle.Advance/Evaluate は NaN <= / >= 比較が常に false になるため、
                // 弾かずに通すと絶対寿命の上限が効かなくなる（延焼フィードバックの唯一の安全弁が消える）。
                // 頻発するものではない（ロード時に一度だけ）ので Diag ではなく Warn でよい。
                Log.Warn("fire whirl load: rejected " + rejected + " entr" +
                    (rejected == 1 ? "y" : "ies") + " with invalid data (NaN/Infinity/negative radius)");
            }

            // ここでは適用しない（Clear() が後から来る）。OnLevelLoaded 側で取り出させる。
            _pendingRestore = restored;
            Log.Info("loaded " + restored.Count + " fire whirls; pending apply");
        }

        /// <summary>
        /// 壊れたセーブデータへの保険。NaN/Infinity は FireWhirlLifecycle の比較
        /// （NaN &lt;= / &gt;= は常に false）をすり抜けて絶対寿命の上限を無効化してしまうため、
        /// ここで弾く。Core 側の契約は変えない（デシリアライズ側の責務）。
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
        /// DisasterPlusLoading.OnLevelLoaded から、FireWhirlRegistry.Clear() の後に呼ぶ。
        /// 保留分を取り出し、内部の保留状態は消費済みにする（次のロードに前回分を持ち越さない）。
        /// 復元対象が無ければ null を返す。
        /// </summary>
        public static List<SavedFireWhirl> TakePendingRestore()
        {
            var pending = _pendingRestore;
            _pendingRestore = null;
            return pending;
        }
    }

    /// <summary>
    /// セーブから読み戻した 1 基。車両 ID は保存しない（ロード後に付け直す）。
    /// Ending（終了処理中）フラグは保存しない — 仕様上の既知の制約。
    /// 終了処理の途中でセーブすると、ロード後は完全に生きた状態から復帰する。
    /// </summary>
    public class SavedFireWhirl
    {
        public ushort DisasterId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public float ElapsedMinutes;

        /// <summary>手動発生か（version 2 以降）。旧セーブからは false で読む。</summary>
        public bool Manual;
    }
}
