using System.Collections.Generic;
using System.IO;
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
