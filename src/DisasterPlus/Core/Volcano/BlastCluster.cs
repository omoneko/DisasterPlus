using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>1 発ぶんの爆発。<c>DispatchEffect</c> 1 回に写す。</summary>
    public struct BlastBurst
    {
        /// <summary>噴出口からの水平オフセット（m）。</summary>
        public readonly float OffsetX;

        /// <summary>噴出口からの高さ（m）。</summary>
        public readonly float OffsetY;

        /// <summary>噴出口からの水平オフセット（m）。</summary>
        public readonly float OffsetZ;

        /// <summary><c>SpawnArea</c> の半径（m）。</summary>
        public readonly float RadiusMetres;

        /// <summary><c>DispatchEffect</c> の <c>magnitude</c>（＝粒子密度）。</summary>
        public readonly float Magnitude;

        /// <summary>
        /// 何フレーム遅らせて積むか。**同時に全部出さない** ——
        /// 同時に出すと 1 個の大きい爆発ではなく「1 個の平たい円盤」に見える。
        /// </summary>
        public readonly int DelayFrames;

        public BlastBurst(float offsetX, float offsetY, float offsetZ,
                          float radiusMetres, float magnitude, int delayFrames)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RadiusMetres = radiusMetres;
            Magnitude = magnitude;
            DelayFrames = delayFrames;
        }
    }

    /// <summary>
    /// 1 回の爆発を<b>何発の <c>DispatchEffect</c> に割るか</b>を決める。
    /// **エンジン非依存の純関数だけ。**
    ///
    /// ── なぜ「1 発」ではだめなのか（2026-08-22、所有者の指摘）──────────────
    ///
    /// &gt; 爆発のエフェクトがスケール通りではない、特に破局噴火の時の爆発が
    /// &gt; しょぼすぎます
    ///
    /// 以前は <c>DispatchEffect</c> を<b>1 回</b>呼び、半径だけを
    /// <c>火口半径 ×(0.35 + 0.45·強さ)</c> で広げていた。ところが
    /// <b><c>SpawnArea</c> の半径を広げても粒子は大きくならない</b> ——
    /// 1 フレームの粒子数は <c>max(100, π r²) × magnitude × 0.01 × rateOverTime</c> で
    /// 面積に比例するが、<c>Medium Explosion Particles</c> の粒 1 個の大きさは
    /// プレハブ側で固定である（<c>ShaderPool</c> 由来の実測メモと同じ話）。
    /// つまり半径を 2 倍にすると<b>同じ大きさの粒が薄く散る</b>だけで、
    /// 「大きい爆発」ではなく「まばらな爆発」になる。**指摘のとおりである。**
    ///
    /// 大きく見せる方法は 1 つしかない —— <b>数を増やして、ずらして重ねる</b>。
    /// 実際の火山の爆発も単一の球ではなく、火道から次々に噴き出す塊の集合である。
    ///
    /// ── ★★ 破局噴火は「環状火口列」から噴く ──────────────────────────
    ///
    /// カルデラ形成期の噴火は<b>中央火口からではなく、陥没した屋根のふちの
    /// 環状断層に沿って</b>噴き上がる（ring-fissure eruption）。これが
    /// 「破局噴火の爆発がしょぼい」の本質でもある —— 中央の 1 点だけを
    /// 光らせていたら、半径 5 km のカルデラのどこにも爆発は見えない。
    /// <see cref="For"/> に <c>ringRadiusMetres</c> を渡すと、発の一部を
    /// その環に沿って配る。
    ///
    /// ── 大きさは 2 つの軸で決まる ─────────────────────────────────
    ///
    /// <list type="number">
    /// <item><b>強さ</b>（<c>unit</c>）… 噴火の包絡線。今までどおり</item>
    /// <item><b>山の大きさ</b>（<c>sizeUnit</c>）… <b>ここが抜けていた。</b>
    ///   スライダーを 25.5 にしても、火口半径が変わるだけで発の数は同じだった</item>
    /// </list>
    /// </summary>
    public static class BlastCluster
    {
        /// <summary>1 回の爆発で積む発の数の下限（いちばん小さい火山・弱いとき）。</summary>
        public const int MinBursts = 3;

        /// <summary>同上の上限。**ここを超えて増やさない** ——
        /// <c>DispatchEffect</c> は 1 回ごとにゲームの効果キューを消費する。</summary>
        public const int MaxBursts = 40;

        /// <summary>山の大きさで発の数がどこまで増えるか（倍）。</summary>
        public const float SizeCountGain = 3.4f;

        /// <summary>大爆発（カルデラ形成期）で発の数がさらに何倍になるか。</summary>
        public const float ClimaxCountGain = 2.6f;

        /// <summary>大爆発で 1 発ぶんの密度が何倍になるか。</summary>
        public const float ClimaxMagnitudeGain = 1.8f;

        /// <summary>塊が散らばる範囲（火口半径に対する比）。</summary>
        public const float SpreadRatio = 1.15f;

        /// <summary>塊が積み上がる高さ（火口半径に対する比）。**横だけに散らさない。**</summary>
        public const float RiseRatio = 1.9f;

        /// <summary>1 発ぶんの <c>SpawnArea</c> 半径（火口半径に対する比）の下限。</summary>
        public const float BurstRadiusMinRatio = 0.22f;

        /// <summary>同上の上限。</summary>
        public const float BurstRadiusMaxRatio = 0.62f;

        /// <summary>全部の発が出そろうまでのフレーム数。</summary>
        public const int SpreadFrames = 26;

        /// <summary>
        /// 環（＝カルデラのふち）へ配る発の割合。大爆発のときだけ意味を持つ。
        /// 1 にしない —— 中央の火道も同時に噴いている。
        /// </summary>
        public const float RingShare = 0.55f;

        /// <summary>環に沿った発が環からどれだけばらつくか（環の半径に対する比）。</summary>
        public const float RingJitterRatio = 0.10f;

        /// <summary>
        /// この 1 回の爆発を何発に割るか。
        /// </summary>
        /// <param name="sizeUnit">
        /// 山の大きさ <c>[0,1]</c>。推奨サイズで 0、スライダー上端で 1。
        /// </param>
        /// <param name="climax">カルデラ形成期の大爆発か。</param>
        public static int CountFor(float unit, float sizeUnit, bool climax)
        {
            float u = Clamp01(unit);
            float s = Clamp01(sizeUnit);

            float n = MinBursts * (1f + SizeCountGain * s) * (0.45f + 0.55f * u);
            if (climax) n *= ClimaxCountGain;

            int count = (int)(n + 0.5f);
            if (count < MinBursts) count = MinBursts;
            if (count > MaxBursts) count = MaxBursts;
            return count;
        }

        /// <summary>
        /// <paramref name="index"/> 番目の発。<paramref name="index"/> は
        /// <c>[0, <see cref="CountFor"/>)</c>。
        /// </summary>
        /// <param name="craterRadiusMetres">火口半径（m）。塊の散らばりの基準。</param>
        /// <param name="ringRadiusMetres">
        /// 環状火口列の半径（m）。**0 なら環を使わない**（ふつうの噴火）。
        /// </param>
        /// <param name="seed">この爆発の種。</param>
        public static BlastBurst For(int index, int count, float unit, float sizeUnit,
                                     bool climax, float craterRadiusMetres,
                                     float ringRadiusMetres, uint seed)
        {
            if (index < 0) index = 0;
            if (count < 1) count = 1;
            if (index >= count) index = count - 1;

            float u = Clamp01(unit);
            float crater = IsBad(craterRadiusMetres) || craterRadiusMetres < MinRadius
                ? MinRadius
                : craterRadiusMetres;

            uint draw = (uint)index * 7u + 1u;
            float a = DeterministicRandom.Unit(seed, draw) * 6.2831853f;
            float rr = DeterministicRandom.Unit(seed, draw + 1u);
            float rise = DeterministicRandom.Unit(seed, draw + 2u);
            float sizePick = DeterministicRandom.Unit(seed, draw + 3u);
            float delayPick = DeterministicRandom.Unit(seed, draw + 4u);

            // ★★ 環状火口列（破局噴火だけ）。**発の何割かを環に沿って配る。**
            bool onRing = climax
                          && !IsBad(ringRadiusMetres)
                          && ringRadiusMetres > crater
                          && DeterministicRandom.Unit(seed, draw + 5u) < RingShare;

            float distance;
            if (onRing)
            {
                float jitter = (rr * 2f - 1f) * RingJitterRatio * ringRadiusMetres;
                distance = ringRadiusMetres + jitter;
            }
            else
            {
                // 円盤上に一様（sqrt）。中心にだけ固まらせない。
                distance = (float)Math.Sqrt(rr) * SpreadRatio * crater;
            }

            float offsetX = (float)Math.Cos(a) * distance;
            float offsetZ = (float)Math.Sin(a) * distance;

            // 積み上がる高さ。**環の発は低い**（ふちから横へ噴き出す）。
            float offsetY = rise * RiseRatio * crater * (onRing ? 0.35f : 1f);

            float radius = crater * (BurstRadiusMinRatio
                                     + (BurstRadiusMaxRatio - BurstRadiusMinRatio) * sizePick);
            if (radius < MinRadius) radius = MinRadius;

            float magnitude = EruptionEffectPlan.BlastMagnitude(u);
            // ★ 発を増やしたぶん 1 発を薄くしない —— 薄くすると、増やした意味が
            //   ちょうど打ち消される（面積で正規化する噴煙とはここが違う。
            //   あちらは連続、こちらは一発ものである）。
            if (climax) magnitude *= ClimaxMagnitudeGain;

            int delay = (int)(delayPick * SpreadFrames);
            if (delay < 0) delay = 0;
            if (delay > SpreadFrames) delay = SpreadFrames;

            return new BlastBurst(offsetX, offsetY, offsetZ, radius, magnitude, delay);
        }

        /// <summary>
        /// 山の大きさ <c>[0,1]</c>。推奨サイズ（<c>VolcanoShape.DefaultRadiusOf</c>）で 0、
        /// 形態の帯の上限で 1。**スライダーそのものではなく実寸で測る** ——
        /// 形態ごとに帯が違うので、スライダーの位置だけでは大きさが決まらない。
        /// </summary>
        public static float SizeUnitOf(float radiusMetres, float defaultRadiusMetres,
                                       float maxRadiusMetres)
        {
            if (IsBad(radiusMetres) || IsBad(defaultRadiusMetres) || IsBad(maxRadiusMetres)) return 0f;
            if (maxRadiusMetres <= defaultRadiusMetres) return 0f;
            if (radiusMetres <= defaultRadiusMetres) return 0f;

            return Clamp01((radiusMetres - defaultRadiusMetres)
                           / (maxRadiusMetres - defaultRadiusMetres));
        }

        private const float MinRadius = EruptionEffectPlan.MinRadiusMetres;

        private static float Clamp01(float v)
        {
            if (IsBad(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
