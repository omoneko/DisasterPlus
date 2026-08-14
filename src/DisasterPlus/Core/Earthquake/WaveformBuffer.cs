namespace DisasterPlus.Core.Earthquake
{
    /// <summary>
    /// 観測点 1 個ぶんの地動サンプルを貯める固定長リングバッファ。
    ///
    /// ── なぜ MOD が自分で貯めるのか ────────────────────────────
    ///
    /// <c>EarthquakeSensorAI</c> は**時系列データを一切持たない**（IL 事実文書 §C-1、
    /// ABSENT）。フィールドは <c>m_detectionRange</c> だけで、やっているのは毎 tick
    /// 半径内に <c>EarthquakeCoverage</c> という免疫的リソースを撒くことだけである。
    /// 波形も、履歴も、直近の揺れの大きさも、ゲーム側には**何ひとつ存在しない**。
    /// だから「地震計があるのに波形が見られない」への回答は、
    /// **この MOD が自分で観測して自分で貯める**しかない。
    ///
    /// 貯める値そのものは捏造ではない —— <c>ShakeWaveform</c>（＝バニラ自身の
    /// <c>EarthquakeAI.RenderInstance</c> の式、§A-7）を観測点の位置で評価したものである。
    ///
    /// ── 書く側と読む側のスレッドが違う ──────────────────────────
    ///
    /// このクラス自体はスレッド安全ではない。書き込むのは **sim スレッド**
    /// （<c>SeismographRecorder.Sample</c>）、描画するのは **main スレッド**で、
    /// 受け渡しは①から続く snapshot-then-render（<c>EarthquakeHub</c> の単一の
    /// <c>lock</c>）で行う。main スレッドがこのバッファを直接読んではいけない。
    ///
    /// ── 未使用領域を絶対に読まないこと ──────────────────────────
    ///
    /// 配列は容量ぶん確保済みで、まだ書いていない要素は 0 である。**0 は
    /// 「揺れていない」と見分けが付かない。** 未使用領域まで読むと、存在しない
    /// データの上に自信のある直線を引くことになる。読み出しは全て
    /// <see cref="Count"/> で縛り、<see cref="OldestIndex"/> から順に辿る。
    ///
    /// ── セーブには残さない（設計書 §3.5）──────────────────────────
    ///
    /// 地震が終わったら捨てる。セーブ形式を増やす価値が無く、このプロジェクトは
    /// 保存順に起因する不具合で既に一度痛い目を見ている。
    /// </summary>
    public class WaveformBuffer
    {
        private readonly uint[] _frames;
        private readonly float[] _values;

        /// <summary>次に書く位置。</summary>
        private int _head;

        /// <summary>実際に保持している件数（&lt;= <see cref="Capacity"/>）。</summary>
        private int _count;

        /// <summary>
        /// <paramref name="capacity"/> が 1 未満なら 1 に切り上げる。
        /// 0 個の配列を作ると <see cref="Add"/> が必ず落ちる —— sim スレッドの
        /// <c>IndexOutOfRangeException</c> はスタックトレースの無いポップアップになる。
        /// </summary>
        public WaveformBuffer(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _frames = new uint[capacity];
            _values = new float[capacity];
        }

        public int Capacity { get { return _frames.Length; } }

        public int Count { get { return _count; } }

        /// <summary>
        /// 1 サンプル追加する。**1 件あたりの割り当ては無い**（毎 sim tick の経路）。
        /// 満杯なら最も古い 1 件を落とす。
        /// </summary>
        public void Add(uint frame, float value)
        {
            _frames[_head] = frame;
            _values[_head] = value;

            _head++;
            if (_head >= _frames.Length) _head = 0;
            if (_count < _frames.Length) _count++;
        }

        /// <summary>
        /// 中身を捨てる。配列はゼロ埋めしない —— 読み出しは全て
        /// <see cref="Count"/> で縛られているので、残った値は誰からも見えない。
        /// </summary>
        public void Clear()
        {
            _count = 0;
            _head = 0;
        }

        /// <summary>
        /// 古い順に詰め、**実際に書いた件数**を返す。渡された配列が保持件数より
        /// 短ければ短い方に合わせる（呼び出し側が用意した長さを超えて書かない）。
        /// 配列が null なら 0 を返す（例外にしない）。
        /// </summary>
        public int CopyTo(uint[] frames, float[] values)
        {
            if (frames == null || values == null) return 0;

            int max = frames.Length < values.Length ? frames.Length : values.Length;
            if (max > _count) max = _count;

            int start = OldestIndex;
            for (int i = 0; i < max; i++)
            {
                int index = start + i;
                if (index >= _frames.Length) index -= _frames.Length;
                frames[i] = _frames[index];
                values[i] = _values[index];
            }
            return max;
        }

        /// <summary>保持している中で最も古いサンプルのフレーム。空なら 0。</summary>
        public uint OldestFrame
        {
            get { return _count == 0 ? 0u : _frames[OldestIndex]; }
        }

        /// <summary>保持している中で最も新しいサンプルのフレーム。空なら 0。</summary>
        public uint NewestFrame
        {
            get
            {
                if (_count == 0) return 0u;
                int index = _head - 1;
                if (index < 0) index += _frames.Length;
                return _frames[index];
            }
        }

        /// <summary>
        /// **今保持しているサンプルの中の**最大振幅（絶対値）。空なら 0。
        ///
        /// <see cref="Add"/> で最大値を更新して覚えっぱなしにしない。それをやると、
        /// もうバッファから落ちたサンプルの振幅を「今の最大振幅」として出し続ける
        /// ことになる —— 表示している波形の中に存在しない値である。
        /// NaN は比較が全て false になるので自然に無視される。
        /// </summary>
        public float PeakAbsolute
        {
            get
            {
                float peak = 0f;
                int start = OldestIndex;
                for (int i = 0; i < _count; i++)
                {
                    int index = start + i;
                    if (index >= _frames.Length) index -= _frames.Length;

                    float v = _values[index];
                    if (v < 0f) v = -v;
                    if (v > peak) peak = v;
                }
                return peak;
            }
        }

        /// <summary>
        /// 最も古いサンプルの添字。**満杯になるまでは一度も折り返していない**ので
        /// 常に 0 で、満杯になって以降は次に書く位置がそのまま最古になる。
        /// </summary>
        private int OldestIndex
        {
            get { return _count < _frames.Length ? 0 : _head; }
        }
    }
}
