using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Typhoon
{
    /// <summary>雲の塊 1 個ぶん（描画用）。位置は**台風の目からの相対**（m）。</summary>
    public struct TyphoonParcel
    {
        public readonly float X;

        /// <summary>雲底からの高さ（m）。</summary>
        public readonly float Y;

        public readonly float Z;

        /// <summary>塊の半径（m）。描画側は直径に直すこと。</summary>
        public readonly float RadiusMetres;

        /// <summary>不透明度 <c>[0,1]</c>。生まれと終わりで 0 になる。</summary>
        public readonly float Alpha;

        /// <summary>明るさ <c>[0,1]</c>。0 が雲底の影、1 が雲頂の日向。</summary>
        public readonly float Brightness;

        /// <summary>回転角（度）。</summary>
        public readonly float RotationDegrees;

        public TyphoonParcel(float x, float y, float z, float radiusMetres,
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
    /// 台風の雲を<b>塊（parcel）の群れ</b>として動かす。**エンジン非依存の純関数だけ。**
    ///
    /// ── 所有者の指示（2026-08-22）─────────────────────────────────
    ///
    /// &gt; 台風の雲のエフェクトは一瞬だけ現れて消えてしまいます。火山の噴火の雲が
    /// &gt; 質感としてはふさわしいので、噴火雲エフェクトを応用して台風の雲を
    /// &gt; 作ってください。
    ///
    /// ── ★★ <see cref="Volcano.PlumeParcels"/> と同じ作り ───────────────────
    ///
    /// 噴煙で通った道をそのまま使う:
    ///
    /// <list type="bullet">
    /// <item>塊が<b>1 個ずつ一生を持つ</b>（生まれ → 動き → 薄くなって消える）。
    ///   位相がずれた何百個の一生が重なると、群れは勝手にカオスになる</item>
    /// <item>周期の違う渦を <see cref="TurbulenceOctaves"/> 個重ねる。
    ///   1 つだときれいな波になり、繰り返しが目で追える</item>
    /// <item>描くのは<b>放出を止めた <c>ParticleSystem</c></b>で、
    ///   粒は毎フレーム <c>SetParticles</c> でこちらが置く</item>
    /// </list>
    ///
    /// ★★ <b>「一瞬だけ現れて消える」がこれで直る理由。</b>
    ///   前の <c>VortexPuffCrowd</c> は<b>添字だけで決まる静止した並び</b>で、
    ///   動くのは呼び出し側が足す回転だけだった。そのため
    ///   <b>雲そのものは 1 度置いたら二度と更新されない</b>のと同じで、
    ///   借り物のバニラ粒子（寿命で自然に死ぬ）が消えたあとは
    ///   何も残らなかった。ここは<b>毎フレーム位置が変わる</b>ので、
    ///   置き直しが止まらないかぎり雲は消えない。
    ///
    /// ── 形は台風のものであって、噴煙ではない ────────────────────────
    ///
    /// 借りるのは<b>作り方</b>で、形は別である:
    ///
    /// <code>
    /// 噴煙: 火口で生まれ、上がり、傘で横へ広がる（鉛直の柱）
    /// 台風: 外側で生まれ、**らせんを描いて目へ吸い込まれ**、
    ///       壁雲で上がり、雲頂から外へ吐き出される（水平の渦）
    /// </code>
    ///
    /// <see cref="EyeFraction"/> の内側は<b>雲を置かない</b> ——
    /// 台風の目は本当に晴れている。ここを埋めると、ただの円盤に見える。
    /// </summary>
    public static class TyphoonCloudParcels
    {
        /// <summary>塊の総数。**増やす前に実機の描画負荷を測ること。**</summary>
        public const int Count = 900;

        /// <summary>1 個の塊が生まれてから消えるまで（秒）。**台風は噴煙よりゆっくり。**</summary>
        public const float LifeSeconds = 64f;

        /// <summary>
        /// 台風の目の半径（外周半径に対する比）。**ここには雲を置かない。**
        ///
        /// ★★ 0.10 では埋まった（2026-08-22、tools/TyphoonParcelPreview で気づいた。
        ///   「目の被覆 100%」）。塊 1 個の半径が目の半径と同じくらいあったので、
        ///   壁雲に置いた塊が<b>そのまま目の中まではみ出していた</b>。
        ///   <b>塊の内側の縁</b>で判定すること（<see cref="At"/>）。
        /// </summary>
        public const float EyeFraction = 0.20f;

        /// <summary>壁雲（いちばん濃く、いちばん高い環）の半径（同上）。</summary>
        public const float EyewallFraction = 0.28f;

        /// <summary>一生のあいだに内側へ寄る量（外周半径に対する比）。</summary>
        public const float InflowFraction = 0.34f;

        /// <summary>一生のあいだに回る角度（ラジアン）。**らせんの巻き数を決める。**</summary>
        public const float SwirlRadians = 2.6f;

        /// <summary>内側ほど速く回る度合い（0 なら剛体回転＝板が回って見える）。</summary>
        public const float DifferentialSpin = 1.7f;

        /// <summary>
        /// らせん状の雨雲帯（rainband）の本数。
        ///
        /// ★★ <b>これが無いと台風に見えない。</b>（2026-08-22、オフラインで描いて
        ///   気づいた）塊を角度について一様に置くと、質感がどれだけ良くても
        ///   <b>ただの丸い塊</b>にしかならない。実際の台風は、目のまわりの壁雲と、
        ///   そこから外へ巻き出す数本の腕でできている。
        /// </summary>
        public const int ArmCount = 4;

        /// <summary>
        /// 腕の巻きの強さ。対数らせん <c>θ = θ0 + Tightness × ln(r)</c> の係数で、
        /// 大きいほどきつく巻く。
        /// </summary>
        public const float SpiralTightness = 2.6f;

        /// <summary>腕からの散らばり（ラジアン）。**0 にすると細い線になる。**</summary>
        public const float ArmScatterRadians = 0.22f;

        /// <summary>腕に属さず、腕の隙間を埋める塊の割合。</summary>
        public const float StrayShare = 0.08f;

        /// <summary>雲の厚み（外周半径に対する比）。</summary>
        public const float ThicknessFraction = 0.16f;

        /// <summary>壁雲がほかより高く盛り上がる量（厚みに対する比）。</summary>
        public const float EyewallLiftFraction = 0.85f;

        /// <summary>塊 1 個の半径（外周半径に対する比）。</summary>
        public const float ParcelRadiusFraction = 0.075f;

        /// <summary>塊ごとの大きさのばらつき（上の比に対する割合）。</summary>
        public const float ParcelRadiusSpread = 0.6f;

        /// <summary>乱れを重ねる回数。</summary>
        public const int TurbulenceOctaves = 3;

        /// <summary>乱れの大きさ（塊の半径に対する比）。</summary>
        public const float TurbulenceRatio = 0.8f;

        /// <summary>いちばんゆっくりした渦の周期（秒）。</summary>
        public const float TurbulenceBaseSeconds = 23f;

        /// <summary>生まれてから濃くなりきるまでの一生に対する割合。</summary>
        public const float FadeInFraction = 0.08f;

        /// <summary>薄くなりはじめる一生に対する割合。</summary>
        public const float FadeOutFraction = 0.72f;

        /// <summary>いちばん濃いときの不透明度。</summary>
        public const float PeakAlpha = 0.9f;

        /// <summary>
        /// 塊 1 個ぶんの状態。<paramref name="index"/> は <c>[0, <see cref="Count"/>)</c>。
        /// </summary>
        /// <param name="timeSeconds">連続で増える秒数。</param>
        /// <param name="radiusMetres">渦の外周半径（m）。</param>
        /// <param name="spinRadians">渦全体の回転角（呼び出し側が積む）。</param>
        /// <param name="seed">この台風の種。</param>
        public static TyphoonParcel At(int index, float timeSeconds, float radiusMetres,
                                       float spinRadians, uint seed)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;

            float radius = IsBad(radiusMetres) || radiusMetres <= 0f ? 1000f : radiusMetres;
            float t = IsBad(timeSeconds) ? 0f : timeSeconds;
            float spin = IsBad(spinRadians) ? 0f : spinRadians;

            uint draw = (uint)index * 13u + 5u;

            // ★★ 位相をずらす（噴煙と同じ。これがカオスの入口である）。
            float phase = DeterministicRandom.Unit(seed, draw);
            float age = Frac(t / LifeSeconds + phase) * LifeSeconds;
            float w = age / LifeSeconds;

            // ── 生まれた半径。外側ほど多くの塊が要る（面積が広い）ので sqrt ──
            float birth = EyewallFraction
                          + (1f - EyewallFraction)
                            * (float)Math.Sqrt(DeterministicRandom.Unit(seed, draw + 1u));

            // ── らせん: 一生かけて内へ寄る。**目までは入らない。** ──────────
            float fraction = birth - InflowFraction * w;
            if (fraction < EyeFraction) fraction = EyeFraction;

            // ── 塊そのものの大きさ（腕の配置より先に要る）──────────────
            float sizePick = DeterministicRandom.Unit(seed, draw + 4u);
            float parcel = radius * ParcelRadiusFraction
                           * (1f - ParcelRadiusSpread * 0.5f + ParcelRadiusSpread * sizePick);

            // ★★ **塊の内側の縁で目を守る。** 中心までの距離で判定すると、
            //    壁雲に置いた塊が目の中まではみ出す（それが「目が埋まる」の正体）。
            float minDistance = EyeFraction * radius + parcel;
            float distance = fraction * radius;
            if (distance < minDistance) distance = minDistance;
            fraction = radius > 0f ? distance / radius : fraction;

            // ── ★★ らせん状の雨雲帯 ────────────────────────────────
            //
            //    対数らせん θ = θ0 + Tightness × ln(r)。腕の何本かに割り当てて、
            //    そこから少しだけ散らす。**一様に置くとただの丸い塊になる。**
            float armPick = DeterministicRandom.Unit(seed, draw + 9u);
            float scatter = DeterministicRandom.Unit(seed, draw + 10u) * 2f - 1f;

            float baseAngle;
            if (armPick < StrayShare)
            {
                // 腕の隙間を埋める分。**全部を腕に載せると輪郭が硬くなる。**
                baseAngle = DeterministicRandom.Unit(seed, draw + 2u) * 6.2831853f;
            }
            else
            {
                int arm = (int)(DeterministicRandom.Unit(seed, draw + 11u) * ArmCount);
                if (arm >= ArmCount) arm = ArmCount - 1;

                baseAngle = arm * (6.2831853f / ArmCount)
                            + SpiralTightness * (float)Math.Log(fraction > 1e-3f
                                                                ? fraction : 1e-3f)
                            + scatter * ArmScatterRadians;
            }

            // ★★ **渦の回転を年齢から足さないこと。**（2026-08-22、オフラインで
            //    描いて気づいた）ここに <c>SwirlRadians × w × 内側ほど速い係数</c> を
            //    足していたとき、1 個ごとに最大 8 ラジアンも余分に回るので、
            //    <b>腕がきれいに塗り潰されてただの環になった</b>。
            //
            //    対数らせんでは<b>回転は内へ寄ること自体から出る</b> ——
            //    <c>θ = θ0 + Tightness × ln(r)</c> なので、r が小さくなれば
            //    θ も動く。塊は腕という「模様」の上を流れていくのであって、
            //    模様ごと勝手に回るのではない。
            float angle = baseAngle + spin;
            float x = (float)Math.Cos(angle) * distance;
            float z = (float)Math.Sin(angle) * distance;

            // ── 高さ。壁雲がいちばん高く、外周へ向かって薄く低くなる ────────
            float thickness = ThicknessFraction * radius;
            float wallness = Bell(fraction, EyewallFraction, EyewallFraction * 1.6f);
            float band = DeterministicRandom.Unit(seed, draw + 3u);
            float y = band * thickness * (0.35f + 0.65f * (1f - fraction))
                      + wallness * EyewallLiftFraction * thickness;

            // ── ★★ 乱れ。**ここが「一枚の板」と「雲」を分ける。** ────────
            float scale = parcel * TurbulenceRatio;
            for (int o = 0; o < TurbulenceOctaves; o++)
            {
                float period = TurbulenceBaseSeconds / (1 << o);
                float amp = scale / (1 << o);
                uint os = seed + (uint)(o * 6373);

                float px = DeterministicRandom.Unit(os, draw + 5u) * 6.2831853f;
                float py = DeterministicRandom.Unit(os, draw + 6u) * 6.2831853f;
                float pz = DeterministicRandom.Unit(os, draw + 7u) * 6.2831853f;

                float k = 6.2831853f / period;
                x += (float)Math.Sin(k * age + px) * amp;
                y += (float)Math.Sin(k * age + py) * amp * 0.45f;
                z += (float)Math.Sin(k * age + pz) * amp;
            }

            // ★★ **目を守るのは乱れの「あと」である。**（2026-08-22、テストが拾った）
            //    先に守っても、そのあとの乱れが塊を目の中へ押し戻す
            //    （半径 840 m の目に 683 m まで入っていた）。
            //    ここで外向きに押し出す ——**中心へ寄せるのではなく外へ**。
            float finalDistance = (float)Math.Sqrt(x * x + z * z);
            float keepOut = EyeFraction * radius + parcel;
            if (finalDistance < keepOut)
            {
                if (finalDistance > 1e-3f)
                {
                    float push = keepOut / finalDistance;
                    x *= push;
                    z *= push;
                }
                else
                {
                    // ちょうど中心。向きが無いので生まれた角度へ出す。
                    x = (float)Math.Cos(angle) * keepOut;
                    z = (float)Math.Sin(angle) * keepOut;
                }
            }

            // ── 濃さ ─────────────────────────────────────────
            float alpha;
            if (w < FadeInFraction) alpha = w / FadeInFraction;
            else if (w > FadeOutFraction) alpha = (1f - w) / (1f - FadeOutFraction);
            else alpha = 1f;

            // ★ 壁雲はいちばん濃く、外周の腕は薄い。
            alpha *= PeakAlpha * (0.55f + 0.45f * (1f - fraction) + 0.3f * wallness);
            if (alpha > 1f) alpha = 1f;

            // ── 明るさ。上ほど日を受けて白い ─────────────────────────
            float brightness = Clamp01(y / (thickness * 1.4f));

            float rotation = DeterministicRandom.Unit(seed, draw + 8u) * 360f
                             + angle * 57.29578f;

            return new TyphoonParcel(x, y, z, parcel, Clamp01(alpha), brightness, rotation);
        }

        /// <summary>
        /// <paramref name="at"/> が <paramref name="centre"/> にどれだけ近いか
        /// <c>[0,1]</c>。<paramref name="width"/> で 0 になる釣鐘。
        /// </summary>
        private static float Bell(float at, float centre, float width)
        {
            if (width <= 0f) return 0f;
            float d = at - centre;
            if (d < 0f) d = -d;
            if (d >= width) return 0f;

            float k = d / width;
            return 0.5f * (1f + (float)Math.Cos(Math.PI * k));
        }

        private static float Frac(float v)
        {
            if (IsBad(v)) return 0f;
            float f = v - (float)Math.Floor(v);
            return f < 0f ? f + 1f : f;
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
