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
    /// 沿走方向も同じ理屈で、円盤中心の上限 0.4·L に到達距離 w が足される。
    ///
    /// ── 到達範囲は「1 つの t」では決まらない ──────────────────────────
    ///
    /// <see cref="Contains"/> は最初、点にいちばん近い**ただ 1 つ**の t
    /// （＝ along/L をクランプしたもの）で円との交差を見ていた。**これは誤りである。**
    /// w は t とともに細るので、沿走方向の残差を最小にする t が到達距離を最大にする t
    /// とは限らない。**もっと中央寄りの、より太い円盤が、沿走方向に少し離れていても
    /// 届く**ことがある。
    ///
    /// 反例（L = 1000、W = 100、点は 沿走 300 / 直交 99）:
    ///   t = 0.30 … w = 64.00、残差 0、直交の余り 99 - 32.00 = 67.00 → 67.00² &gt; 64.00² で外
    ///   t = 0.28 … w = 68.64、残差 20、直交の余り 99 - 34.32 = 64.68
    ///              → 20² + 64.68² = 4583 &lt; 68.64² = 4712 で**内側**
    /// L : W = 10 : 1 はバニラの断層の代表的な比なので、これは稀な縁の話ではない。
    /// 誤りの向きも最悪で、**帯の内側の建物を外側と言い**、その建物について
    /// 全体円盤の「倒壊しません」を名乗ることになる（probability = 1 の円盤が
    /// 別に判定しているのに、である）。
    ///
    /// したがって <see cref="Contains"/> は t の**全域**を探す。詳細はそちらの doc。
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
        /// 円盤中心の沿走位置を走査する分割数。
        ///
        /// 探しているのは <c>u ∈ [-0.4L, 0.4L]</c> における <see cref="Gap"/> の最小値だが、
        /// <c>w(u)</c> が 2 次なので Gap は 4 次の区分多項式であり、**単峰である保証が無い**
        /// （三分探索も黄金分割も使えない）。一様走査で谷を掴んでから
        /// <see cref="RefineSteps"/> で詰める。
        ///
        /// 刻みは <c>0.8L / 96</c>。L : W = 10 : 1 のとき成立する u の区間はおよそ 2w ≒ 0.2L
        /// 幅あるので、刻み <c>0.0083L</c> は 20 倍以上細かい。判定がぶれうるのは
        /// 真の境界からこの刻みの半分ぶん以内だけで、そこは細分が拾う。
        /// </summary>
        private const int ScanSteps = 96;

        /// <summary>走査で掴んだ谷を半分ずつ詰める回数。0.8L/96 が 2^-24 倍まで縮む。</summary>
        private const int RefineSteps = 24;

        /// <summary>
        /// p が破壊円盤の落ちうる範囲の内側か。**「当たる」ではない。**
        ///
        /// 円盤中心は <c>(u, s·w(u)·0.5)</c>（<c>u = t·L ∈ [-0.4L, 0.4L]</c>、
        /// <c>s ∈ [-1, 1]</c>）に落ち、そこから半径 <c>w(u)</c> まで届く。
        /// s は連続なので、ある u に対する到達範囲は
        /// **長さ <c>w(u)</c> の縦線分から距離 <c>w(u)</c> 以内**（＝スタジアム形）である。
        /// 求める答えは、それを u について**全部合わせた**集合に p が入るか。
        ///
        /// <c>u = along</c> の 1 点だけを見るのでは足りない —— クラス doc の反例のとおり、
        /// **もっと中央寄りの太い円盤が届く**ことがある。1 点だけを見ていた版は
        /// 帯の内側の建物を外側と誤判定し、その建物について「倒壊しません」を
        /// 名乗っていた。
        ///
        /// <c>u = along</c> が置ける（<c>|along| ≤ 0.4L</c>）ときは沿走方向の残差が 0 になり、
        /// 条件は <c>across ≤ 1.5·w</c> に退化して <see cref="HalfWidthAt"/> と厳密に一致する。
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
            if (float.IsNaN(along) || float.IsNaN(across)) return false;

            float half = MaxOffset * Length;   // 円盤中心が置ける沿走方向の上限

            // 早い棄却。どの円盤も W より太くならないので、この 2 つを外れていれば
            // 走査するまでもなく外側である（カーソルは普通ここで落ちる）。
            if (across > 1.5f * Width) return false;
            if (along > half + Width || along < -(half + Width)) return false;

            float step = 2f * half / ScanSteps;
            float bestU = -half;
            float bestGap = Gap(-half, along, across);
            if (bestGap <= 0f) return true;

            for (int i = 1; i <= ScanSteps; i++)
            {
                float u = -half + step * i;
                float gap = Gap(u, along, across);
                if (gap <= 0f) return true;
                if (gap < bestGap) { bestGap = gap; bestU = u; }
            }

            // 走査の谷間に最小が落ちている場合を拾う。左右を半分ずつ詰める。
            float h = step * 0.5f;
            for (int i = 0; i < RefineSteps; i++)
            {
                float lo = bestU - h;
                if (lo < -half) lo = -half;
                float hi = bestU + h;
                if (hi > half) hi = half;

                float gapLo = Gap(lo, along, across);
                if (gapLo <= 0f) return true;
                float gapHi = Gap(hi, along, across);
                if (gapHi <= 0f) return true;

                if (gapLo < bestGap) { bestGap = gapLo; bestU = lo; }
                if (gapHi < bestGap) { bestGap = gapHi; bestU = hi; }
                h *= 0.5f;
            }

            return false;
        }

        /// <summary>
        /// 沿走位置 <paramref name="u"/> に落ちた円盤の到達範囲から見た、点の**はみ出し**。
        /// 0 以下なら、その u の円盤（蛇行を最大限使った位置）が点に届く。
        ///
        ///   Gap(u) = (along - u)² + max(0, across - 0.5·w(u))² - w(u)²
        ///
        /// 第 2 項の <c>max</c> が、蛇行で中心を点の側へ 0.5w まで寄せられることを表す。
        /// 点が線分の真横にある（<c>across ≤ 0.5w</c>）ときは 0 になり、条件は
        /// <c>|along - u| ≤ w</c> に退化する。
        /// </summary>
        private float Gap(float u, float along, float across)
        {
            float ratio = u / Length;
            float w = Width * (1f - 4f * ratio * ratio);

            // ここには円盤が落ちない（|t| ≧ 0.5 で幅が 0 に細る）。
            if (w <= 0f) return float.MaxValue;

            float da = along - u;

            float dacross = across - 0.5f * w;
            if (dacross < 0f) dacross = 0f;

            return da * da + dacross * dacross - w * w;
        }
    }
}
