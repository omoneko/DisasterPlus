using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 断層に沿った 4 個の破壊円盤が**落ちうる範囲**の幾何。
    ///
    /// 典拠は IL 事実文書 §A-3。毎ステップ 4 回、断層上の正規化位置
    /// t ∈ [-0.4, 0.4] を引き直し、そこに幅 w = W(1 - 4t²) の円盤を置いて
    /// probability = 1 で建物を壊す。
    ///
    /// ── 円盤 1 個の到達距離は w である（2w ではない）─────────────────────
    ///
    /// この帯は以前 destructionRadiusMax = 2w を到達距離として組まれていたが、
    /// **それは 33% 広すぎた。** 呼び出しの実引数は
    /// <c>preRadius: w</c>（IL_0270–0298: ldloc.s 17 が w、arg3 に w がそのまま入る）で、
    /// <c>DisasterHelpers.DestroyBuildings</c> は
    /// <c>if (dist &gt;= preRadius) continue;</c>（IL_0130 の <c>bge.un</c>）を
    /// **種の生成よりもランプの計算よりも前**に置いている。
    /// 2w が効くのは fD の分子だけで、<c>dist &lt; w</c> の範囲では fD &gt; 1、
    /// そこへ probability = 1 が掛かるので比較は無条件に真になる。
    /// **つまり円盤の到達距離は w ちょうどで、2w はどこにも現れない。**
    ///
    /// **これは「当たる場所」ではなく「当たりうる場所」である。**
    /// 位置は毎ステップ振り直されるので、帯の中にいても当たらないことがある。
    /// 呼び出し側は必ずその旨を併記すること（Strings.EarthquakeFaultBandNote）。
    /// 逆に、全体円盤について「倒れません」と断定してよいのは
    /// **この帯の外側の建物だけ**である（Task 5 の BuildingMargin が使う）。
    ///
    /// **全体円盤（SeismicIntensity）と混ぜないこと。** あちらは
    /// probability = 0.02 の線形ランプで、建物ごとに固定されたしきい値と比べる
    /// 決定論的な判定である。こちらは probability = 1、つまり帯に入った建物は
    /// しきい値に関係なく壊れる。**2 つは別のモデル**なので、断層帯を
    /// 「s が高い状態」として表示してはいけない（設計書 §3.1 の最終段落）。
    ///
    /// 蛇行（<c>p.x += dir.y * (s*w*0.5)</c> / <c>p.y -= dir.x * (s*w*0.5)</c>、
    /// IL_0201–023E）は円盤の中心を断層線から**直交方向へ最大 0.5w** ずらす
    /// （s = sin(...) ∈ [-1, 1]）。到達距離 w と合わせて
    /// **直交方向の包絡線は 1.5w** になる。以前の doc は「2w の内側に収まるので
    /// 算入しない」としていたが、その 2w 自体が誤りだったので、蛇行は算入する。
    ///
    /// 沿走方向も同じ理屈で、円盤中心の上限 0.4·L に到達距離 w が足される
    /// （<see cref="Contains"/> はこれを円との交差として厳密に扱う）。
    ///
    /// Length / Width が 0（＝プレハブ 4 値が読めなかった）ときは
    /// <see cref="Known"/> が false になり、Contains は常に false を返す。
    /// 「分からない」を「外側」と言い換えないため、呼び出し側は Known を必ず見る。
    /// </summary>
    public struct FaultBand
    {
        /// <summary>円盤中心が落ちる正規化位置の上限（IL: Randomizer.Int32(-400, 400) * 0.001）。</summary>
        public const float MaxOffset = 0.4f;

        public readonly Vec2 Centre;

        /// <summary>断層の走向。IL: new Vector2(-sin(m_angle), cos(m_angle))。</summary>
        public readonly Vec2 Direction;

        /// <summary>L = m_crackLength * (0.5 + intensity * 0.005)。呼び出し側が計算済みの値を渡す。</summary>
        public readonly float Length;

        /// <summary>W = m_crackWidth * (0.5 + intensity * 0.005)。同上。</summary>
        public readonly float Width;

        public FaultBand(Vec2 centre, float angleRadians, float length, float width)
        {
            Centre = centre;
            double a = angleRadians;
            Direction = new Vec2(-(float)System.Math.Sin(a), (float)System.Math.Cos(a));
            Length = length > 0f && !float.IsNaN(length) ? length : 0f;
            Width = width > 0f && !float.IsNaN(width) ? width : 0f;
        }

        /// <summary>幾何が確定しているか。false なら内外を判定してはいけない。</summary>
        public bool Known { get { return Length > 0f && Width > 0f; } }

        public Vec2 EndA { get { return Centre + Direction * (-Length * 0.5f); } }
        public Vec2 EndB { get { return Centre + Direction * (Length * 0.5f); } }

        /// <summary>
        /// 正規化位置 t に落ちた円盤 1 個の**幅** w = W(1 - 4t²)（IL_01D6–01EA）。
        /// これが <c>preRadius</c> であり、そのまま到達距離になる。
        /// </summary>
        public float PatchRadiusAt(float t)
        {
            if (!Known || float.IsNaN(t)) return 0f;
            float taper = 1f - 4f * t * t;
            if (taper <= 0f) return 0f;
            return Width * taper;
        }

        /// <summary>
        /// 正規化位置 t における、断層線からの直交方向の到達距離
        /// ＝ **到達距離 w ＋ 蛇行 0.5w = 1.5w**（クラス doc の導出）。
        /// </summary>
        public float HalfWidthAt(float t)
        {
            return 1.5f * PatchRadiusAt(t);
        }

        /// <summary>
        /// p が破壊円盤の落ちうる範囲の内側か。**「当たる」ではない。**
        ///
        /// 円盤中心は (t·L, s·w·0.5)（t ∈ [-0.4, 0.4]、s ∈ [-1, 1]）に落ち、
        /// そこから半径 w まで届く。p にいちばん近づける中心は
        /// **t を [-0.4, 0.4] にクランプし、s を p 側へ振り切った**ものなので、
        /// 沿走方向の余り <c>da</c> と直交方向の余り <c>max(0, across - 0.5w)</c> の
        /// 二乗和を w² と比べればよい。
        ///
        /// da = 0（＝両端より内側）なら条件は across ≤ 1.5w に退化し、
        /// <see cref="HalfWidthAt"/> と厳密に一致する。
        ///
        /// w は t によって細るので、厳密には「隣の t のもっと太い円盤が届く」
        /// 可能性が残る。ただしそれが起きるのは w が L と同程度に大きいときだけで、
        /// バニラの断層は L ≫ W（診断ダンプの `fault (L/W)` 行で確認できる）。
        /// **細い側に倒す近似**なので、帯の外と断定する範囲が広がることはない。
        /// </summary>
        public bool Contains(Vec2 p)
        {
            if (!Known) return false;

            float dx = p.X - Centre.X;
            float dz = p.Z - Centre.Z;

            // 断層に沿った成分と、それに直交する成分に分解する。
            float along = dx * Direction.X + dz * Direction.Z;
            float across = dx * Direction.Z - dz * Direction.X;
            if (across < 0f) across = -across;

            // 円盤中心が置ける範囲へクランプした t。両端の外側では
            // 「端の円盤から見て、まだ w ぶん届くか」を見ることになる。
            float t = along / Length;
            if (t < -MaxOffset) t = -MaxOffset;
            else if (t > MaxOffset) t = MaxOffset;

            float w = PatchRadiusAt(t);
            if (w <= 0f) return false;

            float da = along - t * Length;
            if (da < 0f) da = -da;

            // 蛇行は円盤中心を直交方向へ最大 0.5w ずらせる。使い切れる分だけ引く。
            float dacross = across - 0.5f * w;
            if (dacross < 0f) dacross = 0f;

            return da * da + dacross * dacross <= w * w;
        }
    }
}
