using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 断層に沿った 4 個の破壊円盤が**落ちうる範囲**の幾何。
    ///
    /// 典拠は IL 事実文書 §A-3。毎ステップ 4 回、断層上の正規化位置
    /// t ∈ [-0.4, 0.4] を引き直し、そこに幅 w = W(1 - 4t²) の円盤を置いて
    /// probability = 1、destructionRadiusMax = 2w で建物を壊す。
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
    /// 蛇行（sin(t*freq + ph) * w * 0.5）は帯の幅 2w の内側に収まるので算入しない。
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
        /// 正規化位置 t における、断層線からの直交方向の到達距離。
        /// 円盤の幅は w = W(1 - 4t²)、破壊は destructionRadiusMax = 2w まで届く。
        /// </summary>
        public float HalfWidthAt(float t)
        {
            if (!Known) return 0f;
            float taper = 1f - 4f * t * t;
            if (taper <= 0f) return 0f;
            return 2f * Width * taper;
        }

        /// <summary>p が破壊円盤の落ちうる範囲の内側か。**「当たる」ではない。**</summary>
        public bool Contains(Vec2 p)
        {
            if (!Known) return false;

            float dx = p.X - Centre.X;
            float dz = p.Z - Centre.Z;

            // 断層に沿った成分と、それに直交する成分に分解する。
            float along = dx * Direction.X + dz * Direction.Z;
            float across = dx * Direction.Z - dz * Direction.X;
            if (across < 0f) across = -across;

            float t = along / Length;
            if (t < -MaxOffset || t > MaxOffset) return false;

            return across <= HalfWidthAt(t);
        }
    }
}
