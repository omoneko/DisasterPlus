using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>雲の塊 1 個ぶん（描画用）。位置は**噴出口からの相対**（m）。</summary>
    public struct PlumeParcel
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        /// <summary>塊の半径（m）。描画側は直径に直すこと。</summary>
        public readonly float RadiusMetres;

        /// <summary>不透明度 <c>[0,1]</c>。生まれと終わりで 0 になる。</summary>
        public readonly float Alpha;

        /// <summary>明るさ <c>[0,1]</c>。0 が噴出口近くの黒、1 が傘の白。</summary>
        public readonly float Brightness;

        /// <summary>回転角（度）。塊ごとにゆっくり回る。</summary>
        public readonly float RotationDegrees;

        public PlumeParcel(float x, float y, float z, float radiusMetres,
                           float alpha, float brightness, float rotationDegrees)
        {
            X = x;
            Y = y;
            Z = z;
            RadiusMetres = radiusMetres;
            Alpha = alpha;
            Brightness = brightness;
            RotationDegrees = rotationDegrees;
        }
    }

    /// <summary>
    /// 噴煙を<b>塊（parcel）の群れ</b>として動かす。**エンジン非依存の純関数だけ。**
    ///
    /// ── なぜ作り直したのか（2026-08-22、所有者の指摘）─────────────────────
    ///
    /// &gt; 噴煙のアニメーションもまだまだリアルではありません。幾何的なものではなく
    /// &gt; もっと自然的なカオスな煙のアニメーションを作ってほしいです。煙のエフェクトに
    /// &gt; 加えて核キノコ雲（MissileDisaster）のエフェクトも一部利用してリアルにして
    /// &gt; ください。
    ///
    /// <see cref="EruptionColumn"/> は柱を<b>9 段の円盤</b>に割って、そこへゲームの
    /// 粒子エフェクトを湧かせている。断面（教科書の 3 区間）としては正しいが、
    /// <b>段は動かない</b> —— 湧く場所が固定なので、遠目には「積み上がった 9 枚の板」
    /// に見える。**指摘のとおり幾何的である。**
    ///
    /// ── ★★ 何を変えたのか: オイラー的 → ラグランジュ的 ──────────────────
    ///
    /// <list type="bullet">
    /// <item><b>前</b>（オイラー的）… 空間に固定した 9 個の枠。そこを煙が通過する</item>
    /// <item><b>今</b>（ラグランジュ的）… <b>塊そのものを追いかける。</b>
    ///   1 個 1 個が火口で生まれ、上がり、膨らみ、風に流され、薄くなって消える</item>
    /// </list>
    ///
    /// 塊が個別の一生を持つと、群れは勝手にカオスになる ——
    /// **乱数で「がたつき」を足しているのではない。** 位相のずれた何百個の一生が
    /// 重なると、噴煙の縁で絶えず新しい瘤が湧いては崩れる、あの見え方になる。
    ///
    /// ── 使うのは MissileDisaster の<b>作り方</b>である ─────────────────────
    ///
    /// キノコ雲の <c>MushroomCloudPuffsFx</c> から借りるのは
    /// 「<b>放出を止めた <c>ParticleSystem</c> を描画係として使い、粒を毎フレーム
    /// <c>SetParticles</c> でこちらが置く</b>」という一点である
    /// （④の <c>TyphoonVortexPuffFx</c> と同じ。あちらで一度通した道である）。
    /// これでゲームの粒子プレハブの制約（粒の大きさが固定）から外れて、
    /// **塊を何百 m の大きさで描ける**。
    ///
    /// ★★ <b>形は借りない。</b> 核の雲は単発の泡（細い柄＋丸い笠）で、
    ///   火山は供給が続く柱である（<see cref="EruptionColumn"/> のクラス doc）。
    ///   ここが写すのはその <see cref="EruptionColumn"/> の断面のほうである。
    ///
    /// ── 1 個の塊の一生 ─────────────────────────────────────────
    ///
    /// <code>
    /// age = 0            火口の中で生まれる（半径 = 火口 × VentRadiusFactor）
    ///   ↓ 上昇は減速する（RiseDecayPower。ガス推力 → 浮力 → 中立）
    ///   ↓ 半径は巻き込みで増える（EntrainmentSlope）
    ///   ↓ 風下へ倒れる（BendPower。高いほど強い ＝ 風のシアー）
    ///   ↓ 渦にもまれる（TurbulenceOctaves。**これがカオスの実体**）
    /// age = life         傘の高さで横へ広がりきり、薄くなって消える
    /// </code>
    ///
    /// 塊の位相は種から決まるので、**同じ噴火は何度でも同じように見える**
    /// （<see cref="DeterministicRandom"/> だけ。この MOD の乱数の規律）。
    /// </summary>
    public static class PlumeParcels
    {
        /// <summary>塊の総数。**増やす前に実機の描画負荷を測ること。**</summary>
        public const int Count = 560;

        /// <summary>1 個の塊が生まれてから消えるまで（秒）。</summary>
        public const float LifeSeconds = 26f;

        /// <summary>
        /// 塊 1 個の半径が<b>そこでの柱の半径</b>の何倍か。
        ///
        /// ★★ <b>火口の半径を基準にしないこと。</b>（2026-08-22、オフラインで描いて
        ///   気づいた）柱は上へ行くほど太るので、火口基準で決めると
        ///   <b>根元では塊が柱よりでかく、傘では砂粒になる</b>。
        ///   高さごとの柱の太さに比例させれば、どこでも同じ「粒立ち」に見える。
        /// </summary>
        public const float ParcelRadiusRatio = 0.34f;

        /// <summary>塊ごとの大きさのばらつき（<see cref="ParcelRadiusRatio"/> に対する比）。</summary>
        public const float ParcelRadiusSpread = 0.55f;

        /// <summary>年を取るほど少しずつ膨らむ量（1 秒あたり、比）。</summary>
        public const float GrowthPerSecond = 0.022f;

        /// <summary>
        /// 乱れを重ねる回数。**1 では足りない**（きれいな波になる）。
        /// 3 で周期の違う渦が噛み合って、繰り返しが目で追えなくなる。
        /// </summary>
        public const int TurbulenceOctaves = 3;

        /// <summary>
        /// 乱れの大きさ（そのときの柱の半径に対する比）。
        /// **柱の太さに比例させる** —— 絶対値で足すと、細い根元だけが暴れる。
        /// </summary>
        public const float TurbulenceRatio = 0.30f;

        /// <summary>いちばんゆっくりした渦の周期（秒）。</summary>
        public const float TurbulenceBaseSeconds = 9f;

        /// <summary>塊が回る速さ（度／秒）の上限。</summary>
        public const float SpinDegreesPerSecond = 11f;

        /// <summary>生まれてから濃くなりきるまでの一生に対する割合。</summary>
        public const float FadeInFraction = 0.06f;

        /// <summary>薄くなりはじめる一生に対する割合。</summary>
        public const float FadeOutFraction = 0.62f;

        /// <summary>いちばん濃いときの不透明度。</summary>
        public const float PeakAlpha = 0.82f;

        /// <summary>
        /// 塊 1 個ぶんの状態。<paramref name="index"/> は <c>[0, <see cref="Count"/>)</c>。
        /// </summary>
        /// <param name="timeSeconds">噴火が始まってからの秒数（連続で増える値）。</param>
        /// <param name="ventRadiusMetres">火口の半径（m）。</param>
        /// <param name="columnHeightMetres">柱の高さ（m。<see cref="EruptionColumn"/> と同じ）。</param>
        /// <param name="windX">風の向きと速さ（m/秒）。</param>
        /// <param name="windZ">同上。</param>
        /// <param name="seed">この火山の種。</param>
        public static PlumeParcel At(int index, float timeSeconds, float ventRadiusMetres,
                                     float columnHeightMetres, float windX, float windZ,
                                     uint seed)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;

            float vent = Sane(ventRadiusMetres, 8f);
            float height = Sane(columnHeightMetres, EruptionColumn.HeightMinMetres);
            float t = IsBad(timeSeconds) ? 0f : timeSeconds;
            float wx = IsBad(windX) ? 0f : windX;
            float wz = IsBad(windZ) ? 0f : windZ;

            uint draw = (uint)index * 11u + 3u;

            // ★★ **位相をずらすのがカオスの入口である。** 全部を同時に生まれさせると、
            //    群れ全体が一斉に脈打つ（それこそ「幾何的」に見える）。
            float phase = DeterministicRandom.Unit(seed, draw);
            float age = Frac(t / LifeSeconds + phase) * LifeSeconds;
            float w = age / LifeSeconds;

            // ── 上昇（減速する）──────────────────────────────
            // w^(1/RiseDecayPower) 型。下で速く、上でゆるむ。
            float climb = Pow(w, 1f / EruptionColumn.RiseDecayPower);
            float y = climb * height;

            // ── 柱の断面のどこに居るか ──────────────────────────
            //
            // ★★ **向きと「軸からの割合」だけを引く。** 実距離は
            //    <see cref="ColumnRadiusAt"/>（＝その高さでの柱の半径）に掛けて出す。
            //    はじめ「火口の中の点 × 太り率 × 傘の広がり率」で置いていたが、
            //    <b>広がりを 2 度掛けていた</b>ので、柱ではなく画面いっぱいの
            //    塊になった（tools/PlumePreview で気づいた）。
            //    軸からの割合は一生変わらない —— 塊は柱と一緒に広がるのであって、
            //    柱の中を横切っていくわけではない。
            float birthAngle = DeterministicRandom.Unit(seed, draw + 1u) * 6.2831853f;
            float axisFraction = (float)Math.Sqrt(DeterministicRandom.Unit(seed, draw + 2u));

            float columnRadius = ColumnRadiusAt(climb, vent, height);

            float x = (float)Math.Cos(birthAngle) * axisFraction * columnRadius;
            float z = (float)Math.Sin(birthAngle) * axisFraction * columnRadius;

            // ── 風下へ倒れる（高いほど強い ＝ 風のシアー）─────────────
            float bend = Pow(climb, EruptionColumn.BendPower)
                         * EruptionColumn.BendFactor * height
                         / EruptionColumn.ReferenceWindMetresPerSecond;
            x += wx * bend;
            z += wz * bend;

            // ── ★★ 乱れ。**ここが「カオスな煙」の実体である。** ─────────
            //    周期の違う 3 つの渦を重ねる。塊ごとに種が違うので、
            //    隣り合う塊が別々の向きへ捻れる。
            float scale = columnRadius * TurbulenceRatio;
            for (int o = 0; o < TurbulenceOctaves; o++)
            {
                float period = TurbulenceBaseSeconds / (1 << o);
                float amp = scale / (1 << o);
                uint os = seed + (uint)(o * 7919);

                float px = DeterministicRandom.Unit(os, draw + 3u) * 6.2831853f;
                float py = DeterministicRandom.Unit(os, draw + 4u) * 6.2831853f;
                float pz = DeterministicRandom.Unit(os, draw + 5u) * 6.2831853f;

                float k = 6.2831853f / period;
                x += (float)Math.Sin(k * age + px) * amp;
                y += (float)Math.Sin(k * age + py) * amp * 0.5f;
                z += (float)Math.Sin(k * age + pz) * amp;
            }

            // ── 塊そのものの大きさ（**そこでの柱の太さに比例**）──────────
            float sizePick = DeterministicRandom.Unit(seed, draw + 8u);
            float radius = columnRadius * ParcelRadiusRatio
                           * (1f - ParcelRadiusSpread * 0.5f + ParcelRadiusSpread * sizePick)
                           * (1f + GrowthPerSecond * age);

            // ── 濃さ（生まれと終わりで 0）──────────────────────
            float alpha;
            if (w < FadeInFraction) alpha = w / FadeInFraction;
            else if (w > FadeOutFraction) alpha = (1f - w) / (1f - FadeOutFraction);
            else alpha = 1f;
            alpha *= PeakAlpha;

            // ── 明るさ。噴出口の近くは黒く、上は日を受けて白い ────────────
            float brightness = Clamp01(climb * 1.25f);

            float spin = (DeterministicRandom.Unit(seed, draw + 6u) * 2f - 1f)
                         * SpinDegreesPerSecond * age
                         + DeterministicRandom.Unit(seed, draw + 7u) * 360f;

            return new PlumeParcel(x, y, z, radius, Clamp01(alpha), brightness, spin);
        }

        /// <summary>
        /// 柱がその高さで持つ半径（m）。<paramref name="climbUnit"/> は
        /// <c>高さ / 柱の高さ</c>。<see cref="EruptionColumn"/> の断面と同じ形にする ——
        /// **2 つの表現が違う太さを名乗ると、灰の柱と塊の群れがずれて見える。**
        /// </summary>
        public static float ColumnRadiusAt(float climbUnit, float ventRadiusMetres,
                                           float columnHeightMetres)
        {
            float c = Clamp01(climbUnit);
            float vent = Sane(ventRadiusMetres, 8f);
            float height = Sane(columnHeightMetres, EruptionColumn.HeightMinMetres);

            float gasTop = EruptionColumn.GasThrustFraction;
            float base0 = vent * EruptionColumn.VentRadiusFactor;
            float gasTopRadius = vent * EruptionColumn.GasTopRadiusFactor;

            if (c <= gasTop)
            {
                float k = gasTop > 0f ? c / gasTop : 0f;
                return base0 + (gasTopRadius - base0) * k;
            }

            // 対流域: 1 m 上がるごとに EntrainmentSlope だけ太る。
            float rise = (c - gasTop) * height;
            float r = gasTopRadius + rise * EruptionColumn.EntrainmentSlope;

            // ★★ **傘はここで 1 度だけ広げる。** 呼び出し側でも掛けると
            //    二乗になって、柱ではなく塊になる（このクラスの置き方の doc）。
            if (c > EruptionColumn.UmbrellaBaseFraction)
            {
                float umbrella = (c - EruptionColumn.UmbrellaBaseFraction)
                                 / (1f - EruptionColumn.UmbrellaBaseFraction);
                r *= 1f + (EruptionColumn.UmbrellaSpread - 1f) * umbrella;
            }

            return r;
        }

        private static float Frac(float v)
        {
            if (IsBad(v)) return 0f;
            float f = v - (float)Math.Floor(v);
            return f < 0f ? f + 1f : f;
        }

        private static float Pow(float v, float p)
        {
            if (v <= 0f) return 0f;
            return (float)Math.Pow(v, p);
        }

        private static float Sane(float v, float fallback)
        {
            return IsBad(v) || v <= 0f ? fallback : v;
        }

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
