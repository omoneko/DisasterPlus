using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>火口から放り出された岩塊 1 個の弾道。**純粋な値**（Unity も乱数も出てこない）。</summary>
    public struct EjectaBlock
    {
        /// <summary>この枠が使えるか。false なら**何も描かない**（0 で代用しない）。</summary>
        public readonly bool Valid;

        /// <summary>水平の向き（単位ベクトル）。</summary>
        public readonly float DirX;

        public readonly float DirZ;

        /// <summary>水平の初速（m/秒）。</summary>
        public readonly float HorizontalSpeed;

        /// <summary>鉛直の初速（m/秒、上が正）。</summary>
        public readonly float VerticalSpeed;

        /// <summary>
        /// 岩塊の大きさ <c>[0,1]</c>。**大きいほど遠くへ飛ぶ**（所有者の依頼どおり）。
        /// 描画側は粒の量と着弾の煙の広さにこれを掛ける。
        /// </summary>
        public readonly float SizeUnit;

        /// <summary>着弾までの秒数。<see cref="EjectaBallistics.Plan"/> が山肌と交差させて出す。</summary>
        public readonly float FlightSeconds;

        /// <summary>着弾点の火口からの水平距離（m）。</summary>
        public readonly float RangeMetres;

        public EjectaBlock(float dirX, float dirZ, float horizontalSpeed, float verticalSpeed,
                           float sizeUnit, float flightSeconds, float rangeMetres)
        {
            Valid = true;
            DirX = dirX;
            DirZ = dirZ;
            HorizontalSpeed = horizontalSpeed;
            VerticalSpeed = verticalSpeed;
            SizeUnit = sizeUnit;
            FlightSeconds = flightSeconds;
            RangeMetres = rangeMetres;
        }
    }

    /// <summary>
    /// **噴石（ballistic blocks）の弾道。** エンジン非依存の純関数だけで、
    /// Unity の型もゲームの型も <c>System.Random</c> も出てこない。
    ///
    /// ── なぜ Core に置くのか ─────────────────────────────────
    ///
    /// 2026-08-22 の所有者の依頼:
    ///
    /// > 噴火の際に爆発＋噴石のアニメーションも実装してほしいです。
    ///
    /// 今までの「噴石」は火口の上で粒子を湧かせ続けるだけで、
    /// **1 個 1 個の岩が飛んで落ちてはいなかった**。飛ばすとなると
    /// 「どこへ何秒で落ちるか」を決める必要があり、それは**ただの放物線**である ——
    /// つまり実機を起動しないと 1 行も確かめられない理由が無い。ここに集めてテストで固定する。
    ///
    /// ── 何が本物で、何が⑤の演出値か ─────────────────────────────
    ///
    ///   - <see cref="GravityMetresPerSecondSquared"/> = 9.81 は**実在の物理**である
    ///   - 放出角 <see cref="MinElevationDegrees"/>〜<see cref="MaxElevationDegrees"/>
    ///     （55〜80°）も実際の噴石の観測に近い（火口から急角度で出る）
    ///   - **飛距離は初速からではなく、先に決める。** 山の半径に対する比
    ///     （<see cref="MinRangeFraction"/>〜<see cref="MaxRangeFraction"/>）で決めてから
    ///     <c>v = sqrt(range·g / sin 2φ)</c> で初速を逆算する。
    ///     こうしないと**山の大きさに追従しない** —— 半径 350 m の溶岩ドームから
    ///     2.7 km 岩が飛ぶことになる。⑤はプレイヤーが大きさを選べる機能なので、
    ///     そこは物理より先に画面の要求が来る。実測（成層、R = 1200 m）で
    ///     **着弾 255〜1642 m（平均 876 m）、2 割が裾の外、最長の滞空 27 秒**である
    ///   - 空気抵抗は入れない。入れると「大きいほど遠い」を作るのに終端速度が要り、
    ///     数が 3 つ増えて**どれも実測できない**。大小の差は飛距離の比で直接つける
    ///
    /// ── 着弾はどこか ────────────────────────────────────────
    ///
    /// **火口を含んだ山肌**（<c>VolcanoCrater</c> の天井で切った円錐。起伏を掛ける前）と
    /// 放物線の交点である。
    ///
    /// ★★ <b>火口を含めるのを忘れないこと。</b> <c>VolcanoShape.ProfileAt</c> だけを
    ///   地面にすると <c>d = 0</c> で山頂の高さ H が返るのに、噴出口は**火口の底**に在る ——
    ///   打ち上げた瞬間に「地面より下」と判定されて、**噴石が 1 個も飛ばない**
    ///   （実際にそうなった。テストが捕まえた）。
    ///
    /// 起伏（<c>VolcanoRelief</c>）は削る向きにしか働かないので、
    /// **実際の地面はここで使う面と同じか低い** —— つまり噴石は谷の中では
    /// 数 m 手前・数 m 上で止まる。着弾の土煙は 1 秒足らずで消えるので、
    /// 起伏を評価し直す価値は無い（<c>VolcanoRelief.ProfileAt</c> は 1 回 300 flop で、
    /// ここは 1 個あたり 170 回評価する）。
    ///
    /// 半径の外では円錐は 0 なので、**元の地形の高さ**（＝火山を置いた地点の地面）で
    /// 止まる。起伏のある土地では実際の地面と数 m ずれるが、そこもやはり
    /// 1 秒足らずの土煙である。
    ///
    /// ── 乱数 ────────────────────────────────────────────
    ///
    /// <see cref="DeterministicRandom"/> だけ。**フレーム番号を種に混ぜない** ——
    /// 混ぜると同じ岩が毎フレーム抽選し直され、飛んでいる途中で行き先が変わる。
    /// 種は「火山の地点 × 噴出の回数 × 岩の番号」だけから決まる。
    /// </summary>
    public static class EjectaBallistics
    {
        /// <summary>重力加速度（m/秒²）。**ここだけは実在の物理。**</summary>
        public const float GravityMetresPerSecondSquared = 9.81f;

        /// <summary>1 回の噴出で放り出す岩の数の下限（＝いちばん弱いとき）。</summary>
        public const int MinBlocksPerBlast = 5;

        /// <summary>同上の上限。**これが同時に描く粒子系の数の上限でもある。**</summary>
        public const int MaxBlocksPerBlast = 16;

        /// <summary>
        /// いちばん小さい岩の飛距離（山の半径に対する比）。
        /// **ここは「平地に落ちるとしたときの」比である** —— 実際は火口の底から
        /// 出て、それより低い斜面と裾へ落ちるので、着弾はこれより 3〜4 割遠い。
        /// </summary>
        public const float MinRangeFraction = 0.25f;

        /// <summary>いちばん大きい岩の飛距離（同上）。**裾より外まで飛ぶ。**</summary>
        public const float MaxRangeFraction = 0.90f;

        /// <summary>放出角の下限（度、水平から）。</summary>
        public const float MinElevationDegrees = 42f;

        /// <summary>
        /// 放出角の上限（度）。**80° まで上げない。**
        /// 同じ飛距離でも角度が立つほど初速も滞空時間も伸びる。80° を許した版では
        /// 48 秒の弾道が出た（tools/VolcanoPreview の実測）——
        /// 物理としては正しい（実際の噴石も 20〜40 秒かかる）が、画面では
        /// 「岩が止まって見える」時間である。65° で最長 27 秒に収まる。
        /// </summary>
        public const float MaxElevationDegrees = 65f;

        /// <summary>飛距離のばらつき幅（±この割合）。</summary>
        public const float RangeJitter = 0.18f;

        /// <summary>噴出の強さ 0 のときの飛距離の比（強さ 1 で 1.0）。</summary>
        public const float WeakRangeScale = 0.55f;

        /// <summary>弾道を刻む歩幅（秒）。細かくしても着弾点は数 m しか動かない。</summary>
        private const float MarchSeconds = 0.25f;

        /// <summary>
        /// これ以上は飛ばさない（秒）。**無限ループにしない**ための帽子であって、
        /// 演出値ではない —— 上の帯（飛距離 0.9R、角度 65°）でいちばん長い弾道でも
        /// 27 秒なので、**ここに当たる弾道は在ってはならない**
        /// （テストが最長を測って固定している）。
        /// </summary>
        public const float MaxFlightSeconds = 60f;

        /// <summary>二分法の回数。歩幅 0.25 秒を 11 回割ると 0.12 ms。</summary>
        private const int RefineSteps = 11;

        /// <summary>
        /// 1 回の噴出で放り出す岩の数。強いほど多い。
        /// </summary>
        public static int BlocksPerBlast(float unit)
        {
            float u = Clamp01(unit);
            int n = MinBlocksPerBlast
                    + (int)((MaxBlocksPerBlast - MinBlocksPerBlast) * u + 0.5f);
            if (n < MinBlocksPerBlast) return MinBlocksPerBlast;
            if (n > MaxBlocksPerBlast) return MaxBlocksPerBlast;
            return n;
        }

        /// <summary>
        /// 岩 1 個の弾道を決める。**同じ (種, 噴出番号, 岩番号) なら必ず同じ弾道。**
        ///
        /// <paramref name="ventAboveBaseMetres"/> は噴出口が「山を置いた地点の地面」から
        /// 何 m 上かである（＝火口の底の高さ）。0 以下や NaN は 0 に落とす。
        ///
        /// 山の半径・最終高が読めていない（0 以下）ときは
        /// <c>Valid == false</c> を返す —— **0 で代用しない。**
        /// </summary>
        public static EjectaBlock Plan(uint seed, int blastIndex, int blockIndex, float unit,
                                       VolcanoForm form, float radiusMetres, float heightMetres,
                                       float ventAboveBaseMetres)
        {
            if (IsBad(radiusMetres) || radiusMetres <= 0f) return default(EjectaBlock);
            if (IsBad(heightMetres) || heightMetres <= 0f) return default(EjectaBlock);
            if (blastIndex < 0 || blockIndex < 0) return default(EjectaBlock);

            float vent = IsBad(ventAboveBaseMetres) || ventAboveBaseMetres < 0f
                       ? 0f : ventAboveBaseMetres;

            uint key = unchecked((uint)(blastIndex * 61u + 1u) * 0x9E3779B1u
                                 + (uint)blockIndex);

            float azimuth = 6.2831853f * DeterministicRandom.Unit(seed, key);
            float sizeUnit = DeterministicRandom.Unit(seed, key ^ 0x51ED270Bu);
            float angleUnit = DeterministicRandom.Unit(seed, key ^ 0x2545F491u);
            float jitterUnit = DeterministicRandom.Unit(seed, key ^ 0x1B873593u);

            float u = Clamp01(unit);

            // ★ 大きいほど遠い（所有者の依頼）。強さは全体を縮める向きにだけ効く。
            float fraction = MinRangeFraction
                             + (MaxRangeFraction - MinRangeFraction) * sizeUnit;
            fraction *= 1f + RangeJitter * (jitterUnit * 2f - 1f);
            fraction *= WeakRangeScale + (1f - WeakRangeScale) * u;

            float range = fraction * radiusMetres;
            if (!(range > 0f)) return default(EjectaBlock);

            float elevation = (MinElevationDegrees
                               + (MaxElevationDegrees - MinElevationDegrees) * angleUnit)
                              * 0.0174532925f;

            float sin2 = (float)Math.Sin(2.0 * elevation);
            if (!(sin2 > 0.02f)) sin2 = 0.02f;

            // 平地に落ちるとしたときの初速。実際は火口の底から出て斜面に落ちるので、
            // 下の March がそれより手前（斜面）でも先（裾の外）でも正しく止める。
            float speed = (float)Math.Sqrt(range * GravityMetresPerSecondSquared / sin2);

            float horizontal = speed * (float)Math.Cos(elevation);
            float vertical = speed * (float)Math.Sin(elevation);

            float dirX = (float)Math.Cos(azimuth);
            float dirZ = (float)Math.Sin(azimuth);

            float flight = March(form, radiusMetres, heightMetres, vent, horizontal, vertical);
            if (!(flight > 0f)) return default(EjectaBlock);

            return new EjectaBlock(dirX, dirZ, horizontal, vertical, sizeUnit,
                                   flight, horizontal * flight);
        }

        /// <summary>
        /// 噴出口から見た岩の位置（m）。<paramref name="t"/> は打ち上げからの秒数。
        /// **<c>t</c> が飛行時間を超えていても計算はする**（呼び出し側が
        /// <see cref="EjectaBlock.FlightSeconds"/> で切ること）。
        /// </summary>
        public static void OffsetAt(EjectaBlock block, float t,
                                    out float dx, out float dy, out float dz)
        {
            dx = 0f; dy = 0f; dz = 0f;
            if (!block.Valid) return;
            if (IsBad(t) || t < 0f) return;

            dx = block.DirX * block.HorizontalSpeed * t;
            dz = block.DirZ * block.HorizontalSpeed * t;
            dy = block.VerticalSpeed * t
                 - 0.5f * GravityMetresPerSecondSquared * t * t;
        }

        /// <summary>
        /// 放物線と山肌（**起伏を掛ける前の円錐**）の交点までの秒数。
        /// 見つからなければ <see cref="MaxFlightSeconds"/> で切る。
        /// </summary>
        private static float March(VolcanoForm form, float radiusMetres, float heightMetres,
                                   float ventAboveBase, float horizontal, float vertical)
        {
            float previous = 0f;
            for (float t = MarchSeconds; t <= MaxFlightSeconds; t += MarchSeconds)
            {
                if (Below(form, radiusMetres, heightMetres, ventAboveBase,
                          horizontal, vertical, t))
                {
                    float lo = previous;
                    float hi = t;
                    for (int i = 0; i < RefineSteps; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Below(form, radiusMetres, heightMetres, ventAboveBase,
                                  horizontal, vertical, mid)) hi = mid;
                        else lo = mid;
                    }
                    return hi;
                }
                previous = t;
            }
            return MaxFlightSeconds;
        }

        /// <summary>時刻 <paramref name="t"/> で岩が地面より下にいるか。</summary>
        private static bool Below(VolcanoForm form, float radiusMetres, float heightMetres,
                                  float ventAboveBase, float horizontal, float vertical, float t)
        {
            float altitude = ventAboveBase + vertical * t
                             - 0.5f * GravityMetresPerSecondSquared * t * t;
            float distance = horizontal * t;
            return altitude <= GroundAt(form, distance, radiusMetres, heightMetres);
        }

        /// <summary>
        /// 中心から <paramref name="distanceMetres"/> の地面（**火口を含む**、起伏なし）。
        /// <c>VolcanoCrater.ProfileAt</c> と同じ 2 つの式の小さいほうで、
        /// あちらと違って <c>VolcanoRelief</c> を要らない（起伏は削る向きだけなので、
        /// これは必ず実際の地面以上である）。
        /// </summary>
        public static float GroundAt(VolcanoForm form, float distanceMetres,
                                     float radiusMetres, float heightMetres)
        {
            float cone = VolcanoShape.ProfileAt(
                form, distanceMetres, radiusMetres,
                heightMetres * VolcanoCrater.SummitScale(form, radiusMetres));
            float ceiling = VolcanoCrater.CeilingMetres(distanceMetres, radiusMetres,
                                                        heightMetres);
            return cone < ceiling ? cone : ceiling;
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
