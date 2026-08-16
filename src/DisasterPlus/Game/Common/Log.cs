using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断ログ。output_log.txt は
    /// &lt;Steam&gt;\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt にある（AppData ではない）。
    /// Diag はキーごとにスロットリングする。毎 tick 垂れ流すとログが使い物にならなくなる。
    ///
    /// スレッド安全性: Diag は sim スレッド（FireWhirlFeature / FireWhirlDamage /
    /// FireWhirlSpawner / FireWhirlPinner）と main スレッド（IntensityUnlock /
    /// DisasterPanelBar / FireWhirlPlacementTool）の両方から呼ばれる。
    /// System.Collections.Generic.Dictionary は書き込みと読み取りの並行実行が安全ではなく、
    /// main スレッドの新規キー挿入がバケット再確保を起こしている最中に sim スレッドが
    /// TryGetValue すると、例外か壊れたバケット連鎖の無限ループ（＝スタックトレースの
    /// 出ないハング）になる。_lastDiag への全アクセスを _diagGate 1 本で直列化する
    /// （FeatureHost._errorGate と同じ規律）。このロックを持ったまま他クラスのコードは
    /// 呼ばない（Debug.Log もロックの外で行う）。
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[DisasterPlus] ";

        /// <summary>
        /// 同一キーのスロットル間隔（sim フレーム）。
        ///
        /// 時刻源に UnityEngine.Time.realtimeSinceStartup は使えない。UnityEngine.Time は
        /// main スレッド専用で、この Unity ビルドでは sim スレッドからの読み取りに保証が無い。
        /// 代わりに SimulationManager.m_currentFrameIndex を使う
        /// （IL 実測: SimulationManager 上の public な instance フィールド、型 System.UInt32）。
        /// ただの uint フィールドなのでどちらのスレッドから読んでも安全。
        ///
        /// 512 という値の根拠: SimulationManager.Update は IL 実測で
        ///   m_referenceTimer += Time.deltaTime / Time.fixedDeltaTime * FinalSimulationSpeed
        /// としてフレームを進めるので、フレームは「(1 / fixedDeltaTime) × ゲーム速度」/実秒 で進む。
        /// fixedDeltaTime の設定値は Assembly-CSharp 内に set_fixedDeltaTime の呼び出しが
        /// 1 件も無く（全メソッドを IL 走査して確認）Unity のプロジェクト設定側にあるため、
        /// Unity 既定の 0.02 なら通常速度で 50 フレーム/秒 ＝ 512 フレームは従来の 10 実秒に相当する。
        /// ゲーム速度 2/3 では比例して短くなるが、ここはログ量の上限を決めるだけなので
        /// 実秒との厳密な一致は要らない。
        /// </summary>
        private const uint DiagIntervalFrames = 512;

        private static readonly object _diagGate = new object();
        private static readonly Dictionary<string, uint> _lastDiag = new Dictionary<string, uint>();

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message, System.Exception e)
        {
            Debug.LogError(Prefix + message + (e == null ? "" : " :: " + e));
        }

        /// <summary>
        /// 同じ key では DiagIntervalFrames に 1 回しか出さない。
        ///
        /// チャンネル指定のないこの形は General 扱いになる（設計書 5.1）。
        /// チャンネル付きのオーバーロードへ委譲しているので、設定画面の
        /// 「General」チェックボックスは実際にこの経路を止められる。
        /// 既定マスクは General なので、既存の呼び出しの見え方は変わらない。
        /// </summary>
        public static void Diag(string key, string message)
        {
            Diag(DisasterPlus.Core.Diagnostics.LogChannel.General, key, message);
        }

        /// <summary>
        /// チャンネル付きの診断ログ。マスクで無効なら何も出さない。
        ///
        /// 本フェーズで既存呼び出しを機能チャンネルへ移行しないこと（設計書 5.2）。移行すると
        /// 既定 OFF になり、docs/playtest-checklist.md の手順が壊れる。
        ///
        /// マスク判定はスロットル判定より先に行う。逆にすると、チャンネルが OFF の
        /// 呼び出しがスロットル枠を消費してしまい、ON に切り替えた直後の 1 回が
        /// 黙って落ちる。
        /// </summary>
        public static void Diag(int channel, string key, string message)
        {
            if (!DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask())) return;
            if (!ShouldEmit(key)) return;
            Debug.Log(Prefix + "DIAG " + key + ": " + message);
        }

        /// <summary>
        /// channel の Diag が今の設定で出力されうるか。
        ///
        /// <see cref="Diag(int, string, string)"/> は同じ判定を自分でも行うので、
        /// これは**正しさのためではなく、引数の評価コストを避けるためだけ**にある。
        /// C# は呼び出し前に引数を評価し切るので、Diag の内側でいくら弾いても
        /// 文字列連結と ToString() は既に済んでしまっている。既定で OFF の
        /// チャンネル（Forecast 等）を毎 sim tick 呼ぶ経路では、その組み立てが
        /// 丸ごと無駄になる（全体レビュー指摘）。
        ///
        /// スロットル判定（<see cref="ShouldEmit"/>）はここでは見ない。見てしまうと
        /// この問い合わせ自体が枠を消費するか、あるいは呼び出し側が枠の状態に
        /// 依存して分岐することになる。ここが true でも Diag が実際には
        /// 出さないことはある（それで正しい）。
        /// </summary>
        public static bool DiagEnabled(int channel)
        {
            return DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask());
        }

        /// <summary>レベルアンロード時に呼ぶ。都市をまたいでスロットル状態を持ち越さない。</summary>
        public static void Reset()
        {
            lock (_diagGate) { _lastDiag.Clear(); }
        }

        private static bool ShouldEmit(string key)
        {
            uint now = CurrentFrame();
            lock (_diagGate)
            {
                uint last;
                // 減算は uint のまま行う。セーブのロードでフレーム番号が巻き戻っても
                // 差が巨大な値になるだけで、「出さない」側には倒れない。
                if (_lastDiag.TryGetValue(key, out last) && now - last < DiagIntervalFrames) return false;
                _lastDiag[key] = now;
                return true;
            }
        }

        private static uint CurrentFrame()
        {
            // Singleton<T>.instance は sInstance が null のとき Object.FindObjectOfType と
            // new GameObject + AddComponent を走らせる（IL 実測）。どちらも main スレッド専用
            // API なので、sim スレッドから踏まないよう exists で先に確認する
            // （exists は static フィールドの null 判定だけ・IL 実測）。
            // メインメニュー等でまだ居なければ 0 を返す。その場合キーごとに初回 1 回だけ
            // 出て以後は抑制されるが、ログを溢れさせないという目的は保たれる。
            if (!SimulationManager.exists) return 0u;
            return SimulationManager.instance.m_currentFrameIndex;
        }

        private static int CurrentMask()
        {
            try
            {
                ModSettings.Ensure();
                return ModSettings.LogChannelMask.value;
            }
            catch
            {
                // 設定がまだ用意できていない場面でもログで落ちない。
                return DisasterPlus.Core.Diagnostics.LogChannel.DefaultMask;
            }
        }
    }
}
