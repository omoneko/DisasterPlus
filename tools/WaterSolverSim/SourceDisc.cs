using System;

namespace DisasterPlus.Tools.WaterSolverSim
{
    /// <summary>
    /// <b><c>WaterSource</c> の <c>TYPE_NATURAL</c>（マップの川の湧き出し）の再現。</b>
    ///
    /// ── なぜこれなのか（2026-08-31、所有者の指示）────────────────────
    ///
    /// &gt; DLC そのまま使っちゃったら わざわざ MOD で出す意味ないじゃないですか。
    /// &gt; 仕組みを解析して応用、震源付近から津波が同心円状に発生するのを作ってほしい
    ///
    /// DLC の津波が強いのは<b>振幅ではなく仕掛け</b>である —— 外周セルの海面を
    /// 書き換え、Dirichlet 境界が<b>水を湧かせる</b>。<c>TYPE_IMPACT</c> の丘は
    /// 水を押しのけるだけで作らないので、どれだけ大きくしても真似できない
    /// （<see cref="EdgeWave"/> のクラス doc、およびオフライン比較 2026-08-31:
    ///  同じ棚で汀線 84.8 m 対 19.3 m）。
    ///
    /// ★★ **ところがゲームは、同じことをマップの真ん中でやる道具を持っている。**
    ///   <c>WaterSource</c> の <c>TYPE_NATURAL</c> は
    ///   「指定した円を指定した水位まで満たす／抜く」装置で、置き場所は自由である。
    ///   <b>境界条件を震源へ持ってくる</b> —— これが「解析して応用」の中身。
    ///
    /// ── IL（<c>WaterSimulation.SimulateWater</c> IL_184A-20B3）────────────
    ///
    /// 取り込み（<c>m_inputRate</c>、<c>m_inputPosition</c> のまわり）:
    /// <code>
    /// r     = sqrt(inputRate)*0.4 + 10          // m。type 2/3 だけ min(50, r)
    /// total = SUM min(terrain + h - max(target, terrain), h)   // 目標より上の水
    /// take  = min(inputRate, total &gt;&gt; 1)        // natural は毎ステップ超過の半分
    /// // 2 周目: share = (share*take + total - 1)/total  を各セルから引く
    /// </code>
    ///
    /// 吐き出し（<c>m_outputRate</c>、<c>m_outputPosition</c> のまわり）:
    /// <code>
    /// out  = outputRate                          // ★ natural は m_water と無関係に湧く
    /// r    = sqrt(out)*0.4 + 10                  // ★ natural は上限なし
    /// room = SUM min(terrain + h - max(target, terrain), h)   // 目標までの不足（負）
    ///        ただし natural は terrain &gt;= target のセルを飛ばす（＝陸には出さない）
    /// out  = min(out, -(room &gt;&gt; 1))              // 毎ステップ不足の半分
    /// // 2 周目: share = (out + count/2)/count を各セルへ均等に足す
    /// </code>
    ///
    /// ★ <b>半径は流量で決まる</b>（<c>r = sqrt(rate)*0.4 + 10</c>）。半径 1280 m が
    ///   欲しければ rate はおよそ 1.0e7 になる。流量は「速さ」、<c>m_target</c> が
    ///   「高さ」を決めるので、両方を別々に効かせられる。
    /// </summary>
    internal sealed class SourceDisc
    {
        /// <summary>円の中心（セル）。</summary>
        public int CellX;
        public int CellZ;

        /// <summary>目標水面（1/64 m の絶対標高）。実機の <c>m_target</c> は ushort。</summary>
        public int Target;

        /// <summary>取り込み流量。0 なら取り込まない。</summary>
        public long InputRate;

        /// <summary>吐き出し流量。0 なら吐き出さない。</summary>
        public long OutputRate;

        /// <summary>いままでに湧かせた量（診断）。</summary>
        public long Made;

        /// <summary>いままでに抜いた量（診断）。</summary>
        public long Taken;

        /// <summary>
        /// <b>ゲームの int32 なら溢れていた回数。</b>（ゲームには対応物が無い診断）
        ///
        /// ★★ ここが 0 でない設定は<b>実機で使ってはいけない</b>。
        ///   このクラスの冒頭 doc と、IL_1B4C-1B58 を参照。
        /// </summary>
        public long Int32Overflows;

        /// <summary>溢れた最大の積（診断）。int.MaxValue = 2,147,483,647。</summary>
        public long WorstProduct;

        /// <summary>
        /// 円の中でいちばん高い水面（1/64 m の絶対標高）。
        /// **目標を実測から縛るために要る**（Program の --ringgap）。
        /// </summary>
        public int MaxSurface(WaterField field, float radiusMetres)
        {
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, radiusMetres, out minX, out minZ, out maxX, out maxZ)) return 0;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = radiusMetres * radiusMetres;
            int best = int.MinValue;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int s = terrain[i] + cells[i].Height;
                    if (s > best) best = s;
                }
            }

            return best == int.MinValue ? 0 : best;
        }

        /// <summary>実機と同じ半径（m）。natural に上限は無い。</summary>
        public static float RadiusMetres(long rate)
        {
            return (float)Math.Sqrt(rate) * 0.4f + 10f;
        }

        /// <summary>1 水ステップぶん。**取り込みが先、吐き出しが後**（IL の順）。</summary>
        public void Apply(WaterField field)
        {
            Take(field);
            Give(field);
        }

        private void Take(WaterField field)
        {
            if (InputRate <= 0) return;

            float rm = RadiusMetres(InputRate);
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, rm, out minX, out minZ, out maxX, out maxZ)) return;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = rm * rm;

            long total = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];
                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    total += Math.Min(g + h - lvl, h);
                }
            }

            long take = Math.Min(InputRate, total >> 1);
            if (take <= 0) return;

            // ★★ ゲームは `total` も int32（loc154）。ここが溢れる設定も使えない。
            if (total > int.MaxValue || total < int.MinValue)
            {
                Int32Overflows++;
                if (Math.Abs(total) > WorstProduct) WorstProduct = Math.Abs(total);
            }

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];
                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    long share = Math.Min(g + h - lvl, h);

                    // ★★ **ゲームなら int32 の mul である**（IL_1B50）。
                    //    積が int.MaxValue を越える設定は実機でセルの高さを壊す。
                    long product = share * take;
                    if (product > int.MaxValue || product < int.MinValue)
                    {
                        Int32Overflows++;
                        if (Math.Abs(product) > WorstProduct) WorstProduct = Math.Abs(product);
                    }

                    share = (share * take + total - 1) / total;
                    if (share <= 0) continue;
                    if (share > h) share = h;

                    cells[i].Height = (ushort)(h - share);
                    Taken += share;
                }
            }
        }

        private void Give(WaterField field)
        {
            if (OutputRate <= 0) return;

            long give = OutputRate;
            float rm = RadiusMetres(give);
            int minX, minZ, maxX, maxZ;
            if (!Bounds(field, rm, out minX, out minZ, out maxX, out maxZ)) return;

            ushort[] terrain = field.Terrain;
            Cell[] cells = field.Cells;
            int n = field.Size;
            float rr = rm * rm;

            long room = 0;
            int count = 0;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    int g = terrain[i];

                    // ★ natural は陸へは出さない（IL_1E51-1E61）。
                    if (g >= Target) continue;

                    int h = cells[i].Height;
                    int lvl = Math.Max(Target, g);
                    room += Math.Min(g + h - lvl, h);
                    count++;
                }
            }

            if (count <= 0) return;

            // ★ 吐き出し側の room も int32（loc181）。
            if (room > int.MaxValue || room < int.MinValue)
            {
                Int32Overflows++;
                if (Math.Abs(room) > WorstProduct) WorstProduct = Math.Abs(room);
            }

            long allowed = -(room >> 1);
            if (give > allowed) give = allowed;
            if (give <= 0) return;

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = (z - CellZ) * WaterField.CellSizeMetres;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x - CellX) * WaterField.CellSizeMetres;
                    if (dx * dx + dz * dz >= rr) continue;

                    int i = z * n + x;
                    if (terrain[i] >= Target) continue;

                    int h = cells[i].Height;
                    long share = (give + count / 2) / count;
                    if (share > 65535 - h) share = 65535 - h;
                    if (share <= 0) continue;

                    cells[i].Height = (ushort)(h + share);
                    Made += share;
                }
            }
        }

        private bool Bounds(WaterField field, float rm, out int minX, out int minZ,
                            out int maxX, out int maxZ)
        {
            int rc = (int)Math.Ceiling(rm / WaterField.CellSizeMetres);
            minX = Math.Max(0, CellX - rc);
            minZ = Math.Max(0, CellZ - rc);
            maxX = Math.Min(field.LastIndex, CellX + rc);
            maxZ = Math.Min(field.LastIndex, CellZ + rc);
            return minX <= maxX && minZ <= maxZ;
        }
    }
}
