namespace DisasterPlus.Core.FireWhirl
{
    /// <summary>
    /// 「延焼判定は建物を選んでいるのに、1 棟も着火していない」が続いていることの検出。
    ///
    /// なぜ要るか: ③の実装で誤っていた IL 前提のひとつが
    /// 「Building.m_fireIntensity を直接書けば着火する」で、実際は
    /// CommonBuildingAI 派生でないと誰もその値を消費しない。この取り違えは
    /// 例外もログも出さず、延焼が静かに起きないだけだった。
    /// 存在検査（BuildingAI.BurnBuilding が解決できるか）は通ってしまうので、
    /// 「呼んだ結果 1 棟も燃えなかったか」という振る舞いでしか捕まらない。
    ///
    /// エンジン非依存の純粋なカウンタとして Core に置き、ユニットテストで固定する。
    /// </summary>
    public class BarrenSpreadTracker
    {
        /// <summary>
        /// 何回連続で「着火を試みたのに 0 棟」なら異常とみなすか。
        ///
        /// 延焼判定は 16 sim フレームぶんのゲーム内時間ごと（既定で約 0.35 ゲーム内分）に
        /// 走るので、旋風 1 基の寿命（既定 10 分）はおよそ 28 回ぶんに相当する。
        ///
        /// 24 回 ≒ 8.4 ゲーム内分。旋風 1 基が生まれてから消えるまでのあいだ
        /// 「燃やせるはずの建物に一度も火が付かなかった」に相当する。
        ///
        /// 8 回では短すぎた。8 回はゲーム内 2.8 分＝速度 1 の実時間で 3 秒足らずで、
        /// しかも当時は証拠に「バニラが設計上断るもの」（瓦礫・公園・消防署）が
        /// 混ざっていたため、正常な街で必ず踏んだ。証拠側は
        /// FireWhirlDamage.CanBurn で掃除したので、いまや attempted に数えた棟は
        /// バニラが受け付けるはずの棟だけ＝ 1 回の空振りでも十分に異常だが、
        /// 「他 MOD が BurnBuilding を横取りしている」等こちらの知らない事情も
        /// ありうるので、証拠を厚く積んでから名指しする。
        ///
        /// 閾値を伸ばす代償はほぼ無い。検出したい破れ（m_fireIntensity 直接書込の
        /// 再発など）は「1 棟も燃えない」が永久に続くので、何回に設定しても必ず捕まる。
        /// 一方で誤検知は狼少年を作り、この基盤ごと信用されなくなる。
        ///
        /// なお「選ばれた（selected）」ではなく「実際に BurnBuilding を呼んだ（attempted）」
        /// を数えること。IgnitionSpread.Select は既燃の建物を除外するが、選定後に
        /// 燃え出した分は FireWhirlDamage.Ignite 側で弾かれるため、attempted を使わないと
        /// 「周囲が全部すでに燃えている大火災」を異常と誤判定する。
        /// </summary>
        public const int DefaultThreshold = 24;

        private readonly int _threshold;
        private int _streak;
        private bool _tripped;

        public BarrenSpreadTracker() : this(DefaultThreshold) { }

        public BarrenSpreadTracker(int threshold)
        {
            _threshold = threshold < 1 ? 1 : threshold;
        }

        /// <summary>連続して空振りしている回数。</summary>
        public int Streak { get { return _streak; } }

        /// <summary>閾値に達した状態か。Record が着火を観測するまで下りない。</summary>
        public bool Tripped { get { return _tripped; } }

        public int Threshold { get { return _threshold; } }

        /// <summary>
        /// 延焼判定 1 回ぶんの結果を記録する。
        /// </summary>
        /// <param name="attempted">実際に BurnBuilding を呼んだ棟数。</param>
        /// <param name="ignited">着火に成功した棟数。</param>
        /// <returns>この記録でちょうど閾値に達したときだけ true（ログを 1 回だけ出すため）。</returns>
        public bool Record(int attempted, int ignited)
        {
            if (ignited > 0)
            {
                // 動いている。証拠はすべて捨てる。
                _streak = 0;
                _tripped = false;
                return false;
            }

            // 試行が 0 の回は証拠にならない（燃やす対象がそもそも無かっただけ）。
            // リセットもしない。静かな時間を挟んでも証拠は積み上がる。
            if (attempted <= 0) return false;

            _streak++;
            if (_tripped || _streak < _threshold) return false;

            _tripped = true;
            return true;
        }

        /// <summary>レベルアンロード時。都市をまたいで証拠を持ち越さない。</summary>
        public void Reset()
        {
            _streak = 0;
            _tripped = false;
        }
    }
}
