namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 地震計のカバレッジが警報のリードタイムをどれだけ延ばすか。
    ///
    /// IL 事実文書 §A-2、<c>EarthquakeAI.SimulationStep</c> の Emerging 分岐:
    /// <code>
    ///   CheckLocalResource(EarthquakeCoverage, m_targetPosition, out coverage)
    ///   coverage = Mathf.Min(coverage, 100)
    ///   lead     = coverage * 6437 / 100 + 1755      // 整数除算（div.un）
    ///   if (currentFrame + lead &gt;= m_activationFrame) DetectDisaster(id, located: coverage != 0)
    /// </code>
    ///
    /// カバレッジ 0 で 1755 フレーム（約 38.6 ゲーム内分）、100 で 8192 フレーム
    /// （= 65536/8 = **ちょうど 3.0 ゲーム内時間**）。
    ///
    /// **カバレッジは震央で読まれる**（<c>m_targetPosition</c>）。地震計の位置でも
    /// カーソルの位置でもない。効果範囲が震央に届いていない地震計は、その地震に対して
    /// 何も寄与しない（§A-2 / §C-2）。
    ///
    /// **地震計の効果はもう 1 つある。** <c>located</c> が <c>coverage != 0</c> で決まるので、
    /// 地震計が無いと地震はハザードマップに一切描かれない（§A-6 のゲート）。
    /// そちらは <see cref="DisasterPhases.PaintsHazardMap(bool, EarthquakePhase)"/> が扱う。
    /// **地震計自身は <c>DetectDisaster</c> を呼ばない**（§C-2）——呼ぶのは
    /// <c>EarthquakeAI.SimulationStep</c> で、地震計がやっているのは
    /// 半径内に resource 22 を撒くことだけである。因果の向きを逆に読まないこと。
    ///
    /// ここは**バニラの式と定数だけ**でできている（第 1 層）。新しい物理は 1 つも無い。
    /// </summary>
    public static class WarningLeadTime
    {
        /// <summary>カバレッジ 0 のときのリードタイム。IL のリテラル <c>ldc.i4 1755</c>。</summary>
        public const int BaseFrames = 1755;

        /// <summary>カバレッジ 100 で上乗せされる分。IL のリテラル <c>ldc.i4 6437</c>。</summary>
        public const int BonusFrames = 6437;

        /// <summary><c>Mathf.Min(coverage, 100)</c> の 100。</summary>
        public const int MaxCoverage = 100;

        /// <summary>
        /// バニラの <c>Mathf.Min(coverage, 100)</c>。負値はゲームには現れないが、
        /// 読み取りが壊れたときに負のリードタイムを作らないよう 0 で止める。
        /// </summary>
        public static int ClampCoverage(int coverage)
        {
            if (coverage < 0) return 0;
            if (coverage > MaxCoverage) return MaxCoverage;
            return coverage;
        }

        /// <summary>
        /// カバレッジ（生値でよい。中でクランプする）→ リードタイム（フレーム）。
        ///
        /// **整数除算であることが重要。** float で書くと端数の扱いがゲームとずれ、
        /// ゲームが実際には使わないリードタイムを表示することになる
        /// （例: カバレッジ 1 は 1819 であって 1819.37 ではない）。
        /// </summary>
        public static int FramesFor(int coverage)
        {
            return ClampCoverage(coverage) * BonusFrames / MaxCoverage + BaseFrames;
        }

        /// <summary>
        /// リードタイムをゲーム内分で。
        ///
        /// <paramref name="framesPerMinute"/> は呼び出し側から渡す（Game 側は
        /// <c>FeatureHost.FramesPerMinute</c>）。**定数を直書きしない** ——
        /// ③でこれを直書きして全ての持続時間が 4 倍ずれた前科がある。
        ///
        /// 換算できないときは 0 を返す。呼び出し側はこれを「0 分」として表示せず、
        /// 行ごと出さないこと（NaN や無限大をここから漏らさないための番人であって、
        /// 「0 分」という意味のある値ではない）。
        /// </summary>
        public static float MinutesFor(int coverage, float framesPerMinute)
        {
            // NaN は比較演算子を全て false にするので、明示的に弾く。
            if (float.IsNaN(framesPerMinute) || framesPerMinute <= 0f) return 0f;
            return FramesFor(coverage) / framesPerMinute;
        }
    }
}
