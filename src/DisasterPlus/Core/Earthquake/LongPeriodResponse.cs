namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 長周期地震動の応答モデル。**これは本 MOD が発明した物理であって、
    /// バニラにも現実の地震学にも対応する値は無い。**
    ///
    /// バニラの揺れは 0.63 / 0.17 rad/frame の正弦 2 本だけで、低周波成分は
    /// 256 フレーム周期の包絡線（振幅の脈動）であって地震動ではない
    /// （IL 事実文書 §A-7）。建物の高さは揺れにも被害にも一切入っていない
    /// （<c>DisasterHelpers.DestroyBuildings</c> が見るのは <c>m_position</c> の距離だけ、§A-3）。
    /// つまりここにあるのは「可視化」ではなく「追加」である。
    ///
    /// **単位は全て sim フレーム。** 秒でも実在の周期でもない。実在の
    /// T ≒ 0.02h [秒] という関係の**形だけ**を借りて、フレームという単位に
    /// 置き換えている。したがって <see cref="PeriodFramesPerMetre"/> = 4 は
    /// 物理定数ではなく、「高さ 52 m 前後の建物が最も揺すられる」という
    /// 遊びの調整値である。**実在の建築や地震学の値を名乗らないこと。**
    ///
    /// 呼び出し側はこの値を必ず第 2 層として表示すること（<c>Strings.SourceModel</c>）。
    ///
    /// ── 波形グラフには絶対に足さない（全体レビュー I5 との関係）─────────────
    ///
    /// 第 1 層の波形グラフは、速度 3 で 9 フレームおきに 1 点だけ取っていた時期に
    /// **周期 ≒92 フレームの偽の長周期波**を描いていた（エイリアシング）。
    /// その見た目は「長周期地震動そのもの」で、§A-7 が「バニラに長周期成分は無い」と
    /// 確定させている以上、第 1 層のグラフが第 2 層の現象を描いていたことになる。
    /// 修正は <see cref="ShakeWaveform.FirstUnsampledFrame"/> による 1 フレーム 1 点の
    /// 埋め戻しで、**グラフに低周波成分が出たらそれは今も不具合**という状態を作った。
    ///
    /// **本モデルはその状態を壊さない。** ここで作る低周波成分は
    /// <b>変位を一切生まない</b> —— 被害選定の確率にしか入らず、
    /// <see cref="ShakeWaveform"/> にも <c>SeismographRecorder</c> にも 1 項も足さない。
    /// つまり「波形に見える長周期の揺らぎ」は、この機能を ON にしても
    /// 依然として**サンプリングの不具合以外ではありえない**。
    /// 将来ここを波形へ流し込みたくなったら、その瞬間にこの判別が消えることを
    /// 先に理解すること。
    /// </summary>
    public static class LongPeriodResponse
    {
        /// <summary>
        /// 長周期成分の角速度（rad/frame）。バニラの 0.63 / 0.17 より 1 桁小さい。
        /// **本 MOD が選んだ値であり、実測でも実在の値でもない。**
        /// </summary>
        public const float WaveRateRadPerFrame = 0.03f;

        /// <summary>
        /// 建物の高さ 1 m あたりの固有周期（フレーム）。
        /// 実在の T ≒ 0.02h [秒] の**形だけ**を借りた調整値である。
        /// </summary>
        public const float PeriodFramesPerMetre = 4f;

        /// <summary>共振の鋭さ。大きいほど広い高さ帯が影響を受ける（フレーム）。</summary>
        public const float ResonanceWidthFrames = 60f;

        /// <summary>
        /// 到達範囲がバニラの全体円盤 <c>R = 2000 + 20i</c> の何倍か。
        /// 「短周期より遠くまで届く」を表現するためだけの倍率。
        /// </summary>
        public const float RangeFactor = 2f;

        /// <summary>
        /// この高さ未満の建物は対象外。低層は長周期の影響を受けない、という
        /// **前提を明示するための閾値**であって、実測された境界ではない。
        /// </summary>
        public const float MinHeightMetres = 20f;

        /// <summary>
        /// 1 回の走査で <see cref="ExtraCollapseChance"/> が返す追加倒壊確率の上限。
        ///
        /// **バニラの全体円盤が 0.02 なのに対し、これは最大 0.25 と 1 桁大きい。**
        /// 意図的だが、だからこそこの機能は既定 OFF で、強さのスライダーで
        /// 抑えられるようになっている。
        ///
        /// ── **これは実際に使われる確率の上限ではない**（第 2 層レビュー M8）─────
        ///
        /// 呼び出し側（<c>LongPeriodDamage.IsSelected</c> と、同じ順序を写している
        /// <c>EarthquakeLongPeriodText.CursorRow</c>）は、このクランプの**後**に
        /// <c>TimeOfDayFactor.Of(hour)</c>（最大 <c>TimeOfDayFactor.NightFactor</c>
        /// ＝ 1.15）を掛ける。したがって画面と被害選定に出る実際の上限は
        /// <c>0.25 × 1.15 ＝ 0.2875</c> である。
        ///
        /// 順序を入れ替えないのは、この定数を「**このモデル自身が出す最大値**」という
        /// 意味のままにしておきたいからで、時間帯係数はモデルの外から掛かる別の量である。
        /// その代わり、上限を名乗る場所（この doc と診断ダンプの model 行）は
        /// **必ず 0.2875 まで含めて言う** —— 0.25 とだけ書くと、
        /// 「確信を持って誤った数値」になる。
        /// </summary>
        public const float MaxExtraChance = 0.25f;

        /// <summary>強さスライダーの満目盛り。<c>strength / 10</c> が倍率になる。</summary>
        public const float MaxStrength = 10f;

        /// <summary>長周期成分の周期（フレーム）＝ <c>2π / WaveRateRadPerFrame</c> ≒ 209。</summary>
        public static float WavePeriodFrames
        {
            get { return (float)(2.0 * System.Math.PI / WaveRateRadPerFrame); }
        }

        /// <summary>
        /// 建物の固有周期（フレーム）。高さに比例させているだけで、
        /// 質量も剛性も階数も見ていない。**これは近似ではなく、単なる遊びの写像である。**
        /// </summary>
        public static float BuildingPeriodFrames(float heightMetres)
        {
            if (float.IsNaN(heightMetres) || heightMetres <= 0f) return 0f;
            return heightMetres * PeriodFramesPerMetre;
        }

        /// <summary>
        /// 共振倍率 ∈ (0, 1]。<see cref="WavePeriodFrames"/> でちょうど 1 になり、
        /// そこから離れるほど 0 に近づく（ローレンツ型）。
        /// </summary>
        public static float Resonance(float buildingPeriodFrames)
        {
            if (float.IsNaN(buildingPeriodFrames)) return 0f;

            float detune = (buildingPeriodFrames - WavePeriodFrames) / ResonanceWidthFrames;
            float r = 1f / (1f + detune * detune);
            if (r < 0f) return 0f;
            return r > 1f ? 1f : r;
        }

        /// <summary>
        /// 長周期成分の到達範囲（m）。全体円盤の <see cref="RangeFactor"/> 倍。
        /// **揺れそのものに半径の打ち切りは無い**（§A-7）ので、これは
        /// 「本 MOD がこの被害を足す範囲」であって物理的な境界ではない。
        /// </summary>
        public static float RangeOf(byte intensity)
        {
            return SeismicIntensity.RadiusOf(intensity) * RangeFactor;
        }

        /// <summary>
        /// 1 回の走査で足す追加倒壊確率。
        ///
        /// <code>
        /// Resonance(h*4) × (1 - d/Range) × (strength/10) × 0.25
        /// </code>
        ///
        /// **0 を返す条件は全て「壊さない」側に倒れている**（高さが読めない・低層・
        /// 範囲外・強さ 0・壊れた入力）。高さが分からないのに「高層ほど壊れる」を
        /// 適用したら、それはこの MOD が最も嫌う形の嘘になる。
        /// </summary>
        /// <param name="heightMetres">建物の高さ（m）。**0 は「読めなかった」**。</param>
        /// <param name="distance">震央からの水平距離（m）。</param>
        /// <param name="intensity"><c>DisasterData.m_intensity</c> の生値。</param>
        /// <param name="strength">設定の強さ（0〜10）。0 で完全に無効。</param>
        public static float ExtraCollapseChance(float heightMetres, float distance,
                                                byte intensity, float strength)
        {
            if (float.IsNaN(heightMetres) || float.IsNaN(distance) || float.IsNaN(strength))
            {
                return 0f;
            }
            if (heightMetres < MinHeightMetres) return 0f;
            if (strength <= 0f) return 0f;

            float range = RangeOf(intensity);
            if (range <= 0f) return 0f;
            if (distance < 0f) distance = 0f;
            if (distance >= range) return 0f;

            float scale = strength / MaxStrength;
            if (scale > 1f) scale = 1f;

            float falloff = 1f - distance / range;
            float chance = Resonance(BuildingPeriodFrames(heightMetres))
                           * falloff * scale * MaxExtraChance;

            if (chance < 0f) return 0f;
            return chance > MaxExtraChance ? MaxExtraChance : chance;
        }
    }
}
