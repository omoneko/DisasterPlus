using System;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Volcano;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 隆起 —— <c>RawHeights</c> の書き込みと分割 <c>UpdateArea</c>、そして山頂の火口。
    /// **sim スレッド専用。⑤が地形を書く唯一の型である。**
    ///
    /// ── ★★ 上げてよい半径は <c>VolcanoClearing.ClearedRadiusMetres</c> だけで決まる ──
    ///
    /// <code>
    /// activeRadius = UpliftSchedule.ActiveRadiusMetres(
    ///                    footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres)
    /// </code>
    ///
    /// **第 2 引数にこれ以外を渡してはいけない**（計画の罠 1）。準備が届いていない場所を
    /// 上げると、道路は <c>Heights.PrimaryLevel</c> でセルを道路の y に、建物は
    /// <c>SecondaryLevel</c> で建物の y に**強制固定**し、しかもそれは毎フラッシュ
    /// ゼロからやり直される（§A-2）。**書き続けても勝てない。** 例外は 1 つも出ず、
    /// 「なんとなく変な山」——山の中の平らな溝とすり鉢——になる（設計書 §1.2）。
    /// <c>ClearedRadiusMetres</c> が 0 なら <c>ActiveRadiusMetres</c> は 0 を返し、
    /// この型は 1 セルも書かない。**Core 側のテストがその 0 を固定している。**
    ///
    /// **レビューの grep**: このファイルの <c>ActiveRadiusMetres</c> の呼び出しは 1 箇所で、
    /// 第 2 引数が <c>VolcanoClearing.ClearedRadiusMetres</c> であること。
    ///
    /// ── 増分ではなく「その時刻における絶対目標」を書く（罠 2）───────────────
    ///
    /// 素朴な <c>raw[i] += step</c> は**必ず無言で壊れる**。<c>RawHeights</c> は
    /// <c>ushort</c> の <c>raw/64</c> メートルで、書き込み側は <c>if (n != raw)</c> で
    /// 省略する（§C-8 IL_01E7）。1 tick の変化がセル高さで 1/64 m = 0.015625 m を
    /// 下回ると**丸めで消え、そのセルは永久に動かない**。外周ほど遅く上がるので、
    /// 素朴な実装では**裾だけが最初から完全停止する**。
    ///
    /// 絶対目標（<c>UpliftSchedule.RawTargetAt</c>）にすると、まだ 1 raw 単位に届かない
    /// セルは「書かれないだけ」で、進捗は <c>progress</c> という float に蓄積されている。
    /// 山頂だけは毎 tick 動かなければならないので、<c>UpliftSchedule.TotalTicksFor</c> が
    /// 要求 tick 数を <c>H×64</c> で切り詰める。**この切り詰めを外さないこと。**
    ///
    /// ── <see cref="_baseRaw"/> は開始時に 1 回だけ控える ───────────────────
    ///
    /// **毎 tick 読み直してはいけない。** 読み直すと⑤が前 tick に書いた値が「元の高さ」に
    /// なり、**プロファイルが毎 tick 積算されて山が天井まで伸びる**。
    /// 実費は影響矩形ぶんの <c>ushort</c>（半径 1 km で 31 KB、2 km で 123 KB、3 km で 279 KB。§E-13）。
    ///
    /// > **<c>TerrainManager.BackupHeights</c> / <c>UndoBuffer</c> を使わない**（§E-13）。
    /// > あれは <c>TerrainTool</c> / <c>DistrictTool</c> の専有物で、<c>TerrainTool.OnEnable</c> が
    /// > <c>RawHeights</c> 全体を複製し直す ——
    /// > **プレイヤーが整地ツールを開いた瞬間に⑤の退避が消える。**
    /// >
    /// > **この配列を「元に戻す機能」の土台だと誤解しないこと。** ⑤は火山を消す機能を
    /// > 持たない（設計書 §1.3 の「不可逆でよい」）。用途は
    /// > **「元の高さからの絶対目標を計算する」ことだけ**で、セーブにも残さない。
    ///
    /// ── タイルは 1 tick に 1 枚（罠 3）──────────────────────────
    ///
    /// <c>UpdateArea</c> は矩形が 128×128 raw セルを超えた分を**タイル分割せずに無言で
    /// 切り捨てる**（§A-1 の <c>Min(m_maxX, m_minX + 120 + 8)</c>）。加えて**単発の要求面積が
    /// 10000 セルを超えると入れ子のバッチを無視して即フラッシュする**。
    /// <c>TileSplit</c> が両方を同時に満たす矩形（99×99 = 9801 セル）を返す。
    ///
    /// **全タイルを 1 tick で呼ばない。** 2 枚目以降は <c>merged &gt; 10000</c> の判定に
    /// 掛かって毎回途中フラッシュする（§A-1 IL_00A8）。タイル数ぶんの tick をかけて
    /// 一周すればよく、隆起は何百 tick も続くので**遅れは目に見えない**。
    /// 継ぎ目に段差は出ない —— <c>TileAt</c> が ±2 セルの重なりを持って返すからである。
    ///
    /// ── 火口は <c>MakeCrater</c> をちょうど 1 回（罠 4）───────────────────
    ///
    /// <c>MakeCrater</c> は先頭で <c>TerrainModify.RefreshAllModifications()</c> を呼ぶ
    /// （§C-8 IL_0006）。これは <c>UpdateAreaImplementation</c> そのものなので、
    /// **呼ぶたびに強制フラッシュが 1 回走り**、その tick にバニラのマネージャが溜めた分まで
    /// 巻き込んで吐き出す。したがって⑤は**隆起の最後に 1 回だけ**呼ぶ。
    /// <see cref="CraterCarved"/> が二度呼びを防ぐ。
    ///
    /// ── 呼んではいけないもの（§D-12）───────────────────────────
    ///
    /// **<c>Begin/EndUpdateArea</c> を⑤が呼んではいけない。**
    /// <c>SimulationManager.SimulationStep</c> が先頭で <c>BeginUpdateArea</c>、末尾で
    /// <c>EndUpdateArea</c> を呼んでおり、MOD の sim tick フックは全部その内側にある。
    /// ⑤が足しても <c>m_modifyingLevel</c> が 1 増えて 1 減るだけで、内側の <c>End</c> は
    /// <c>if (loc1 != 0) return</c>（IL_0017）で握り潰される。害は無いが意味も無く、
    /// 「バッチしているつもり」という誤解だけが残る。
    /// **レビューの grep**（★ 実際に走らせて件数を合わせてある。全体レビュー M12 ——
    /// 素で走らせると**この規則の文そのもの**が 4 件引っかかり、
    /// 「0 件」という手順が最初から成立していなかった。コメント行を落とすこと）:
    ///
    /// <code>
    /// grep -rn --include=*.cs -E "BeginUpdateArea|EndUpdateArea" src/DisasterPlus/ \
    ///   | grep -vE ':[0-9]+: *//' | wc -l          # -> 0
    /// </code>
    ///
    /// <c>grep -v '///'</c> では足りない —— <c>//</c> 1 本のコメントも落とす必要がある。
    ///
    /// **main スレッドから <c>UpdateArea</c> を呼ぶのが本当に危ないほう**である。
    /// <c>m_modifyingLevel == 0</c> なので毎回フラッシュし、sim スレッドが溜めている
    /// 途中の蓄積と競合する。⑤の地形書き込みは全部この型（sim スレッド）に置く。
    ///
    /// ── 「建てられる地面」と水位は遅れる。これは不具合ではない（設計書 §7.3）─────
    ///
    /// <c>m_blockHeights</c> はゲームモードで上へ 2 m / 64 sim フレームしか動かず（§A-2）、
    /// <c>WaterSimulation.m_heightBuffer</c> は**その配列そのもの**である（§A-4）。
    /// 見た目（<c>m_finalHeights</c> / <c>m_detailHeights</c>）は即座に変わるが、
    /// 600 m の山なら 300 × 64 = 19200 sim フレーム ≒ 7 ゲーム内時間かけて追いつく。
    /// **隆起をこの速度に合わせて遅くしない** —— 合わせても <c>m_blockHeights</c> は
    /// 64 フレームに 1 回しか動かないので意味が無い。見積りをパネルと診断に出す。
    ///
    /// ── 1 tick あたりの仕事量の上限（明示する）──────────────────────
    ///
    /// 間隔は <see cref="IntervalFrames"/> フレームぶんの**経過ゲーム内時間**
    /// （<c>frameIndex % N</c> にしない。火災旋風 付録 A-4）。毎 sim tick にしないのは
    /// §A-3 のフィードバックループのためで、<c>m_flattenTerrain == false</c> の建物が
    /// 動くたび <c>BuildingManager.SimulationStepImpl</c> が追加の <c>UpdateArea</c> を出す。
    /// 準備段がフットプリント内の建物を取り除いているので残るのは⑤が壊せなかった建物だけだが、
    /// その規模は <c>VolcanoClearing.LastBuildingsRefused</c> がそのまま示す（診断に出す）。
    ///
    /// 1 tick の上限は<b>影響矩形のセル数ぶんの配列書き込み 1 回</b>と
    /// <b><c>UpdateArea</c> ちょうど 1 回（99×99 = 9801 セル）</b>。
    /// 矩形は半径 R で <c>(2R/16 + 3)²</c> セル —— 既定の成層火山（R=1200 m）で 153² ≒ 23,409、
    /// 形態の最大（R=3000 m）でも 378² ≒ 142,884 である。
    /// <c>RawHeights</c> への書き込みは**ただの配列書き込み**で、
    /// <c>UpdateArea</c> を呼ぶまで誰も読まない（§D-12）。
    /// </summary>
    public static class VolcanoUplift
    {
        /// <summary>隆起の間隔（フレーム相当のゲーム内時間）。**毎 sim tick にしない**（§A-3）。</summary>
        private const int IntervalFrames = 16;

        /// <summary><c>RawHeights</c> の 1 行のセル数（1081²、§C-8）。</summary>
        private const int RawStride = 1081;

        /// <summary>期待する <c>RawHeights</c> の長さ。合わなければ 1 セルも書かない。</summary>
        private const int RawLength = RawStride * RawStride;

        /// <summary>
        /// 開始時に控えた「元の高さ」。**毎 tick 読み直さない**（クラス doc）。
        /// 火口を彫ったら捨てる。
        /// </summary>
        private static ushort[] _baseRaw;

        private static int _minX, _minZ, _maxX, _maxZ;
        private static int _width;

        private static int _tileCount;
        private static int _tileCursor;

        private static int _tick;
        private static int _totalTicks;

        /// <summary>
        /// 目標に届いたあと、火口を彫るまでに残っているタイルの枚数。
        /// <c>-1</c> は「まだ届いていない」。**この待ちを外すと山の外側が
        /// 1 tick ぶん低いまま固まる**（<see cref="Step"/> の該当箇所）。
        /// </summary>
        private static int _finalFlushLeft = -1;

        private static float _progress;
        private static float _activeRadius;
        private static float _summitMetres;
        private static int _cellsWrittenLastTick;

        private static bool _started;
        private static bool _complete;
        private static bool _craterCarved;

        private static Vec3 _centre;
        private static float _minutesSinceTick;

        private static string _lastFailure;
        private static bool _errorLogged;

        /// <summary>隆起の進捗 [0,1]。**準備が届いていない間は進まない。**</summary>
        public static float ProgressUnit { get { return _progress; } }

        /// <summary>
        /// 今この tick に上げてよい半径（m）。**＝準備が届いた範囲**（クラス doc）。
        /// パネルはこれに「準備が届いた範囲」と添えて出す —— 罠 1 を実機で
        /// 目で確かめられる唯一の行である。
        /// </summary>
        public static float ActiveRadiusMetres { get { return _activeRadius; } }

        /// <summary>今の山頂の盛り上がり（m）。元の地形高さからの相対量である。</summary>
        public static float SummitMetres { get { return _summitMetres; } }

        /// <summary>隆起が終わったか（火口も彫り終えている）。</summary>
        public static bool Complete { get { return _complete; } }

        /// <summary>山頂の火口を彫ったか。**二度彫らないための旗**（罠 4）。</summary>
        public static bool CraterCarved { get { return _craterCarved; } }

        /// <summary>これまでに進んだ tick 数。</summary>
        public static int Ticks { get { return _tick; } }

        /// <summary>隆起に使う tick 数（<c>UpliftSchedule.TotalTicksFor</c> で切り詰め済み）。</summary>
        public static int TotalTicks { get { return _totalTicks; } }

        /// <summary>直近 1 tick で実際に値が変わったセル数（0 なら丸めで消えている）。</summary>
        public static int CellsWrittenLastTick { get { return _cellsWrittenLastTick; } }

        /// <summary>影響矩形を覆うタイル数（<c>TileSplit</c>）。</summary>
        public static int TileCount { get { return _tileCount; } }

        /// <summary>次に <c>UpdateArea</c> するタイルの番号。</summary>
        public static int TileCursor { get { return _tileCursor; } }

        /// <summary>
        /// 直近に上げられなかった理由（**英語・診断用**）。上げられていれば null。
        /// **黙って何もしないをやらない**ための口である。
        /// </summary>
        public static string LastFailure { get { return _lastFailure; } }

        /// <summary>
        /// 火山を手放すときとレベルアンロードで呼ぶ。冪等である。
        /// **既に変わった地形は戻らない**（不可逆・設計書 §1.3）。畳むのは予定だけである。
        /// </summary>
        public static void Reset()
        {
            // ★ 退避配列は必ず捨てる。半径 3 km で 279 KB あり、都市をまたいで
            //    持ち越すと前の都市の地形を「元の高さ」として名乗ることになる。
            _baseRaw = null;
            _minX = 0;
            _minZ = 0;
            _maxX = 0;
            _maxZ = 0;
            _width = 0;
            _tileCount = 0;
            _tileCursor = 0;
            _tick = 0;
            _totalTicks = 0;
            _finalFlushLeft = -1;
            _progress = 0f;
            _activeRadius = 0f;
            _summitMetres = 0f;
            _cellsWrittenLastTick = 0;
            _started = false;
            _complete = false;
            _craterCarved = false;
            _centre = new Vec3(0f, 0f, 0f);
            _minutesSinceTick = 0f;
            _lastFailure = null;

            // _errorLogged は戻さない（この DLL が参照しているゲームのビルドに対する事実）。
        }

        /// <summary>
        /// sim スレッド。**必ず <c>VolcanoFeature.OnSimulationTick</c> のポーズガードより
        /// 下から呼ぶこと**（ポーズ中に山が育つ）。<paramref name="frame"/> は診断用で、
        /// **周期の判定には使わない**（<c>frameIndex % N</c> にしない）。
        /// </summary>
        public static void Tick(VolcanoFootprint footprint, uint frame, float deltaMinutes)
        {
            try
            {
                Step(footprint, deltaMinutes);
                WriteDiag(frame);
            }
            catch (Exception e)
            {
                _lastFailure = "the uplift tick threw " + e.GetType().Name;
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Log.Error("volcano uplift failed", e);
                }
                else
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                             "volcano uplift failed: " + e.GetType().Name);
                }
            }
        }

        private static void Step(VolcanoFootprint footprint, float deltaMinutes)
        {
            if (!footprint.Valid) return;

            if (!_started || !SamePoint(_centre, footprint.Centre))
            {
                if (!Start(footprint)) return;
            }

            if (_complete) return;

            // ★ 間隔の累積は対象より先に進める（④の TyphoonWind と同じ形）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            float interval = framesPerMinute > 0f ? IntervalFrames / framesPerMinute : 0f;

            if (deltaMinutes > 0f) _minutesSinceTick += deltaMinutes;
            if (interval > 0f && _minutesSinceTick > interval) _minutesSinceTick = interval;

            if (framesPerMinute <= 0f) return;
            if (_minutesSinceTick < interval) return;

            // ★★ **罠 1。第 2 引数はこれ以外を渡してはいけない。**
            _activeRadius = UpliftSchedule.ActiveRadiusMetres(
                footprint.RadiusMetres, VolcanoClearing.ClearedRadiusMetres);

            // 準備が 1 mm も届いていないなら、tick も進めない。進めると
            // 「上げていないのに進捗だけ終わる」——山が育たないまま完成する。
            // **累積は消費しない**ので、準備が届いた次の tick で即座に走る。
            if (!(_activeRadius > 0f)) return;

            _minutesSinceTick = 0f;

            _progress = UpliftSchedule.ProgressAt(_tick, _totalTicks);
            _summitMetres = footprint.HeightMetres * _progress;

            if (!WriteHeights(footprint)) return;
            FlushOneTile();

            if (_tick < _totalTicks)
            {
                _tick++;
                return;
            }

            // ★★ **準備が外周まで届くまでは終わらせない。**
            //    書いたのは activeRadius の内側だけなので、ここで打ち切ると
            //    activeRadius と R のあいだの帯が**元の高さのまま残る**——
            //    山の外側に環状の段差ができる。準備の走査は隆起より間隔が長い
            //    （64 対 16 フレーム）ので、この待ちは普通に発生する。
            //    進捗はもう 1 なので、待っている間の WriteHeights は
            //    「準備が届いた分だけ」を毎 tick 埋め足していく。
            if (_activeRadius < footprint.RadiusMetres - VolcanoShape.MetresPerRawUnit) return;

            // ★★ **目標の高さは書き終えたが、まだ全部は見えていない。**
            //    1 tick に流せるタイルは 1 枚なので（罠 3）、最後の書き込みが
            //    画面に出ているのは 1 枚ぶんだけで、残りのタイルは 1 tick 前の高さ
            //    ——山頂の 1/_totalTicks ぶん低い形——のまま止まっている。
            //    **そこで火口を彫って終わると、山の外側が永久に低いまま残る。**
            //    全タイルをここでまとめて呼ぶと merged &gt; 10000 の判定で毎回
            //    途中フラッシュするので（§A-1 IL_00A8）、**1 tick 1 枚のまま
            //    残りを流し切ってから**火口へ進む。
            //    この間の WriteHeights は progress = 1 のままなので 0 セルしか書かない。
            if (_finalFlushLeft < 0) _finalFlushLeft = _tileCount - 1;
            if (_finalFlushLeft > 0)
            {
                _finalFlushLeft--;
                return;
            }

            // ★ 最後の 1 回。火口はここでしか彫らない（罠 4）。
            CarveCrater(footprint);
            _progress = 1f;
            _summitMetres = footprint.HeightMetres;
            _complete = true;

            // もう使わない。メモリを返す（クラス doc の実費表）。
            _baseRaw = null;
        }

        /// <summary>
        /// 影響矩形を決めて「元の高さ」を控える。**開始時に 1 回だけ**（クラス doc）。
        /// 失敗したら <see cref="_lastFailure"/> を残して false を返す。
        /// </summary>
        private static bool Start(VolcanoFootprint footprint)
        {
            Reset();

            ushort[] raw = ReadRawHeights();
            if (raw == null) return false;

            if (!TileSplit.CellRangeFor(footprint.Centre.X, footprint.Centre.Z,
                                        footprint.RadiusMetres,
                                        out _minX, out _minZ, out _maxX, out _maxZ))
            {
                _lastFailure = "the volcano footprint does not cover a single terrain cell";
                return false;
            }

            _width = _maxX - _minX + 1;
            int height = _maxZ - _minZ + 1;
            _baseRaw = new ushort[_width * height];

            for (int z = 0; z < height; z++)
            {
                int source = (_minZ + z) * RawStride + _minX;
                Array.Copy(raw, source, _baseRaw, z * _width, _width);
            }

            _tileCount = TileSplit.TileCountFor(_minX, _minZ, _maxX, _maxZ);
            _tileCursor = 0;

            // ★ 山頂が毎 tick 1 raw 単位以上動くよう切り詰める（罠 2）。
            //   換算は FeatureHost.FramesPerMinute から出す（定数を直書きしない）。
            float framesPerMinute = FeatureHost.FramesPerMinute;
            int requestedTicks = framesPerMinute > 0f
                ? (int)(ModSettings.VolcanoUpliftMinutes.value * framesPerMinute / IntervalFrames)
                : 1;
            _totalTicks = UpliftSchedule.TotalTicksFor(footprint.HeightMetres, requestedTicks);

            _centre = footprint.Centre;
            _started = true;
            _lastFailure = null;

            Log.Info("volcano uplift started: rect " + _width + "x" + height
                     + " cells, " + _tileCount + " tiles, " + _totalTicks + " ticks");
            return true;
        }

        /// <summary>
        /// 準備が届いた範囲の全セルに、**その時刻における絶対目標**を書く（罠 2）。
        ///
        /// **書き込みだけで <c>UpdateArea</c> は呼ばない。** 書いた人が <c>UpdateArea</c> を
        /// 呼ぶまで誰も読まないのが地形の規律で（§D-12）、こちらは全域へ先に行き、
        /// 表示がタイル 1 枚ずつ追いつく形になる。
        /// </summary>
        private static bool WriteHeights(VolcanoFootprint footprint)
        {
            ushort[] raw = ReadRawHeights();
            if (raw == null || _baseRaw == null) return false;

            float centreX = footprint.Centre.X;
            float centreZ = footprint.Centre.Z;
            float activeSquared = _activeRadius * _activeRadius;
            float radius = footprint.RadiusMetres;
            float height = footprint.HeightMetres;
            VolcanoForm form = footprint.Form;

            int written = 0;

            for (int z = _minZ; z <= _maxZ; z++)
            {
                float worldZ = (z - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                float dz = worldZ - centreZ;
                float dz2 = dz * dz;
                if (dz2 > activeSquared) continue;

                int rowRaw = z * RawStride;
                int rowBase = (z - _minZ) * _width;

                for (int x = _minX; x <= _maxX; x++)
                {
                    float worldX = (x - TileSplit.CellOffset) * TileSplit.RawCellSizeMetres;
                    float dx = worldX - centreX;
                    float d2 = dx * dx + dz2;
                    if (d2 > activeSquared) continue;

                    float d = (float)Math.Sqrt(d2);
                    float profile = VolcanoShape.ProfileAt(form, d, radius, height);

                    ushort target = UpliftSchedule.RawTargetAt(
                        _baseRaw[rowBase + (x - _minX)], profile, _progress);

                    int index = rowRaw + x;
                    // ★ バニラの MakeCrater と同じ「変わったときだけ書く」（§C-8 IL_01E7）。
                    if (raw[index] == target) continue;

                    raw[index] = target;
                    written++;
                }
            }

            _cellsWrittenLastTick = written;
            return true;
        }

        /// <summary>
        /// タイルを 1 枚だけ <c>UpdateArea</c> する（罠 3）。
        ///
        /// **<c>TileAt</c> が返すのは「そのまま渡す矩形」である。** ここで margin を
        /// 足し直してはいけない —— 足すと 103×103 = 10609 セルになり、10000 の閾値を
        /// 跨いで毎回フラッシュする（<c>TileSplit</c> のクラス doc）。
        ///
        /// <c>surface</c> / <c>zones</c> を false にしてあるのはバニラの <c>MakeCrater</c> /
        /// <c>MakeCrack</c> と同じ引数だからである（§C-8 IL_021C）。
        /// </summary>
        private static void FlushOneTile()
        {
            if (_tileCount <= 0) return;

            int tMinX, tMinZ, tMaxX, tMaxZ;
            if (!TileSplit.TileAt(_tileCursor, _minX, _minZ, _maxX, _maxZ,
                                  out tMinX, out tMinZ, out tMaxX, out tMaxZ))
            {
                _tileCursor = 0;
                return;
            }

            TerrainModify.UpdateArea(tMinX, tMinZ, tMaxX, tMaxZ, true, false, false);

            _tileCursor++;
            if (_tileCursor >= _tileCount) _tileCursor = 0;
        }

        /// <summary>
        /// 山頂の火口。**⑤全体で <c>MakeCrater</c> を呼ぶのはこの 1 箇所・1 回だけ**（罠 4）。
        ///
        /// <c>raiseEdges: true</c> は「深さ 0.7×depth の穴」＋「0.75r に高さ 0.3×depth の
        /// 環状の縁」（§C-8 の実測表）。火口そのものである。
        /// <c>VolcanoShape.HeightFor</c> がこの縁の分（<c>CraterRimHeadroomOf</c>）を
        /// 先に天井から引いてあるので、天井ぎりぎりの山でも縁だけが切られて円環が
        /// 平らになることはない（§C-10）。
        ///
        /// 火口の半径は 400 m 以下（<c>CraterRadiusOf</c> がクランプ）なので、
        /// <c>MakeCrater</c> が自分で出す矩形は最大 53 セル角 = 2809 セルに収まり、
        /// 128 セルと 10000 セルの両方の閾値の内側である。
        /// </summary>
        private static void CarveCrater(VolcanoFootprint footprint)
        {
            if (_craterCarved) return;
            _craterCarved = true;

            float craterRadius = VolcanoShape.CraterRadiusOf(footprint.RadiusMetres);
            float craterDepth = VolcanoShape.CraterDepthOf(footprint.HeightMetres);
            if (!(craterRadius > 0f) || !(craterDepth > 0f)) return;

            DisasterHelpers.MakeCrater(
                new Vector2(footprint.Centre.X, footprint.Centre.Z),
                craterRadius, craterDepth, true);

            Log.Info("volcano summit crater carved: r=" + craterRadius.ToString("F0")
                     + " m, depth=" + craterDepth.ToString("F0") + " m");
        }

        /// <summary>
        /// <c>RawHeights</c> を取る。**長さが 1081² でなければ 1 セルも書かない** ——
        /// <c>z*1081 + x</c> の添字が別のセルを指し、**マップの無関係な場所が隆起する**
        /// （<see cref="VolcanoTerrainFacts.Usable"/> と同じ述語）。
        ///
        /// <c>Singleton&lt;T&gt;.exists</c> を先に見る（<c>instance</c> は main スレッド専用 API）。
        /// </summary>
        private static ushort[] ReadRawHeights()
        {
            if (!Singleton<TerrainManager>.exists)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            var tm = Singleton<TerrainManager>.instance;
            if (tm == null)
            {
                _lastFailure = "TerrainManager is not available";
                return null;
            }

            ushort[] raw = tm.RawHeights;
            if (raw == null || raw.Length != RawLength)
            {
                _lastFailure = "RawHeights is not a ushort[1081^2]; nothing was raised";
                return null;
            }

            return raw;
        }

        /// <summary>1/64 m（raw 1 単位）より細かい差は「同じ地点」とみなす。</summary>
        private static bool SamePoint(Vec3 a, Vec3 b)
        {
            return Same(a.X, b.X) && Same(a.Z, b.Z);
        }

        private static bool Same(float a, float b)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d < VolcanoShape.MetresPerRawUnit;
        }

        /// <summary>
        /// **セルを 1 つも書かなかった tick も出す。** 「機能が死んでいる」と
        /// 「もう目標に届いている」がログ上で区別できなくなる。
        /// 引数の文字列連結は毎回走るので <c>DiagEnabled</c> で先に落とす。
        /// </summary>
        private static void WriteDiag(uint frame)
        {
            if (!Log.DiagEnabled(DisasterPlus.Core.Diagnostics.LogChannel.Volcano)) return;

            Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Volcano, "VolcUplift",
                "uplift frame=" + frame
                + " tick " + _tick + "/" + _totalTicks
                + " progress=" + _progress.ToString("F3")
                + " summit=" + _summitMetres.ToString("F1")
                + " active=" + _activeRadius.ToString("F0")
                + " cells=" + _cellsWrittenLastTick
                + " tile=" + _tileCursor + "/" + _tileCount
                + (_craterCarved ? " crater=carved" : "")
                + (_complete ? " (complete)" : ""));
        }
    }
}
