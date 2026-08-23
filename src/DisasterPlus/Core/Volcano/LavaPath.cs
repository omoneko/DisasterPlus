using System;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// 溶岩の 1 歩。**純関数で、勾配の符号を知らない。**
    ///
    /// 呼び出し側（<c>Game/Volcano/VolcanoLava</c>）が
    /// <c>TerrainManager.SampleDetailHeight(Vector3, out slopeX, out slopeZ)</c> の結果を
    /// **下り方向の単位ベクトル**に直してから渡す。ここに符号の解釈を置かないのは、
    /// 事実文書 §B-6 が 3 引数版の存在を確定させている一方で
    /// **slopeX / slopeZ の符号までは読んでいなかった**からである。
    /// 取り違えると溶岩が山を登るが、例外は 1 つも出ない。
    /// 符号は Game 側の 1 箇所で確定させ、そこで実行時にも観測する。
    ///
    /// > **本タスクで IL を読んで符号は確定した**（<c>VolcanoLava</c> のクラス doc に
    /// > 実測を書いてある）。それでも**この型には符号を持ち込まない** ——
    /// > ここは「向きと歩幅から次の点を出す」だけの型で、地形の解釈を持たないことが
    /// > テストで固定できる範囲を決めている。
    ///
    /// **平ら・窪み・NaN は「進まない」を返す**（溜まる）。とりあえず前へ進めると、
    /// 溶岩が窪地を素通りしてマップの端まで走り続ける。
    ///
    /// 乱数は <see cref="DisasterPlus.Core.Common.DeterministicRandom"/> だけを使う。
    /// <c>VanillaRandomizer</c> は**使わない** —— ここで決めるのはバニラが引く値ではなく、
    /// ⑤が発明した判断である（⑤はバニラの災害スロットに載らないので、
    /// 同期すべきバニラの引きが構造上 1 つも存在しない）。
    /// </summary>
    public static class LavaPath
    {
        /// <summary>1 歩の距離（m）。地形の詳細セルが 4 m なので、その 3 倍。</summary>
        public const float StepMetres = 12f;

        /// <summary>
        /// これ未満の勾配は「平ら」とみなして止める（無次元。1 = 45 度）。
        /// <c>SampleDetailHeight</c> の 3 引数版が返す傾斜は
        /// **メートル毎メートル**なので、これはそのまま勾配である（§B-6 の実測）。
        /// </summary>
        public const float MinSlope = 0.002f;

        /// <summary>1 本の流れが進める最大歩数。**止まらない溶岩を作らない。**</summary>
        public const int MaxSteps = 512;

        /// <summary>
        /// 溶岩を出す点を火口の縁からどれだけ外へ出すか（火口半径に対する比）。
        ///
        /// ★★ **縁のちょうど上から出さないこと。** 火口は 2026-08-22 から高さ
        ///   プロファイルの一部で、縁は<b>稜線（局所的な最大）</b>である。その真上で
        ///   <c>SampleDetailHeight</c> の勾配を読むと、下り方向が**火口の内側を指すことが
        ///   ある** —— 溶岩は窪みへ流れ落ち、<c>StopFlat</c> でその場に溜まって終わる。
        ///   例外は 1 つも出ないので、症状は「溶岩が 1 本も山を下らない」だけである。
        /// </summary>
        public const float VentRimClearanceFactor = 1.15f;

        /// <summary>
        /// 同上の絶対値の下限（m）。地形の raw セルが 16 m なので、その 1.5 倍だけ外へ出て
        /// 「縁のセル」から確実に離れる。小さい火口（半径 40 m）では比より効く。
        /// </summary>
        public const float VentRimClearanceMetres = 24f;

        /// <summary>
        /// 溶岩を出す半径（m）。<paramref name="craterRadiusMetres"/> が読めないときは
        /// 1 歩ぶん（<see cref="StepMetres"/>）を返す —— **0 を返して中心から出さない。**
        /// </summary>
        public static float VentRadiusMetres(float craterRadiusMetres)
        {
            if (IsBad(craterRadiusMetres) || craterRadiusMetres <= 0f) return StepMetres;

            float byFactor = craterRadiusMetres * VentRimClearanceFactor;
            float byMetres = craterRadiusMetres + VentRimClearanceMetres;
            return byFactor > byMetres ? byFactor : byMetres;
        }

        /// <summary>火口を出た直後の流れの幅（半径 m）。</summary>
        public const float SpreadBaseMetres = 28f;

        /// <summary>どれだけ流れても超えない幅（半径 m）。</summary>
        public const float SpreadMaxMetres = 78f;

        /// <summary>
        /// 倍率を掛けたあとでも**絶対に超えない**広がり（m）。
        ///
        /// ★★ これは演出値ではなく<b>着火の走査が数え切れる上限</b>である
        ///   （2026-08-22）。<c>VolcanoLava.Ignite</c> は 1 歩ごとに
        ///   <c>p ± radius</c> の矩形を行優先で舐めるので、半径が大きくなると
        ///   セル数が 1 歩あたりの上限（<c>MaxBuildingCellsPerStep</c> /
        ///   <c>MaxTreeCellsPerStep</c>）を超えて**黙って打ち切られる** ——
        ///   そうなると「光っている溶岩の下の建物が燃えない」が起きる。
        ///
        ///   96 m のとき:
        ///     建物グリッド（64 m 角）… 192 m を跨ぐので高々 4×4 ＝ 16 セル（上限 25）
        ///     樹木グリッド（32 m 角）… 192 m を跨ぐので高々 7×7 ＝ 49 セル（上限 64）
        ///
        ///   **ここを上げるなら、必ず両方の上限も上げ直すこと。**
        /// </summary>
        public const float SpreadHardMaxMetres = 96f;

        /// <summary>1 km 進むごとに広がる量（m）。</summary>
        public const float SpreadPerKilometre = 25f;

        /// <summary>
        /// 下り方向へ 1 歩進む。<paramref name="downhill"/> は**下り方向のベクトル**で、
        /// 長さは問わない（内部で正規化する）。
        ///
        /// 進めないときは <paramref name="next"/> に <paramref name="current"/> を入れて
        /// <c>false</c> を返す。**「とりあえず前へ」をやらない**（クラス doc）。
        /// </summary>
        public static bool NextPosition(Vec2 current, Vec2 downhill, float stepMetres,
                                        out Vec2 next)
        {
            next = current;

            if (IsBad(current.X) || IsBad(current.Z)) return false;
            if (IsBad(downhill.X) || IsBad(downhill.Z)) return false;
            if (IsBad(stepMetres) || stepMetres <= 0f) return false;

            float length = (float)Math.Sqrt(downhill.X * downhill.X + downhill.Z * downhill.Z);
            if (IsBad(length) || length < MinSlope) return false;

            float inv = 1f / length;
            next = new Vec2(current.X + downhill.X * inv * stepMetres,
                            current.Z + downhill.Z * inv * stepMetres);
            return true;
        }

        /// <summary>
        /// <paramref name="flowIndex"/> 本目の初期方向（**必ず単位長**）。
        /// 等間隔に配りつつ <see cref="DeterministicRandom"/> で少し揺らす。
        ///
        /// 揺らぎの幅を <c>±π/(2n)</c> に抑えてあるのは、隣の流れと入れ替わらない
        /// ようにするためである（入れ替わると「放射状に出る」が崩れる）。
        /// **フレーム番号は混ぜない** —— 混ぜると同じ流れが tick ごとに向きを引き直す。
        /// </summary>
        public static Vec2 InitialDirection(uint seed, int flowIndex, int flowCount)
        {
            int n = flowCount <= 0 ? 1 : flowCount;
            int i = ((flowIndex % n) + n) % n;

            double baseAngle = 2.0 * Math.PI * i / n;
            float jitter = DeterministicRandom.Unit(seed, (uint)i) - 0.5f;
            double angle = baseAngle + jitter * (Math.PI / n);

            return new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        /// <summary>
        /// 流れた距離から今の広がり（半径 m）。遠くへ行くほど広がるが、必ず頭打ちになる。
        /// おかしな入力は <see cref="SpreadBaseMetres"/> に落とす（**NaN を外へ出さない**）。
        /// </summary>
        public static float SpreadRadiusFor(float travelledMetres)
        {
            return SpreadRadiusFor(travelledMetres, 1f);
        }

        /// <summary>
        /// 上と同じだが、<paramref name="widthFactor"/> のぶん太らせる
        /// （<c>LavaVolume.WidthFactor</c>。2026-08-22、所有者の依頼
        /// 「溶岩流の太さを、もう少し太くしてほしいです（噴火規模に合わせて）」）。
        ///
        /// ★★ <b>この 1 本が見た目と被害の両方を決める。</b>
        ///   <c>VolcanoLavaFx</c> の帯の幅も <c>VolcanoLava.Ignite</c> の着火半径も
        ///   ここから出ている。**片方だけ太らせない** —— 描いた溶岩が、
        ///   その下の建物に火を付けないのは嘘である。
        ///
        /// 結果は必ず <see cref="SpreadHardMaxMetres"/> 以下（あちらの doc の理由）。
        /// </summary>
        public static float SpreadRadiusFor(float travelledMetres, float widthFactor)
        {
            float f = IsBad(widthFactor) || widthFactor <= 0f ? 1f : widthFactor;

            if (IsBad(travelledMetres) || travelledMetres < 0f)
            {
                return Cap(SpreadBaseMetres * f);
            }

            float r = SpreadBaseMetres + travelledMetres / 1000f * SpreadPerKilometre;
            if (r > SpreadMaxMetres) r = SpreadMaxMetres;
            return Cap(r * f);
        }

        private static float Cap(float r)
        {
            if (r < 0f) return 0f;
            return r > SpreadHardMaxMetres ? SpreadHardMaxMetres : r;
        }

        private static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }
    }
}
