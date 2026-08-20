namespace DisasterPlus.Core.Volcano
{
    /// <summary>
    /// **どの矩形を、いつ <c>UpdateArea</c> へ渡すか**を決める（呼ぶのは呼び出し側）。
    ///
    /// ── なぜ型として切り出したか ────────────────────────────────
    ///
    /// 実機の指摘⑤「噴火のアニメーションをもっとスムーズに（現在は断続的な
    /// せり上がりです）」の正体は、**目に見える 1 段が「1 tick の上昇量」ではなく
    /// 「そのタイルが再び流されるまでの上昇量」だった**ことである。
    /// フットプリント全体（既定 R=1200 m で 151×151 セル）を 4 枚に割って
    /// 1 tick 1 枚の総当たりで流していたので、1 枚が流れ直すまで 4 tick かかり、
    /// 見える 1 段は 1 tick の 4 倍になっていた。
    ///
    /// この判断は整数演算だけでできるので Core に置く。**ユニットテストと
    /// <c>tools/VolcanoPreview</c> の両方が、ゲームを起動せずに同じ物を回せる。**
    /// 「見た目の変更は自分でオフラインに描画・計測してから実機テストを頼む」
    /// というこのプロジェクトの決まりが、この分離を要求している。
    ///
    /// ── 何をしているか ──────────────────────────────────
    ///
    ///   1. その tick に**実際に書き換えたセルの外接矩形**を受け取る。
    ///   2. まだ流し切っていない矩形へ union で足す（<b>足す</b>。上書きしない ——
    ///      上書きすると、総当たりの途中で矩形が縮んだときに
    ///      **まだ流していないタイルが消える**）。
    ///   3. 1 回で出せるなら（<see cref="TileSplit.FitsSinglePass"/>）そのまま出して
    ///      溜まりを空にする。隆起の前半はここを通るので、**変わった全域が
    ///      毎 tick 画面に出る**。
    ///   4. 出せないならタイルを 1 枚ずつ総当たりし、一周したところで空にする。
    ///
    /// **1 回の呼び出しで返す矩形はちょうど 1 つ**（罠 3。2 枚目以降は
    /// <c>merged &gt; 10000</c> の判定で毎回途中フラッシュする、§A-1 IL_00A8）。
    /// 返す矩形は <see cref="TileSplit"/> が両方の閾値
    /// （一辺 128 未満・面積 10000 未満）を守ったもので、
    /// **呼び出し側で margin を足し直してはいけない。**
    ///
    /// <see cref="HasPending"/> が false になった時点で「書いた分は全部画面に出た」。
    /// 火口を彫ってよいかの判定にそのまま使える —— **枚数を数えて待たないこと**。
    /// 数えると、待っている間に届いた最後の書き込みを取りこぼす。
    ///
    /// スレッド安全ではない。sim スレッドからだけ触ること。
    /// </summary>
    public class UpliftFlushPlan
    {
        private int _minX, _minZ, _maxX, _maxZ;
        private bool _hasPending;
        private int _tileCount;
        private int _cursor;

        /// <summary>まだ画面に出ていない書き換えが残っているか。</summary>
        public bool HasPending { get { return _hasPending; } }

        /// <summary>
        /// 溜まっている矩形を出し切るのに要る <c>UpdateArea</c> の回数。
        /// **1 なら「変わった全域が 1 tick で画面に出る」＝ いちばん滑らかな状態。**
        /// 溜まりが無いときも 1 を返す（「次に何か変われば 1 回で出せる」）。
        /// </summary>
        public int TileCount { get { return _hasPending ? _tileCount : 1; } }

        /// <summary>総当たりの何枚目か。単発で出せているときは 0。</summary>
        public int Cursor { get { return _hasPending ? _cursor : 0; } }

        /// <summary>溜まりを捨てる。**地形は戻らない**（畳むのは予定だけ）。</summary>
        public void Reset()
        {
            _hasPending = false;
            _tileCount = 0;
            _cursor = 0;
            _minX = 0;
            _minZ = 0;
            _maxX = 0;
            _maxZ = 0;
        }

        /// <summary>
        /// この tick に書き換えた矩形を足し、**今回 <c>UpdateArea</c> へ渡す矩形**を返す。
        /// 出すものが無ければ false（そのとき out はすべて 0）。
        ///
        /// <paramref name="dirtyValid"/> が false なら「この tick は 1 セルも変わらなかった」。
        /// それでも溜まりが残っていれば流し続ける —— 目標に届いたあと、
        /// まだ画面に出ていない分を出し切るのがこの経路である。
        /// </summary>
        public bool Next(bool dirtyValid, int dirtyMinX, int dirtyMinZ, int dirtyMaxX, int dirtyMaxZ,
                         out int passMinX, out int passMinZ, out int passMaxX, out int passMaxZ)
        {
            passMinX = 0;
            passMinZ = 0;
            passMaxX = 0;
            passMaxZ = 0;

            if (dirtyValid && dirtyMinX <= dirtyMaxX && dirtyMinZ <= dirtyMaxZ)
            {
                if (!_hasPending)
                {
                    _hasPending = true;
                    _minX = dirtyMinX;
                    _minZ = dirtyMinZ;
                    _maxX = dirtyMaxX;
                    _maxZ = dirtyMaxZ;
                    _cursor = 0;
                }
                else
                {
                    if (dirtyMinX < _minX) _minX = dirtyMinX;
                    if (dirtyMinZ < _minZ) _minZ = dirtyMinZ;
                    if (dirtyMaxX > _maxX) _maxX = dirtyMaxX;
                    if (dirtyMaxZ > _maxZ) _maxZ = dirtyMaxZ;
                }

                _tileCount = TileSplit.TileCountFor(_minX, _minZ, _maxX, _maxZ);
                if (_cursor >= _tileCount) _cursor = 0;
            }

            if (!_hasPending) return false;

            // ★ 1 回で出せるなら分割しない。**「だいたい入る」で省かないこと** ——
            //   はみ出した分は例外にならず、更新されないまま残る（§A-1）。
            if (TileSplit.FitsSinglePass(_minX, _minZ, _maxX, _maxZ))
            {
                bool ok = TileSplit.ExpandForPass(_minX, _minZ, _maxX, _maxZ,
                                                  out passMinX, out passMinZ,
                                                  out passMaxX, out passMaxZ);
                Reset();
                return ok;
            }

            if (!TileSplit.TileAt(_cursor, _minX, _minZ, _maxX, _maxZ,
                                  out passMinX, out passMinZ, out passMaxX, out passMaxZ))
            {
                // 番号が範囲外。次の tick で 0 から引き直す（1 tick 遅れるだけで、
                // 溜まりは捨てない —— 捨てると出していない範囲が永久に残る）。
                _cursor = 0;
                return false;
            }

            _cursor++;
            if (_cursor >= _tileCount)
            {
                // 一周した ＝ 溜まっていた範囲は全部画面に出た。
                _hasPending = false;
                _tileCount = 0;
                _cursor = 0;
            }
            return true;
        }
    }
}
