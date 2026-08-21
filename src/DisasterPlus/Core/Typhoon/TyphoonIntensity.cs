namespace DisasterPlus.Core.Typhoon
{
    /// <summary>
    /// ④の台風の**最盛期強度**（<c>peak</c>）の受け入れ範囲。
    /// <b>Core なのでエンジンには一切触らない。</b>
    ///
    /// ── なぜ 0 を受け取らないのか ──────────────────────────────
    ///
    /// バニラの強度スライダーの生値は <c>[0, 255]</c> で、そのまま
    /// <c>DisasterData.m_intensity</c> になる（<c>IntensitySlider</c> のクラス doc）。
    /// つまりプレイヤーはスライダーを 0 まで下げられる。**強度 0 の台風は
    /// 「起きたが何もしない台風」**であり、災害スロットを 1 個消費して
    /// 8192 フレーム居座るだけの、どこも壊れていないのに何も起きない状態を作る。
    /// 押した人から見れば「ボタンが効かなかった」と区別が付かない。
    ///
    /// そこで<b>下限へ引き上げる</b>。断るのではなく引き上げるのは、
    /// バニラの災害タイルが同じ操作で必ず何かを起こすからである ——
    /// ④だけがクリックに無反応で応えると、そちらのほうが説明の付かない挙動になる。
    /// 引き上げたことは呼び出し側が <see cref="WasRaised"/> で名乗れる。
    ///
    /// <see cref="MinPeak"/> を設定画面のスライダーの下限（10）と揃えてあるのは、
    /// 「設定で選べる最弱」と「タイルで選べる最弱」を同じにするためである。
    /// </summary>
    public static class TyphoonIntensity
    {
        /// <summary>受け付ける最小の最盛期強度。設定画面のスライダー下限と同じ。</summary>
        public const int MinPeak = 10;

        /// <summary>受け付ける最大の最盛期強度（<c>DisasterData.m_intensity</c> は byte）。</summary>
        public const int MaxPeak = 255;

        /// <summary>
        /// 依頼された生値を、実際に使える最盛期強度へ落とす。
        /// <c>[<see cref="MinPeak"/>, <see cref="MaxPeak"/>]</c> に収まる。
        /// </summary>
        public static byte PeakOf(int requested)
        {
            if (requested < MinPeak) return (byte)MinPeak;
            if (requested > MaxPeak) return (byte)MaxPeak;
            return (byte)requested;
        }

        /// <summary>依頼された値が下限に引き上げられたか（診断とログのため）。</summary>
        public static bool WasRaised(int requested)
        {
            return requested < MinPeak;
        }
    }
}
