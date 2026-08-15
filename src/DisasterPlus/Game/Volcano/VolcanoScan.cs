using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ⑤が「影響範囲に入っている建物と道路」を決める**唯一の場所**。
    /// 数える側（<see cref="VolcanoSurvey"/>）と壊す側（<see cref="VolcanoClearing"/>）の
    /// 両方がここを通る。**sim スレッド専用**（ゲームのバッファを読む）。
    ///
    /// ── なぜ型を 1 つ作ったのか（全体レビュー I2 / I4）─────────────────
    ///
    /// 元は同じ規則が 2 つのファイルに写してあり、<c>VolcanoClearing</c> の doc が
    /// 「<c>SegmentGridMargin</c> は <c>VolcanoSurvey</c> と同じ値でなければ、
    /// 数えた本数と壊す本数がずれる」と**注意書きで**担保していた。
    /// レビューはその隣で**マスクが実際にずれていた**ことを見つけている ——
    /// 調査側だけが <c>Untouchable</c> と <c>Collapsed</c> を弾いており、
    /// **不可逆の操作の直前に、壊れる数を実際より少なく見せていた**。
    ///
    /// > 注意書きは同じ間違いを 2 度目に防げなかった。**だから定数を共有する。**
    /// > 調査と準備でマスク・余白・当たり判定のどれか 1 つでも変えたくなったら、
    /// > ここを変えるしかなく、変えれば必ず両方に効く。
    ///
    /// ── マスク（IL 事実文書 §G-16 (c)(d)）───────────────────────
    ///
    /// ⑤が取り除かなければならないのは「<b>地形を自分の高さに固定し続けるもの</b>」
    /// である。<c>Building.TerrainUpdated</c> は <c>m_flags &amp; 524291</c>
    /// （<c>Created|Deleted|Demolishing</c>）しか見ず、<c>NetSegment.TerrainUpdated</c> は
    /// <c>m_flags &amp; 3</c> しか見ない。したがって:
    ///
    ///   - <c>Untouchable</c> の建物・道路も**固定する**ので候補に含める
    ///   - <c>Collapsed</c> の瓦礫と倒壊済みの道路も**固定する**ので候補に含める。
    ///     しかも <c>demolish: true</c> は既に <c>Collapsed</c> の建物にも効き、
    ///     <c>Demolishing</c> を立てて <b>true を返す</b>（§G-16 (d) の IL_0210。
    ///     本タスクで再度逆アセンブルして確認した）
    ///   - <c>Demolishing</c> だけは弾く —— そこは既に地形固定が止まっている
    ///     （<c>NetSegment.Flags</c> に <c>Demolishing</c> は無い。§F-15）
    ///
    /// ── 道路の当たり判定（§F-15 / <see cref="FootprintReach"/>）──────────────
    ///
    /// セルを決める位置は**両端ノードの中点**、位置を持っているフィールドは
    /// <c>m_middlePosition</c>（ベジェの中点の平均）で、**この 2 つは同じではない**。
    /// だから矩形を <see cref="SegmentGridMargin"/> セルだけ広げ、距離は
    /// <b>両端ノード → 中点 → 両端ノードの折れ線</b>で測る。
    /// 中点 1 点で測ると、中心へ向かって伸びる幹線道路が取り除かれずに残り、
    /// **完成した山の中に平らな溝が残る**（あちらのクラス doc）。
    /// </summary>
    internal static class VolcanoScan
    {
        /// <summary>
        /// 道路の矩形を広げるセル数。セルを決める位置（両端ノードの中点）と、
        /// 折れ線が届く範囲は同じではない（クラス doc / §F-15）。
        ///
        /// **これでも取りこぼしうる**: 128 m より長く矩形の外へ出ている道路は、
        /// 折れ線が円に掛かっていてもセルが矩形の外にある。実際の道路 1 本の
        /// 長さ（ゲームの上限は概ね数百 m）に対して 2 セルは十分だが、
        /// 「必ず全部」ではないことを名乗っておく。
        /// </summary>
        internal const int SegmentGridMargin = 2;

        /// <summary>
        /// 建物の候補条件。<c>Untouchable</c> と <c>Collapsed</c> を**含める**
        /// のが⑤に固有の判断で、理由はクラス doc にある。
        /// </summary>
        internal const Building.Flags BuildingCandidateMask =
            Building.Flags.Created | Building.Flags.Deleted | Building.Flags.Demolishing;

        /// <summary>
        /// 道路の候補条件。<c>NetSegment.Flags</c> に <c>Demolishing</c> は無い（§F-15）ので、
        /// <c>Created</c> かつ <c>Deleted</c> でないことだけを見る。
        /// </summary>
        internal const NetSegment.Flags SegmentCandidateMask =
            NetSegment.Flags.Created | NetSegment.Flags.Deleted;

        /// <summary>この建物は⑤の相手か（<see cref="BuildingCandidateMask"/>）。</summary>
        internal static bool IsCandidate(Building.Flags flags)
        {
            return (flags & BuildingCandidateMask) == Building.Flags.Created;
        }

        /// <summary>この道路は⑤の相手か（<see cref="SegmentCandidateMask"/>）。</summary>
        internal static bool IsCandidate(NetSegment.Flags flags)
        {
            return (flags & SegmentCandidateMask) == NetSegment.Flags.Created;
        }

        /// <summary>
        /// 建物が影響範囲に入っているか。距離は <c>m_position</c> で測る ——
        /// 大きな建物は角が範囲の中にあっても中心が外なら入らないが、
        /// **それは概算であり、そう名乗るほうが「全部壊れる」と嘘をつくより正しい**
        /// （設計書 §7.2）。
        /// </summary>
        internal static bool BuildingInside(Vector3 position, Vec2 origin, float radiusSquared)
        {
            return origin.DistanceSquaredTo(new Vec2(position.x, position.z)) <= radiusSquared;
        }

        /// <summary>
        /// 道路が影響範囲に入っているか（クラス doc の折れ線判定）。
        ///
        /// <paramref name="nodes"/> が null（ノードのバッファが読めない）なら
        /// <c>m_middlePosition</c> の 1 点だけで判定する。**そのときは
        /// 中心へ伸びる道路を取りこぼしうる**が、数える側と壊す側で同じ関数を
        /// 通っている以上、両者は同じ答えを出す。
        /// </summary>
        internal static bool SegmentInside(NetSegment[] segments, NetNode[] nodes, ushort id,
                                           Vec2 origin, float radiusMetres)
        {
            Vector3 middle = segments[id].m_middlePosition;
            var mid = new Vec2(middle.x, middle.z);

            Vec2 start = mid;
            Vec2 end = mid;

            if (nodes != null)
            {
                ushort startNode = segments[id].m_startNode;
                ushort endNode = segments[id].m_endNode;

                if (startNode != 0 && startNode < nodes.Length)
                {
                    Vector3 p = nodes[startNode].m_position;
                    start = new Vec2(p.x, p.z);
                }

                if (endNode != 0 && endNode < nodes.Length)
                {
                    Vector3 p = nodes[endNode].m_position;
                    end = new Vec2(p.x, p.z);
                }
            }

            return FootprintReach.CircleTouchesPolyline(origin, radiusMetres, start, mid, end);
        }

        /// <summary>
        /// 道路のノードのバッファ。読めなければ null（<see cref="SegmentInside"/> が
        /// 中点だけの判定に落ちる）。**例外を投げない。**
        /// </summary>
        internal static NetNode[] NodeBuffer(NetManager nm)
        {
            if (nm == null) return null;
            return nm.m_nodes != null ? nm.m_nodes.m_buffer : null;
        }
    }
}
