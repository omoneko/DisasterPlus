namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// サンプル列を「列ごとの行番号」に落とす純関数。描画そのものは
    /// <c>Game/Earthquake/WaveformView</c> が行う（こちらは <c>UnityEngine</c> に触れない）。
    ///
    /// ── バケットは**フレーム範囲**で割る。配列添字では割らない ──────────────
    ///
    /// <c>SimulationManager.SimulationStep</c> は 1 tick の中で
    /// <c>FinalSimulationSpeed</c> 回（ゲーム速度 1/2/3 で 1/3/9 回）ループするので、
    /// <c>m_currentFrameIndex</c> は tick ごとに 1/3/9 ずつ飛ぶ（火災旋風設計書 付録 A-4）。
    /// **サンプルの間隔はゲーム速度で変わる。** 添字で等分すると、速度を上げた瞬間に
    /// 時間軸が 9 倍に伸びた波形が描かれる。<c>frameIndex % N</c> で周期を組むのと
    /// 同じ種類の誤りで、症状が「もっともらしいが間違っている絵」なので気付きにくい。
    ///
    /// ── サンプルが 1 個も無い列は <see cref="Empty"/>（-1）──────────────────
    ///
    /// **0 を返してはいけない。** 0 は中央行（＝変位 0）に落ちるので、データが
    /// 届いていない区間が「揺れていない区間」として描かれる。①から続く
    /// 「読めていないものを 0 と名乗らない」の、この機能における現れである。
    /// </summary>
    public static class WaveformPlot
    {
        /// <summary>この列にはサンプルが 1 個も無い、の印。</summary>
        public const int Empty = -1;

        /// <summary>
        /// 各列の行番号（0 = 上端、<paramref name="height"/> = 下端、中央 = 変位 0）を返す。
        /// 戻り値の長さは常に <paramref name="width"/>。
        ///
        /// <paramref name="scale"/> は「変位 1.0 が中央から上端まで届く」倍率。
        /// 呼び出し側が振幅から決める（<c>1 / 最大振幅</c> など）。**0 以下や NaN を
        /// 渡すと全列 <see cref="Empty"/> になる** —— 縦軸が決まらないまま中央に
        /// 平らな線を引くと、揺れていない波形として読まれる。
        ///
        /// 窓の外（<paramref name="fromFrame"/> 未満 / <paramref name="toFrame"/> 超）の
        /// サンプルは端の列へ寄せずに捨てる。寄せると、そこだけ古い揺れが積み上がった
        /// 柱になる。
        ///
        /// 壊れた入力（null 配列・件数 0・幅ゼロの窓）でも例外を投げない。
        /// </summary>
        public static int[] Columns(uint[] frames, float[] values, int count,
                                    uint fromFrame, uint toFrame,
                                    int width, int height, float scale)
        {
            if (width < 1) width = 1;

            int[] columns = new int[width];
            for (int i = 0; i < width; i++) columns[i] = Empty;

            if (frames == null || values == null) return columns;
            if (count > frames.Length) count = frames.Length;
            if (count > values.Length) count = values.Length;
            if (count <= 0) return columns;
            if (height < 1) return columns;
            if (scale <= 0f || float.IsNaN(scale)) return columns;
            if (toFrame <= fromFrame) return columns;

            long span = (long)toFrame - fromFrame;
            int middle = height / 2;

            // 列ごとに「今のところ絶対値が最大の変位」。Columns は再描画のときだけ
            // 呼ばれるので（毎フレームではない）、この 1 本の割り当ては許容する。
            float[] best = new float[width];

            for (int i = 0; i < count; i++)
            {
                float v = values[i];
                if (float.IsNaN(v)) continue;

                // uint 同士の引き算にしない。窓より古いサンプルが巨大な正の値に化ける。
                long relative = (long)frames[i] - fromFrame;
                if (relative < 0 || relative > span) continue;

                int column = (int)(relative * width / span);
                if (column >= width) column = width - 1;

                float magnitude = v < 0f ? -v : v;
                float bestMagnitude = best[column] < 0f ? -best[column] : best[column];

                // 同じ列に複数落ちたら絶対値が最大のものを採る。最後の 1 個を採ると、
                // たまたま零交差に当たった列で揺れが消える。
                if (columns[column] != Empty && magnitude <= bestMagnitude) continue;

                best[column] = v;
                columns[column] = RowOf(v, scale, middle, height);
            }

            return columns;
        }

        /// <summary>
        /// 変位 → 行番号。中央行が変位 0 で、上へ行くほど行番号が小さい。
        ///
        /// 切り捨てではなく四捨五入する。float の 0.9 は double に広げると
        /// 0.899999976… なので、切り捨てだと 1 行ぶん内側に寄って波形が痩せる。
        /// 掛け算は double で行い、**int にする前に**クランプする（int の範囲を
        /// 溢れると符号が反転して、上端に飛ぶはずの列が下端に出る）。
        /// </summary>
        private static int RowOf(float value, float scale, int middle, int height)
        {
            double offset = (double)value * scale * middle;
            if (offset > height) offset = height;
            if (offset < -height) offset = -height;

            int row = middle - (int)System.Math.Round(offset);
            if (row < 0) row = 0;
            if (row > height) row = height;
            return row;
        }
    }
}
