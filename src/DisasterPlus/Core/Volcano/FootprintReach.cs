using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 「その道路は影響範囲の円に掛かっているか」の幾何。**エンジン非依存。**
    ///
    /// ── なぜ中点 1 点では足りないのか（全体レビュー I4）─────────────────
    ///
    /// ⑤は当初、道路を <c>NetSegment.m_middlePosition</c> の 1 点だけで判定していた。
    /// その結果、**中点が円の外にあり、しかも円の中心へ向かって伸びている幹線道路が
    /// 1 本も取り除かれなかった。** 取り除かれない道路は <c>m_flags &amp; 3 == 1</c> の
    /// ままなので <c>NetSegment.TerrainUpdated</c> が <c>Heights.PrimaryLevel</c> を
    /// 掛け続け、**その道路の y に沿って山の中に平らな溝が残る**（IL 事実文書 §A-2 / §G-16 (c)）。
    /// 隆起の途中の前線ならその後の走査が拾い直すが、**いちばん外側の帯は二度と
    /// 走査されない** —— 設計書 §1.2 が発見した失敗そのものである。
    ///
    /// ── 代表点は 3 点、判定は折れ線と円の距離 ────────────────────────
    ///
    /// 道路 1 本について取れる位置は 3 つある（§F-15 で IL 実測済み）:
    /// 両端ノードの位置（<c>m_startNode</c> / <c>m_endNode</c> の <c>m_position</c>）と
    /// <c>m_middlePosition</c>（2 本のベジェの中点の平均）。
    /// この 3 点を結ぶ折れ線と円の中心との距離が半径以下なら、その道路は範囲に掛かる。
    ///
    /// **3 点の「点」だけを見るのでは足りない**（円を貫くだけの長い直線道路は
    /// 3 点とも外側になりうる）。だから折れ線の**線分**との距離を測る。
    /// 逆に <c>m_bounds</c>（AABB）で判定すると、斜めに走る道路で角の分だけ
    /// 広く当たる —— **壊す側と数える側が同じ述語を使う**以上、
    /// 「壊さないのに数える」も「数えないのに壊す」も等しく避けたい。
    ///
    /// > **これでも近似である。** ベジェの膨らみは折れ線より外側へ出うるし、
    /// > 長い 1 本の道路の一部だけが範囲に掛かる場合も表せない。⑤が本数を
    /// > 「概数」としてしか出さない理由の 1 つがこれで、設計書 §7.2 がそう名乗る。
    ///
    /// エンジン非依存（<c>UnityEngine</c> も LINQ も <c>System.Random</c> も使わない）。
    /// </summary>
    public static class FootprintReach
    {
        /// <summary>
        /// 3 点 <paramref name="a"/> → <paramref name="mid"/> → <paramref name="b"/> の
        /// 折れ線が、中心 <paramref name="centre"/>・半径 <paramref name="radiusMetres"/> の
        /// 円に掛かるか。
        ///
        /// 半径が NaN か 0 以下なら false（範囲が無いものに掛かりようがない）。
        /// **NaN の点を含む脚は無視する** —— 読めなかった 1 点のせいで道路 1 本を
        /// 丸ごと取りこぼすより、残りの脚で判定するほうが安全側である。
        /// 3 点とも NaN なら false。
        /// </summary>
        public static bool CircleTouchesPolyline(Vec2 centre, float radiusMetres,
                                                 Vec2 a, Vec2 mid, Vec2 b)
        {
            if (float.IsNaN(radiusMetres) || radiusMetres <= 0f) return false;

            float radiusSquared = radiusMetres * radiusMetres;

            bool aOk = IsFinite(a);
            bool midOk = IsFinite(mid);
            bool bOk = IsFinite(b);

            if (aOk && midOk
                && DistanceSquaredToSegment(centre, a, mid) <= radiusSquared) return true;

            if (midOk && bOk
                && DistanceSquaredToSegment(centre, mid, b) <= radiusSquared) return true;

            // 中点だけが読めなかったときは両端を直接結ぶ（脚が 1 本も残らないのを避ける）。
            if (!midOk && aOk && bOk
                && DistanceSquaredToSegment(centre, a, b) <= radiusSquared) return true;

            // 残った 1 点だけで見る（他の 2 点が NaN のとき）。
            if (aOk && !midOk && !bOk) return centre.DistanceSquaredTo(a) <= radiusSquared;
            if (midOk && !aOk && !bOk) return centre.DistanceSquaredTo(mid) <= radiusSquared;
            if (bOk && !aOk && !midOk) return centre.DistanceSquaredTo(b) <= radiusSquared;

            return false;
        }

        /// <summary>
        /// 点 <paramref name="p"/> と線分 <paramref name="a"/>–<paramref name="b"/> の
        /// 距離の 2 乗。**平方根を取らない**（比較は 2 乗のまま行う）。
        /// 線分が退化している（2 点が同じ）ときは端点との距離になる。
        /// </summary>
        public static float DistanceSquaredToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            float abx = b.X - a.X;
            float abz = b.Z - a.Z;
            float apx = p.X - a.X;
            float apz = p.Z - a.Z;

            float lengthSquared = abx * abx + abz * abz;
            if (!(lengthSquared > 0f)) return apx * apx + apz * apz;

            float t = (apx * abx + apz * abz) / lengthSquared;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float dx = apx - abx * t;
            float dz = apz - abz * t;
            return dx * dx + dz * dz;
        }

        private static bool IsFinite(Vec2 v)
        {
            return !float.IsNaN(v.X) && !float.IsNaN(v.Z)
                   && !float.IsInfinity(v.X) && !float.IsInfinity(v.Z);
        }
    }
}
