using System.Collections.Generic;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 診断ログ。output_log.txt は
    /// &lt;Steam&gt;\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt にある（AppData ではない）。
    /// Diag はキーごとにスロットリングする。毎 tick 垂れ流すとログが使い物にならなくなる。
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[DisasterPlus] ";
        private const float DiagIntervalSeconds = 10f;

        private static readonly Dictionary<string, float> _lastDiag = new Dictionary<string, float>();

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

        /// <summary>同じ key では DiagIntervalSeconds に 1 回しか出さない。</summary>
        public static void Diag(string key, string message)
        {
            float now = Time.realtimeSinceStartup;
            float last;
            if (_lastDiag.TryGetValue(key, out last) && now - last < DiagIntervalSeconds) return;
            _lastDiag[key] = now;
            Debug.Log(Prefix + "DIAG " + key + ": " + message);
        }

        /// <summary>レベルアンロード時に呼ぶ。都市をまたいでスロットル状態を持ち越さない。</summary>
        public static void Reset()
        {
            _lastDiag.Clear();
        }

        /// <summary>
        /// チャンネル付きの診断ログ。マスクで無効なら何も出さない。
        ///
        /// 既存の Diag(key, message) は General 扱いのまま残してある。
        /// 本フェーズで既存呼び出しを機能チャンネルへ移行しないこと。移行すると
        /// 既定 OFF になり、docs/playtest-checklist.md の手順が壊れる。
        /// </summary>
        public static void Diag(int channel, string key, string message)
        {
            if (!DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(channel, CurrentMask())) return;
            Diag(key, message);
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
