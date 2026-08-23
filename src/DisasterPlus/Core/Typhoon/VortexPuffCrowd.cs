using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>群れの粒 1 個。位置は**渦の中心を原点とした比**である。</summary>
    public struct CrowdPuff
    {
        /// <summary>渦の中心からの角度（ラジアン、回転を足す前）。</summary>
        public readonly float AngleRadians;

        /// <summary>渦の半径に対する比。</summary>
        public readonly float RadiusFraction;

        /// <summary>雲の厚みに対する比。</summary>
        public readonly float HeightFraction;

        /// <summary>渦の半径に対する、粒の**半径**の比。</summary>
        public readonly float SizeFraction;

        /// <summary>濃さ <c>[0,1]</c>。腕の先ほど薄い。</summary>
        public readonly float DensityFraction;

        /// <summary>元になった段（甲板・塔・傘）。色と並び順に使う。</summary>
        public readonly VortexCloudLayer Layer;

        public CrowdPuff(float angleRadians, float radiusFraction, float heightFraction,
                         float sizeFraction, float densityFraction, VortexCloudLayer layer)
        {
            AngleRadians = angleRadians;
            RadiusFraction = radiusFraction;
            HeightFraction = heightFraction;
            SizeFraction = sizeFraction;
            DensityFraction = densityFraction;
            Layer = layer;
        }
    }

    /// <summary>
    /// <see cref="VortexPuffLayout"/> の <b>88 個の円盤</b>を、**中を埋める雲の粒の群れ**へ広げる。
    /// <b>Core なのでエンジンには一切触らない</b>（状態も持たない）。
    ///
    /// ── なぜ要るのか（2026-08-22、オフラインで測って分かった）───────────────
    ///
    /// 白い雲の粒を自前で置く作り直し（<c>Game/Typhoon/TyphoonVortexPuffFx</c>）の
    /// 最初の版は、<see cref="VortexPuffLayout"/> の 88 個をそのまま 1 粒ずつ置いた。
    /// <c>tools/TyphoonPreview</c> で描いて測ると:
    ///
    /// <code>
    /// 粒の面積 ÷ 渦の面積 = 0.41   ← 1.0 を割ると穴が空く
    /// </code>
    ///
    /// 絵も数字どおりで、**渦ではなく点々**だった。原因ははっきりしている ——
    /// あの 88 個は<b>円盤</b>であって粒ではない。旧実装はその円盤の中へ
    /// バニラの粒子を数千個<b>撒いて</b>いたので埋まっていた。
    /// 撒くのをやめて置くなら、**円盤の中身もこちらで作らなければならない。**
    ///
    /// ── 何個置くか ────────────────────────────────────
    ///
    /// 円盤の面積に比例して配り、合計を <see cref="TotalCount"/> に収める。
    /// 面積に比例させるのは、そうしないと**外側の大きい円盤だけがすかすかになる**
    /// からである（旧実装が <c>MagnitudeFor</c> で密度を解き直していたのと同じ理由）。
    ///
    /// <see cref="TotalCount"/> はミサイル MOD のキノコ雲（620 個）と同じ桁である。
    /// あちらは 1 都市に 1 つだが、台風も同時に 1 つなので釣り合う。
    ///
    /// ── ★★ 種にフレーム番号を混ぜない ─────────────────────────
    ///
    /// 粒の位置は<b>添字だけ</b>から決まる（<see cref="DeterministicRandom"/>）。
    /// 混ぜると毎フレーム別の場所へ跳んで、雲ではなく砂嵐になる。
    /// 動きは呼び出し側が足す回転だけで、**群れの形は動かない**。
    /// </summary>
    public static class VortexPuffCrowd
    {
        /// <summary>置く粒の総数。</summary>
        public const int TotalCount = 640;

        /// <summary>
        /// 1 つの円盤に配る粒の下限。**0 にしない** —— 0 にすると
        /// いちばん内側の細い円盤（眼の壁の下段）が消えて、眼が広がって見える。
        /// </summary>
        public const int MinPerDisc = 2;

        /// <summary>
        /// 粒 1 個の半径（**渦の半径**に対する比）の基準に掛ける倍率。
        ///
        /// ★★ <b>円盤の大きさには比例させない。</b>（2026-08-22、描いて分かった。）
        ///   最初は「その円盤の半径の 0.62 倍」にしていたが、外周の円盤は
        ///   中心付近の何倍も大きいので、**外側だけ 1.8 km の塊**になり、
        ///   腕が読めない一枚の綿になった（<c>docs/images/typhoon</c> で確認）。
        ///
        ///   実際の雲は、渦のどこにあっても<b>似た大きさの塔</b>の集まりである。
        ///   だから基準は段ごとの比（<c>VortexPuffLayout.SizeFractionOf</c> ——
        ///   甲板 0.055 / 塔 0.040 / 傘 0.070）で、そこにこの倍率を掛ける。
        /// </summary>
        public const float PuffSizeGain = 1.0f;

        /// <summary>粒ごとの大きさの散らばり（±この比）。**揃うと人工物に見える。**</summary>
        public const float SizeJitter = 0.30f;

        /// <summary>
        /// **半径方向**にどこまで散らすか（円盤の半径に対する比）。腕の太さになる。
        /// </summary>
        public const float RadialScatterRatio = 1.15f;

        /// <summary>
        /// **腕に沿う向き**にどこまで散らすか（渦の半径に対する比、片側）。
        ///
        /// ★★ <b>これが無いと腕が繋がらない。</b>（2026-08-22、描いて分かった。）
        ///   円盤の中だけに散らしていた頃は、88 個の円盤がそのまま
        ///   **88 個の綿の塊**として見え、腕は「点線」だった
        ///   （<c>docs/images/typhoon/vortex-owned-plan.png</c> の 2 版目）。
        ///
        ///   腕に沿った隣の円盤との間隔は、渦の半径に対しておよそ 0.2〜0.3 である
        ///   （3 本の腕 × 5 列を <c>SpiralTurns</c> ＝ 0.45 回転に配っている）。
        ///   その半分より広く散らせば、隣どうしが重なって 1 本の帯になる。
        /// </summary>
        public const float AlongArmScatterRatio = 0.16f;

        private const uint AngleSalt = 0x43524F57u;
        private const uint RadiusSalt = 0x43524F58u;
        private const uint HeightSalt = 0x43524F59u;
        private const uint SizeSalt = 0x43524F5Au;

        /// <summary>
        /// 群れを <paramref name="into"/> へ書く。書いた数を返す
        /// （<paramref name="into"/> が <see cref="TotalCount"/> 未満なら 0）。
        ///
        /// **毎フレーム呼んでよい**（確保 0 バイト、分岐だけ）。
        /// </summary>
        public static int Build(CrowdPuff[] into)
        {
            if (into == null || into.Length < TotalCount) return 0;

            // 1) 円盤ごとの面積。**比例配分の分母である。**
            float totalArea = 0f;
            for (int i = 0; i < VortexPuffLayout.PuffCount; i++)
            {
                float disc = VortexPuffLayout.PuffAt(i).DiscFraction;
                if (disc > 0f) totalArea += disc * disc;
            }
            if (!(totalArea > 0f)) return 0;

            // 2) 面積に比例して配る。端数と下限のぶんは 3) で詰める。
            int written = 0;

            for (int i = 0; i < VortexPuffLayout.PuffCount && written < TotalCount; i++)
            {
                VortexPuff disc = VortexPuffLayout.PuffAt(i);
                if (!(disc.DiscFraction > 0f)) continue;

                float share = disc.DiscFraction * disc.DiscFraction / totalArea;
                int count = (int)(TotalCount * share + 0.5f);
                if (count < MinPerDisc) count = MinPerDisc;

                for (int k = 0; k < count && written < TotalCount; k++)
                {
                    into[written] = Scatter(disc, i, k);
                    written++;
                }
            }

            // 3) まだ余っていたら、**大きい円盤から**順に足して埋める。
            //    余りを捨てると、置く数が回ごとに変わって見える。
            for (int pass = 0; written < TotalCount; pass++)
            {
                bool grew = false;

                for (int i = 0; i < VortexPuffLayout.PuffCount && written < TotalCount; i++)
                {
                    VortexPuff disc = VortexPuffLayout.PuffAt(i);
                    if (!(disc.DiscFraction > 0f)) continue;

                    into[written] = Scatter(disc, i, 1000 + pass * 64 + i);
                    written++;
                    grew = true;
                }

                if (!grew) break;   // 円盤が 1 つも無い（起こらないが、無限に回らない）
            }

            return written;
        }

        /// <summary>
        /// 円盤 <paramref name="discIndex"/> の中の <paramref name="k"/> 番目の粒。
        /// **添字だけで決まる**（クラス doc）。
        /// </summary>
        private static CrowdPuff Scatter(VortexPuff disc, int discIndex, int k)
        {
            uint seed = unchecked((uint)(discIndex * 7919 + k * 104729));

            // ★★ **円盤の局所座標で散らす** —— 半径方向は腕の太さ、
            //    腕に沿う向きは隣の円盤との繋がりになる（<c>AlongArmScatterRatio</c>）。
            //    円の中へ一様に散らすのをやめたのは、それでは腕が点線になったからである。
            float radial = (2f * DeterministicRandom.Unit(seed, RadiusSalt) - 1f)
                           * disc.DiscFraction * RadialScatterRatio;
            float along = (2f * DeterministicRandom.Unit(seed, AngleSalt) - 1f)
                          * AlongArmScatterRatio;

            float radius = disc.RadiusFraction + radial;
            if (radius < 0.001f) radius = 0.001f;

            // 弧長 → 角度。**半径で割る**ので、内側ほど角度としては大きく散る
            // （腕の内側は詰まっている、という実際の形と同じ向きである）。
            float angle = disc.AngleRadians + along / radius;

            // 帯の中の高さ。円盤は段の下端なので、上へだけ散らす（旧実装と同じ約束）。
            float height = disc.HeightFraction
                           + disc.BandFraction * DeterministicRandom.Unit(seed, HeightSalt);

            // ★ 大きさは**段**で決まる（クラス doc）。円盤の大きさには比例させない。
            float size = VortexPuffLayout.SizeFractionOf(disc.Layer) * PuffSizeGain
                         * (1f + SizeJitter * (2f * DeterministicRandom.Unit(seed, SizeSalt) - 1f));
            if (size < 0.001f) size = 0.001f;

            return new CrowdPuff(angle, radius, height, size, disc.DensityFraction, disc.Layer);
        }
    }
}
