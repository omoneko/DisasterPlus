using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 溶岩の帯（リボン）の頂点を作る**純データ**。④の <c>SpiralMesh</c> と同じ扱いで、
    /// <c>UnityEngine</c> を 1 つも知らない。
    ///
    /// ── なぜゼロから作るのか ─────────────────────────────────
    ///
    /// 溶岩・マグマ・溶融物のプレハブもマテリアルもシェーダも、**DLL の文字列ヒープに
    /// 1 件も無い**（§B-5。UTF-16 / ASCII の両方で <c>lava</c> / <c>magma</c> /
    /// <c>molten</c> を走査してヒット 0）。借りられる既製品が存在しないので、
    /// ⑤は形も面も自分で作る。
    ///
    /// ── 高さは⑤が持たない ────────────────────────────────
    ///
    /// <paramref name="heightOffset"/> は「地面からどれだけ浮かせるか」だけである。
    /// **地面の高さは Game 側が <c>SampleDetailHeight</c> で引いて各頂点に足す。**
    /// Core に地形を持ち込まない。
    ///
    /// ── 折れ線は 1 本ぶんである ──────────────────────────────
    ///
    /// 溶岩は複数本流れるが、この型が扱うのは**1 本の折れ線**だけである。
    /// 呼び出し側が流れごとに呼び、できた頂点を 1 枚のメッシュへ詰め合わせる
    /// （別々の流れの点を 1 本の折れ線として渡すと、流れの間を飛び回る帯になる）。
    /// </summary>
    public static class LavaRibbon
    {
        /// <summary>1 本の折れ線に使える点の上限。**配列を無限に伸ばさない。**</summary>
        public const int MaxPoints = 256;

        /// <summary>帯の最小の幅（m）。0 幅の帯は面積を持たず、何も描かれない。</summary>
        public const float MinWidthMetres = 4f;

        /// <summary>点の数に対する頂点数（各点の左右で 2 個）。</summary>
        public static int VertexCountFor(int pointCount)
        {
            if (pointCount < 2) return 0;
            return pointCount * 2;
        }

        /// <summary>点の数に対する三角形の添字の数（区間ごとに 2 枚 ＝ 6 個）。</summary>
        public static int TriangleIndexCountFor(int pointCount)
        {
            if (pointCount < 2) return 0;
            return (pointCount - 1) * 6;
        }

        /// <summary>
        /// 折れ線と幅から帯を作る。点が 2 個に満たなければ**何も作らずに false**
        /// （縮退したメッシュを Unity へ渡すと、例外は出ないまま描画が静かに壊れる）。
        ///
        /// <paramref name="count"/> が <see cref="MaxPoints"/> を超えたら
        /// <see cref="MaxPoints"/> で切る。
        ///
        /// 進行方向は前後の点の差分（端は片側だけ）。**差分が 0 なら直前の有効な
        /// 方向を引き継ぐ**（無ければ <c>(1, 0)</c>）—— 溶岩が止まると同じ点が続くので、
        /// ここで 0 除算すると <c>NaN</c> の頂点ができる。
        /// </summary>
        public static bool Build(Vec2[] points, int count, float[] widths, float heightOffset,
                                 out Vec3[] vertices, out float[] uv, out int[] triangles)
        {
            vertices = null;
            uv = null;
            triangles = null;

            if (points == null || widths == null) return false;
            if (count < 2) return false;

            int n = count;
            if (n > MaxPoints) n = MaxPoints;
            if (n > points.Length) n = points.Length;
            if (n > widths.Length) n = widths.Length;
            if (n < 2) return false;

            vertices = new Vec3[VertexCountFor(n)];
            uv = new float[VertexCountFor(n) * 2];
            triangles = new int[TriangleIndexCountFor(n)];

            float lastDirX = 1f;
            float lastDirZ = 0f;

            for (int i = 0; i < n; i++)
            {
                int prev = i > 0 ? i - 1 : 0;
                int next = i < n - 1 ? i + 1 : n - 1;

                float dx = points[next].X - points[prev].X;
                float dz = points[next].Z - points[prev].Z;
                float length = (float)Math.Sqrt(dx * dx + dz * dz);

                if (length > 1e-4f && !IsBad(length) && !IsBad(dx) && !IsBad(dz))
                {
                    lastDirX = dx / length;
                    lastDirZ = dz / length;
                }

                // 進行方向を 90 度回した単位ベクトル。
                float normalX = -lastDirZ;
                float normalZ = lastDirX;

                float half = widths[i];
                if (IsBad(half) || half < MinWidthMetres) half = MinWidthMetres;
                half *= 0.5f;

                float px = points[i].X;
                float pz = points[i].Z;
                if (IsBad(px)) px = 0f;
                if (IsBad(pz)) pz = 0f;

                int left = i * 2;
                int right = left + 1;

                vertices[left] = new Vec3(px + normalX * half, heightOffset,
                                          pz + normalZ * half);
                vertices[right] = new Vec3(px - normalX * half, heightOffset,
                                           pz - normalZ * half);

                float v = n > 1 ? (float)i / (n - 1) : 0f;
                uv[left * 2] = 0f;
                uv[left * 2 + 1] = v;
                uv[right * 2] = 1f;
                uv[right * 2 + 1] = v;
            }

            for (int i = 0; i < n - 1; i++)
            {
                int t = i * 6;
                int a = i * 2;

                triangles[t] = a;
                triangles[t + 1] = a + 2;
                triangles[t + 2] = a + 1;

                triangles[t + 3] = a + 1;
                triangles[t + 4] = a + 2;
                triangles[t + 5] = a + 3;
            }

            return true;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
