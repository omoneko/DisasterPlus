namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// バニラの強度スライダーの生値（0〜255）を、⑤の「山の大きさの倍率」に読み替える。
    ///
    /// ── なぜ倍率なのか ───────────────────────────────────
    ///
    /// バニラの災害は「タイルを選ぶ → 強度スライダー → 地図をクリック」で起きる。
    /// ⑤にも同じ流れを与える、というのが所有者の依頼である。ところが⑤には
    /// 「強度」に相当する量が無い —— ⑤が持っているのは<b>形態・半径・最終高</b>で、
    /// それは設定画面にある。そこでスライダーを<b>設定のサイズに対する倍率</b>として読む。
    ///
    ///   - <see cref="AnchorRaw"/>（55、ゲーム自身の既定値）＝ <b>設定どおりのサイズ</b>
    ///   - 生値が 2 倍なら山も 2 倍（半径も最終高も同じ倍率で伸びる）
    ///
    /// **設定画面の半径・最終高が死んだつまみにならない**のが要点である。
    /// スライダーはそこからの伸び縮みしか決めない。実際に何メートルになるかは
    /// 形態ごとの帯（<see cref="VolcanoShape.RadiusFor"/> /
    /// <see cref="VolcanoShape.HeightFor"/>）が最後にクランプし、
    /// **確認の行がその結果をメートルで見せてから**でないと 1 つも壊れない。
    ///
    /// ── 倍率の帯 ────────────────────────────────────
    ///
    /// 生値 0 でも <see cref="MinScale"/> より小さくしない。0 倍は「山を作らない」で
    /// あって「小さい山」ではなく、押しても何も起きない操作を作ることになる。
    /// 上は <see cref="MaxScale"/> で切る（形態ごとの帯がどうせ切るが、
    /// **NaN や桁違いの値をそのまま掛けない**）。
    /// </summary>
    public static class VolcanoSizeScale
    {
        /// <summary>倍率 1.0 に対応する生値。ゲーム自身の災害の既定強度と同じ 55。</summary>
        public const int AnchorRaw = 55;

        /// <summary>いちばん小さくしたときの倍率。0 倍（＝何も起きない）にはしない。</summary>
        public const float MinScale = 0.2f;

        /// <summary>いちばん大きくしたときの倍率。形態ごとの帯がさらに切る。</summary>
        public const float MaxScale = 4f;

        /// <summary>
        /// 生値から倍率へ。範囲外・負の値は帯へクランプする
        /// （スライダーの上限は他 MOD が動かしうるので、読み捨てない）。
        /// </summary>
        public static float ScaleFor(int raw)
        {
            if (raw <= 0) return MinScale;

            float scale = raw / (float)AnchorRaw;
            if (scale < MinScale) return MinScale;
            if (scale > MaxScale) return MaxScale;
            return scale;
        }

        /// <summary>
        /// 倍率から生値へ（スライダーに初期値を入れるときの逆写像）。
        /// <see cref="ScaleFor"/> と往復しても帯の中では値が変わらない。
        /// </summary>
        public static int RawFor(float scale)
        {
            if (float.IsNaN(scale)) return AnchorRaw;
            if (scale < MinScale) scale = MinScale;
            if (scale > MaxScale) scale = MaxScale;

            int raw = (int)(scale * AnchorRaw + 0.5f);
            return raw < 1 ? 1 : raw;
        }

        /// <summary>
        /// メートル値に倍率を掛ける。**NaN と負の入力をそのまま通さない** ——
        /// .cgs は手で編集されうるし、掛け算の結果が NaN になると
        /// 形態ごとのクランプが既定値へ落として、原因が分からなくなる。
        /// </summary>
        public static float Apply(float metres, float scale)
        {
            if (float.IsNaN(metres) || float.IsNaN(scale)) return metres;
            if (metres <= 0f) return metres;

            if (scale < MinScale) scale = MinScale;
            if (scale > MaxScale) scale = MaxScale;
            return metres * scale;
        }
    }
}
