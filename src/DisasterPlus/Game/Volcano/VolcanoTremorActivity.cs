using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Volcano;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 位相 → 火山性地震の活動度 <c>[0,1]</c>。**ここ 1 か所だけが持つ対応表である。**
    ///
    /// ── なぜ切り出したか（2026-08-22）──────────────────────────
    ///
    /// この対応表を要る者が 2 人になった:
    ///
    ///   1. <see cref="VolcanoTremorShake"/> —— **main スレッド**、カメラを揺らす。
    ///      入力は <c>VolcanoSnapshot</c>（sim が publish したもの）。
    ///   2. <see cref="VolcanoTremorTrace"/> —— **sim スレッド**、地震計に記録する
    ///      （所有者の依頼「火山性地震は震度計に記録されていないのも修正して」）。
    ///      入力は sim 側の生の状態。
    ///
    /// 2 人が別々に <c>switch</c> を書くと、**片方だけ直した位相**が必ず出る ——
    /// 画面は揺れているのに記録には出ない（あるいはその逆）という、
    /// いちばん切り分けにくい食い違いになる。だから対応表は 1 つにする。
    ///
    /// ── ★★ 冷え具合の向きに注意（2026-08-22 に直した取り違え）─────────────
    ///
    /// <c>VolcanoLava.CoolUnit</c> は <b>1 が「まだ熱い」、0 が「冷え切った」</b>である
    /// （あちらの doc と <c>LavaGlow.CoolFade</c>）。
    /// ところが <c>VolcanicTremor.ActivityUnit</c> の <c>coolUnit</c> は
    /// <b>1 が「冷えきった」</b>で、**向きが逆**である。
    ///
    /// 実装は <c>VolcanoLava.CoolUnit</c> をそのまま渡していたので、余韻は
    /// <b>溶岩が熱いあいだ 0、冷え切ってから 0.42</b> という**真後ろ**の形になっていた。
    /// 噴火直後がいちばん静かで、冷え切ってから鳴り出す。
    /// ここで <c>1 − coolUnit</c> に直す。**変換はこの 1 か所だけで行う。**
    /// </summary>
    public static class VolcanoTremorActivity
    {
        /// <summary>
        /// 今の活動度 <c>[0,1]</c>。<paramref name="lavaCoolUnit"/> は
        /// <c>VolcanoLava.CoolUnit</c> の向き（**1 = まだ熱い**）で渡すこと。
        /// </summary>
        public static float For(VolcanoPhase phase, float progressUnit,
                                float eruptionIntensityUnit, float lavaCoolUnit)
        {
            switch (phase)
            {
                // 準備（破壊）と隆起 —— マグマが上がってきている段。**噴火の前から揺れる。**
                case VolcanoPhase.Clearing:
                case VolcanoPhase.Uplifting:
                    return VolcanicTremor.ActivityUnit(progressUnit, false, 0f, false, 0f);

                case VolcanoPhase.Erupting:
                    return VolcanicTremor.ActivityUnit(1f, true, eruptionIntensityUnit,
                                                       false, 0f);

                // 噴火が終わってから溶岩が冷えきるまで、余韻が引いていく。
                // ★ 向きを揃える（クラス doc）。1 − CoolUnit が「冷えた度合い」である。
                case VolcanoPhase.Flowing:
                case VolcanoPhase.Cooling:
                    return VolcanicTremor.ActivityUnit(1f, false, 0f, true,
                                                       1f - Clamp01(lavaCoolUnit));

                default:
                    return 0f;
            }
        }

        /// <summary>揺れが届く距離（山の半径の何倍か）。**外はきっかり 0。**</summary>
        public const float ReachRadiusFactor = 4.5f;

        /// <summary>
        /// 地動 <c>[-1,1]</c> を**②と同じ変位の単位**へ直す倍率。
        /// ②のバニラの理論最大（<c>ShakeWaveform.MaxDisplacement</c> ＝ 0.6）の 0.7 倍 ——
        /// 火山性地震は近くでは強く感じるが、**本震級の断層地震ではない**。
        ///
        /// ★★ <b>カメラ（<see cref="VolcanoTremorShake"/>）と記象
        /// （<see cref="VolcanoTremorTrace"/>）の両方がこれを使う。</b>
        /// 片方だけが生の <c>[-1,1]</c> を使うと、記象の縦の尺度は 3 本で共通
        /// なので、**火山性微動だけがバニラの本震より 1.7 倍大きい絵**になる。
        /// </summary>
        public const float DisplacementGain = 0.7f * ShakeWaveform.MaxDisplacement;

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
