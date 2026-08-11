using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラの災害パネルの強度スライダー上限を解放する。
    ///
    /// IL 実測（設計書 付録 A-2）:
    ///   DisastersOptionPanel.OnSliderValueChanged(c, value):
    ///       m_label.text = (value / 10).ToString("F1")
    ///       m_disasterTool.m_intensity = (int)value
    /// つまりスライダーの生値がそのまま byte 強度で、表示だけが /10。
    /// set_maxValue の呼び出しはアセンブリ内に存在せず、上限は UI プレハブ側にある。
    /// よってパッチ対象が無く、実行時に maxValue を書けばよい。
    /// </summary>
    public static class IntensityUnlock
    {
        /// <summary>DisasterData.m_intensity は Byte。255 が真の上限で、表示は 25.5 になる。</summary>
        public const float MaxIntensityByte = 255f;

        /// <summary>再試行の間隔（main スレッド更新の回数）。FindObjectOfType は毎フレーム回すには重い。</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>再試行の上限。パネルが現れない環境で永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private static bool _applied;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        public static void Reset()
        {
            _applied = false;
            _gaveUp = false;
            _framesSinceTry = 0;
            _attempts = 0;
        }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の試行は RetryIntervalFrames ごと。
        ///
        /// 災害パネルはレベルロード時点ではまだ構築されていないことがある。
        /// ロード時 1 回きりの試行にすると、その都市では二度と上限が上がらない。
        /// </summary>
        public static void Tick()
        {
            if (_applied || _gaveUp) return;
            if (_framesSinceTry++ < RetryIntervalFrames) return;
            _framesSinceTry = 0;
            Apply();
        }

        /// <summary>main スレッドから呼ぶ。</summary>
        public static void Apply()
        {
            if (_applied || _gaveUp) return;

            ModSettings.Ensure();
            if (!ModSettings.IntensityUnlock.value) { _gaveUp = true; return; }

            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                Log.Warn("gave up looking for the disaster intensity slider after "
                         + MaxAttempts + " attempts; cap not raised");
                return;
            }

            try
            {
                var panel = Object.FindObjectOfType<DisastersOptionPanel>();
                if (panel == null)
                {
                    // まだ構築されていない。Tick が後で再試行する。
                    Log.Diag("intensityUnlock", "DisastersOptionPanel not found yet; will retry");
                    return;
                }

                var slider = panel.Find<UISlider>("Slider");
                if (slider == null)
                {
                    Log.Warn("intensity slider not found; cap not raised");
                    return;
                }

                if (slider.maxValue >= MaxIntensityByte)
                {
                    _applied = true;
                    return;   // 他 MOD が既に上げている
                }

                Log.Info("raising intensity slider cap " + slider.maxValue + " -> " + MaxIntensityByte);
                slider.maxValue = MaxIntensityByte;
                _applied = true;
            }
            catch (System.Exception e)
            {
                Log.Error("intensity unlock failed", e);
            }
        }
    }
}
