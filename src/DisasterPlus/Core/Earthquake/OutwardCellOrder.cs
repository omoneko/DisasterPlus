namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 矩形のグリッドを**震央のセルから外側へ**辿るための順序。整数演算だけで、
    /// 序数 <c>n</c> ↔ 相対セル <c>(dx, dz)</c> を双方向に持たない**片道の写像**である
    /// （序数から相対セルを出せれば足りる）。
    ///
    /// ── なぜ順序を発明するのか（第 2 層レビュー I1）─────────────────────
    ///
    /// 長周期の走査は 1 回あたりの仕事量に上限を持ち、上限に達したら打ち切って
    /// 次回は続きから再開する。ところが打ち切りの順序が**行優先（row-major）**だと、
    /// 最初に見るセルは矩形の <c>(minX, minZ)</c> ＝ **震央からいちばん遠い角**になる。
    /// 追加倒壊確率は <c>(1 - d/range)</c> に比例するので、そこは確率がほぼ 0 の場所で
    /// ある。強度 55（range 6200 m）なら矩形はおよそ 194×194 セルで、成熟した都市の
    /// 1 セルに建物が 3 棟あるとすると建物の上限 2048 棟は 3 行半で尽きる。震央の行
    /// （97 行目）へ届くには 28 回ぶんの走査 ＝ 7,200 フレーム前後を要し、
    /// <c>m_activeDuration</c> がそれより短ければ**震央の街区には一切被害が出ないまま
    /// 地震が終わる**。遠くの高層が数棟だけ倒れて、震央の周りが無傷になる。
    ///
    /// そこで**チェビシェフ距離の輪（リング）順**にする。序数 0 が震央のセル、
    /// 以後は <c>max(|dx|,|dz|)</c> が小さい順に 1 周ずつ回る。上限で打ち切られたとき
    /// 落ちるのは**いちばん確率の低い外側**になり、震央の周りは必ず最初の走査で
    /// 評価される。
    ///
    /// **ユークリッド距離ではなくチェビシェフ距離**にしてあるのは、序数から相対セルを
    /// 整数演算だけで出せるからである。真の距離順との差は 1 リングぶん（最大 64 m の
    /// 判定順の前後）で、「近い方を先に見る」という目的には十分であり、
    /// **選定そのものは順序に一切依存しない**（<c>IsSelected</c> は地震 ID と建物 ID
    /// だけで決まり、フレームもセル順も混ぜない）ので、順序が結論を変えることはない。
    ///
    /// ── 矩形の外は「飛ばす」──────────────────────────────
    ///
    /// リングは震央を中心とした正方形なので、クランプされた矩形からはみ出す。
    /// はみ出したセルは<b>飛ばすだけ</b>で、1 回の走査の上限（セル数）には数えない。
    /// 飛ばしは整数演算 2〜3 個で、最悪でも <see cref="OrdinalCount"/> ＝ 29 万回ぶん
    /// （震央がマップの端にある場合）だが、走査の間隔は 256 フレームなので
    /// 実測が要るほどの量ではない。数えてしまうと、上限がはみ出しの分だけ
    /// 目減りして「実際に見た建物が上限より少ない」という読めない診断になる。
    ///
    /// **④台風の風害走査もこの順序を使う**（<c>Game/Typhoon/TyphoonWind</c>）。名前空間が
    /// Earthquake のままなのは意図的で、型を動かすと②のテストとレビュー済みの doc 参照が
    /// 全部動く。順序そのものは災害に依存しない。
    /// </summary>
    public static class OutwardCellOrder
    {
        /// <summary>
        /// 中心セルから見て、この矩形の全セルを覆うのに必要なリングの半径。
        ///
        /// 中心が矩形の外にあっても正しい（矩形内の任意の <c>(x, z)</c> について
        /// <c>|x - cx| ≤ max(|minX - cx|, |maxX - cx|)</c> が成り立つ）。
        /// </summary>
        public static int RingRadiusFor(int centreX, int centreZ,
                                        int minX, int maxX, int minZ, int maxZ)
        {
            int r = Abs(minX - centreX);
            r = Max(r, Abs(maxX - centreX));
            r = Max(r, Abs(minZ - centreZ));
            r = Max(r, Abs(maxZ - centreZ));
            return r;
        }

        /// <summary>
        /// 半径 <paramref name="ringRadius"/> までを 1 度ずつ辿るのに必要な序数の総数
        /// <c>(2r+1)²</c>。負の半径は 0 として 1 を返す（中心セルだけ）。
        ///
        /// 呼び出し側の実際の上限（グリッド 270 辺）では最大 541² ＝ 292,681 で、
        /// <c>int</c> の範囲に十分収まる。それでも桁あふれを塞いでおく ——
        /// あふれた値で <c>while (ordinal &lt; count)</c> を回すと**走査が 1 セルも
        /// 走らない**という、いちばん見えにくい壊れ方をする。
        /// </summary>
        public static int OrdinalCount(int ringRadius)
        {
            if (ringRadius <= 0) return 1;
            if (ringRadius > 23169) return int.MaxValue;   // 46339² < int.MaxValue
            int side = 2 * ringRadius + 1;
            return side * side;
        }

        /// <summary>
        /// 序数 <paramref name="ordinal"/> に対応する中心からの相対セル。
        /// 序数が負なら false（このとき <paramref name="dx"/> / <paramref name="dz"/> は 0）。
        ///
        /// 順序は 序数 0 が中心、以後リング <c>r = 1, 2, ...</c> を
        /// 東辺（南→北）→ 北辺（東→西）→ 西辺（北→南）→ 南辺（西→東）の順に 1 周する。
        /// **同じリング内のどの順で回るかは意味を持たない** —— 重要なのは
        /// 「リングの外側は必ず内側より後」だけである。
        /// </summary>
        public static bool Offset(int ordinal, out int dx, out int dz)
        {
            dx = 0;
            dz = 0;
            if (ordinal < 0) return false;
            if (ordinal == 0) return true;

            // (2r-1)² ≤ ordinal < (2r+1)² を満たす r。整数平方根で出す
            // （double の Sqrt だと (2r+1)² 直前で 1 ずれる環境がありうる）。
            int r = (IntegerSqrt(ordinal) + 1) / 2;
            int inner = 2 * r - 1;
            int k = ordinal - inner * inner;   // 0 .. 8r-1
            int sideLength = 2 * r;
            int side = k / sideLength;
            int t = k - side * sideLength;

            if (side == 0) { dx = r; dz = -r + t; return true; }
            if (side == 1) { dx = r - t; dz = r; return true; }
            if (side == 2) { dx = -r; dz = r - t; return true; }
            dx = -r + t;
            dz = -r;
            return true;
        }

        /// <summary>この相対セルのチェビシェフ距離（＝そのセルが属するリングの半径）。</summary>
        public static int RingOf(int dx, int dz)
        {
            return Max(Abs(dx), Abs(dz));
        }

        /// <summary>
        /// <c>floor(sqrt(n))</c>。<c>n &lt;= 0</c> は 0。
        ///
        /// **初期値をビット長から作る。** <c>x₀ = n</c> のニュートン法は
        /// <c>log₂(sqrt(n))</c> 回（n ≒ 29 万で 9 回）除算するが、
        /// <c>x₀ = 2^⌈bits/2⌉</c> なら 3 回前後で収まる。
        /// <see cref="Offset"/> は 1 セルにつき 1 回ここを通り、はみ出しを飛ばす
        /// 経路も通るので、最悪の走査（震央がマップの角・範囲が全域・建物がほとんど
        /// 無いマップ）では 1 回の走査で 13 万回ほど呼ばれる。
        /// </summary>
        private static int IntegerSqrt(int n)
        {
            if (n <= 0) return 0;
            if (n < 4) return 1;

            int bits = 0;
            for (int v = n; v != 0; v >>= 1) bits++;

            int x = 1 << ((bits + 1) / 2);   // ≧ sqrt(n)
            int y = (x + n / x) / 2;
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }
            return x;
        }

        private static int Abs(int v)
        {
            return v < 0 ? -v : v;
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
    }
}
