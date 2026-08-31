using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **第 2 層の節 —— この MOD が発明した挙動だけを置く場所。** main スレッド専用。
    ///
    /// ── ここが第 1 層と混ざったら、この機能は存在価値を失う ────────────────
    ///
    /// 第 1 層（タブの中身）は全て「バニラ自身の式と定数から導いた量」で、
    /// <c>[measured]</c> の接頭辞が付く。この節に出るものは 1 つも実測ではない ——
    /// バニラは海中の地震から津波を起こさないし、建物の高さを揺れにも被害にも
    /// 使っていない（§A-7 / §A-3）。したがって:
    ///
    ///   - 行は必ず <see cref="EarthquakeRows.AddLayer2Row(UIPanel,string,ref float)"/> で作る
    ///     （中身は <see cref="EarthquakeRows.SetLayer2"/> でしか書けず、
    ///     <c>Strings.SourceModel</c> が必ず頭に付く）
    ///   - 節の見出し（<c>EarthquakeLayer2Header</c>）が「Disaster + が足した挙動
    ///     （バニラにはありません）」と名乗る
    ///   - 色も第 1 層と変える（ただし**色だけには頼らない**）
    ///
    /// **この節はタブの外にある。** 計画の共通規則「第 2 層は第 1 層の下に構築し、
    /// 実行時の並べ替えはしない」を、タブ化したあとも守るため
    /// （<see cref="EarthquakePanelTabs"/> の doc）。どのタブを見ていても、
    /// 本 MOD が足した挙動は常に同じ場所に、第 1 層の全内容より下に見えている。
    ///
    /// ── 節はブロック単位で畳む（縦の予算は本当に無い）──────────────────
    ///
    /// 第 2 層は**独立した機能が複数**ある（津波連鎖 / 長周期地震動）。全部の行を
    /// 常に確保すると、どれも使っていないプレイヤーのパネルが 150 px 以上高くなり、
    /// <see cref="EarthquakePanel.ClampToView"/> が「ビューより高い」と警告する側へ
    /// 一歩近づく。そこで**設定が ON のブロックの行だけを積む**。
    ///
    /// 位置の組み直し（<see cref="Layout"/>）が走るのは
    /// **構築時と、ON/OFF の組み合わせが変わったフレームだけ**である。毎フレーム
    /// レイアウトを回すと、<see cref="EarthquakePanel.Relayout"/> が
    /// <c>relativePosition</c> を書き換えるためパネルが微妙に動き続ける。
    ///
    /// ── 文言（できていないことをできているように書かない） ──────────────
    ///
    /// ★★ **2026-08-29 に書き換えた。** DLC の <c>TsunamiAI</c> はいまも
    /// マップ外周からしか波を出せないが、**こちらはもう TsunamiAI を使っていない。**
    /// 同じソルバの <c>TYPE_IMPACT</c>（どこにでも置ける外力）を震源に置くので、
    /// <c>EarthquakeTsunamiFromShore</c> は「波は震源から同心円状に広がります」と書く。
    /// 原則は変わっていない ——「できていないことをできているように書かない」。
    /// できるようになったから書き換えたのであって、宣伝したわけではない
    /// （docs/superpowers/specs/2026-08-29-tsunami-il-facts.md）。
    ///
    /// 長周期地震動の側は <c>EarthquakeLongPeriodNote</c> が
    /// 「バニラは揺れにも被害にも建物の高さを一切使っていません」と名乗る。
    /// この 1 文が無いと、プレイヤーは「高層ほど揺れる」をゲームの仕様だと思う。
    /// </summary>
    internal static class EarthquakeLayer2Rows
    {
        /// <summary>
        /// 津波の節を出すか。
        ///
        /// ★★ **設定だけで決めてはいけない。**（2026-08-30、第 3 回検証）
        ///   <c>EarthquakeTsunamiChain</c> は既定 OFF だが、海溝型地震は
        ///   設定に関係なく津波を連れてくる。設定だけで隠していたので、
        ///   <b>クリックしてから波が来るまでの数分間、画面には何も出なかった</b>。
        ///   待っているあいだ何も出ないのは「壊れている」と読まれる。
        /// </summary>
        private static bool TsunamiRowWanted
        {
            get
            {
                // ★★ **波が走っているあいだは必ず出す。**（2026-08-31、第 4 回検証）
                //    <c>LastId</c> は地震の災害枠が空いた時点で 0 になるが、
                //    波はそのあと 10 分ちかく走り続ける。それだけだと
                //    <b>波が来る前に行が消える</b> —— 待っている人には
                //    「終わった」か「壊れた」にしか見えない。
                return TsunamiRing.Running
                       || ModSettings.EarthquakeTsunamiChain.value
                       || TrenchQuakeSlot.LastId != 0;
            }
        }

        /// <summary>見出し。ブロックが 1 つでも出ていれば出す。</summary>
        private const int BlockHeader = 0;

        /// <summary>津波連鎖（Task 9）。<c>ModSettings.EarthquakeTsunamiChain</c> に従う。</summary>
        private const int BlockTsunami = 1;

        /// <summary>長周期地震動（Task 10）。<c>ModSettings.EarthquakeLongPeriod</c> に従う。</summary>
        private const int BlockLongPeriod = 2;

        /// <summary>1 行ぶんの配置情報。<see cref="Step"/> は行が y を進める量。</summary>
        private struct Layer2Row
        {
            internal UILabel Label;
            internal float Step;
            internal int Block;
        }

        private static readonly List<Layer2Row> _rows = new List<Layer2Row>();

        private static UILabel _headerLabel;
        private static UILabel _tsunamiLabel;
        private static UILabel _tsunamiNoteLabel;
        private static UILabel _longPeriodLabel;
        private static UILabel _timeLabel;
        private static UILabel _timeNoteLabel;

        private static bool _built;
        private static bool _tsunamiVisible;
        private static bool _longPeriodVisible;
        private static float _sectionTop;
        private static float _sectionHeight;

        /// <summary>節が始まる y（＝第 1 層の下端）。パネルの高さの計算に使う。</summary>
        internal static float SectionTop { get { return _sectionTop; } }

        /// <summary>今この節が占めている高さ。全ブロックが畳まれているときは 0。</summary>
        internal static float VisibleHeight { get { return _sectionHeight; } }

        /// <summary>
        /// パネル構築時に 1 回。**行は常に作る**（設定は実行中に変わるので、
        /// あとから作れる仕組みを持つより、作って隠す方が単純で壊れにくい）。
        /// <paramref name="y"/> は、実際に見えているぶんだけ進む。
        /// </summary>
        internal static void Build(UIPanel root, ref float y)
        {
            _sectionTop = y;
            _rows.Clear();

            float t = y;
            float before = t;

            _headerLabel = EarthquakeRows.AddSectionHeader(root, "Layer2Header", ref t,
                Strings.EarthquakeLayer2Header);
            Record(_headerLabel, BlockHeader, t - before);

            // ★ **2 行ぶんの高さを取る（＝折り返させる）。** 予定時刻の 1 行だけなら
            //    1 行で足りるが、同じラベルには「十分な広さの海がマップ外周に無いため…」
            //    （英語で約 110 文字）も入る。折り返さない行に入れると**途中で切れて
            //    消える** —— しかもいちばん切れてほしくない、「これは失敗ではない」を
            //    説明している文である。
            before = t;
            _tsunamiLabel = EarthquakeRows.AddLayer2Row(root, "Tsunami", ref t, 42f);
            Record(_tsunamiLabel, BlockTsunami, t - before);

            // 「波がどこから来るか」の常設の説明。**観測値ではない**ので接頭辞を付けない
            // （Strings.EarthquakeSensorEffect / EarthquakeOverlayLegend と同じ扱い）。
            // 予定・発生のときだけ出す —— 波が来ないと分かっているとき（NoSea / DLC 無し）に
            // 「波は…から到達します」を残すと、来ない波の到達方向を説明することになる。
            before = t;
            _tsunamiNoteLabel = EarthquakeRows.AddPlainRow(root, "TsunamiNote", ref t, "", 56f);
            Record(_tsunamiNoteLabel, BlockTsunami, t - before);

            // 長周期地震動。カーソル直下の建物 1 個について出す。
            //
            // ★ **3〜4 行ぶんの高さを取る。** 本文（接頭辞込みで約 110 文字）だけなら
            //    42f で足りたが、第 2 層レビュー I1 / M3 で末尾に但し書きが付くように
            //    なった（「本震まで適用されません」「走査が上限で打ち切り」）。
            //    折り返さない高さに入れると**途中で切れて消える** —— しかも消えるのは
            //    「この数字はまだ効いていない」という、いちばん切れてほしくない部分である。
            before = t;
            _longPeriodLabel = EarthquakeRows.AddLayer2Row(root, "LongPeriod", ref t, 72f);
            Record(_longPeriodLabel, BlockLongPeriod, t - before);

            // **常設の注記。** これが無いと「高層ほど倒れる」がゲームの仕様に見える。
            before = t;

            // 時間帯係数（Task 11）。**独立した設定は作らない** —— 掛かる先は
            // 長周期の追加被害だけなので、長周期と同じブロックに置いて一緒に出入りさせる。
            before = t;
            _timeLabel = EarthquakeRows.AddLayer2Row(root, "TimeOfDay", ref t);
            Record(_timeLabel, BlockLongPeriod, t - before);

            // 日夜サイクルが切ってあるときだけ出す注記。**黙って係数が 1.00 に
            // なるだけで説明が出ないのは、この MOD が最も嫌う形の出力である**（§F-1）。
            before = t;
            _timeNoteLabel = EarthquakeRows.AddPlainRow(root, "TimeOfDayNote", ref t, "", 40f);
            Record(_timeNoteLabel, BlockLongPeriod, t - before);

            _built = true;
            _tsunamiVisible = TsunamiRowWanted;
            _longPeriodVisible = ModSettings.EarthquakeLongPeriod.value;
            Layout();

            y = _sectionTop + _sectionHeight;
        }

        /// <summary>
        /// 行と、その行が y を進める量を控える。<see cref="Layout"/> はこの順序と
        /// 進み量だけで位置を組み直すので、**行を足す順序 ＝ 画面上の順序**になる。
        /// </summary>
        private static void Record(UILabel label, int block, float step)
        {
            _rows.Add(new Layer2Row { Label = label, Step = step, Block = block });
        }

        /// <summary>レベルアンロード時。参照を捨てるだけ（実体はパネルごと消える）。</summary>
        internal static void Destroy()
        {
            _rows.Clear();
            _headerLabel = null;
            _tsunamiLabel = null;
            _tsunamiNoteLabel = null;
            _longPeriodLabel = null;
            _timeLabel = null;
            _timeNoteLabel = null;
            _built = false;
            _tsunamiVisible = false;
            _longPeriodVisible = false;
            _sectionTop = 0f;
            _sectionHeight = 0f;
        }

        /// <summary>
        /// main スレッドから毎フレーム。<paramref name="snapshot"/> が null または
        /// 無効なら値の行だけを空にする（節そのものは設定で決まる）。
        /// </summary>
        internal static void Refresh(EarthquakeSnapshot snapshot)
        {
            if (!_built) return;

            bool tsunami = TsunamiRowWanted;
            bool longPeriod = ModSettings.EarthquakeLongPeriod.value;
            if (tsunami != _tsunamiVisible || longPeriod != _longPeriodVisible)
            {
                _tsunamiVisible = tsunami;
                _longPeriodVisible = longPeriod;
                Layout();
                // 高さが変わったフレームだけ。毎フレーム位置を書き換えない。
                EarthquakePanel.Relayout();
            }

            bool valid = snapshot != null && snapshot.Valid;

            if (tsunami)
            {
                if (valid) RefreshTsunamiRow(snapshot);
                else
                {
                    EarthquakeRows.SetPlain(_tsunamiLabel, "");
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");
                }
            }

            if (longPeriod)
            {
                string text = valid ? EarthquakeLongPeriodText.CursorRow(snapshot) : null;
                EarthquakeRows.SetPlain(_longPeriodLabel, "");
                if (text != null) EarthquakeRows.SetLayer2(_longPeriodLabel, text);

                // 時間帯の行は**地震が無くても出す**。係数は都市の時計だけで決まり、
                // 地震の有無とは関係が無い。日夜サイクルを切っている人が
                // 「なぜ 1.00 のままなのか」をいつでも確かめられるようにする。
                string time = valid ? EarthquakeLongPeriodText.TimeRow(snapshot) : null;
                EarthquakeRows.SetPlain(_timeLabel, "");
                if (time != null) EarthquakeRows.SetLayer2(_timeLabel, time);
                EarthquakeRows.SetPlain(_timeNoteLabel,
                    EarthquakeLongPeriodText.TimeNote(snapshot));
            }
        }

        /// <summary>
        /// 見えているブロックの行だけを上から詰め直す。
        /// **構築時と ON/OFF が変わったフレームだけ**呼ぶ（クラス doc）。
        /// </summary>
        private static void Layout()
        {
            bool any = _tsunamiVisible || _longPeriodVisible;
            float y = _sectionTop;

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.Label == null) continue;

                bool visible = IsBlockVisible(row.Block, any);
                row.Label.isVisible = visible;
                if (!visible) continue;

                // 位置だけを動かす。**ラベルの生成も .text の代入もここでは行わない**
                // （その 2 つは EarthquakeRows の中だけ、という担保を崩さないため）。
                var p = row.Label.relativePosition;
                row.Label.relativePosition = new Vector3(p.x, y);
                y += row.Step;
            }

            _sectionHeight = y - _sectionTop;
        }

        private static bool IsBlockVisible(int block, bool any)
        {
            if (block == BlockHeader) return any;
            if (block == BlockTsunami) return _tsunamiVisible;
            if (block == BlockLongPeriod) return _longPeriodVisible;
            return false;
        }

        /// <summary>
        /// 状態ごとの 1 行。**「震源から波が広がる」とは絶対に書かない**（クラス doc）。
        ///
        /// <c>NoSea</c> は**失敗ではない**。内陸マップでは海側外周区間が 10 セルに満たず、
        /// 何も起きないのが正常な結果である（§B-3）。だから理由を書いて終わりにする。
        /// <c>Failed</c>（災害スロット満杯など）だけは行を出さない —— あれは
        /// プレイヤーが何かできる状態ではなく、原因は診断ダンプにしか意味がない。
        /// </summary>
        private static void RefreshTsunamiRow(EarthquakeSnapshot snapshot)
        {
            // 注記は「これから波が来る」ときだけ添える（既定は消す）。
            EarthquakeRows.SetPlain(_tsunamiNoteLabel, "");

            switch (snapshot.TsunamiState)
            {
                case TsunamiChainState.Scheduled:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, PendingText(snapshot));
                    return;

                case TsunamiChainState.Raised:
                    EarthquakeRows.SetPlain(_tsunamiNoteLabel, Strings.EarthquakeTsunamiFromShore);
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeTsunamiRaised
                        + RaisedProgress() + "   (#" + snapshot.TsunamiQuakeId + ")");
                    return;

                case TsunamiChainState.NoSea:
                    // ★★ **理由ごとに違う 1 行を出す。**（2026-08-31、相互検証）
                    //    どの理由でも「内陸マップです」と言っていたのは、
                    //    沖の深海で断られた人を正反対の方向へ送っていた。
                    EarthquakeRows.SetLayer2(_tsunamiLabel, TsunamiRefusalText());
                    return;

                case TsunamiChainState.NoDlc:
                    // 新しいキーは増やさない（計画 Step 5）。
                    EarthquakeRows.SetLayer2(_tsunamiLabel, Strings.EarthquakeNeedsDlc);
                    return;

                default:
                    // ★★ **海溝型を起こした直後は、待っていることを名乗る。**
                    //    （2026-08-30、第 4 回検証）
                    //    海溝型は Emerging のあいだ Idle のままで、それが
                    //    <b>クリックから 2 分 17 秒</b>続く。そのあいだ行を空にすると、
                    //    画面には何も出ない —— 待っている人はそれを「壊れている」と読み、
                    //    もう一度クリックする。**空白は答えではない。**
                    if (TrenchQuakeSlot.LastId != 0)
                    {
                        EarthquakeRows.SetPlain(_tsunamiNoteLabel,
                            Strings.EarthquakeTsunamiFromShore);

                        // ★★ **出し終えた波を「到達予定」に戻さない。**
                        //    （第 5 回検証）連鎖が監視をやめても LastId は残るので、
                        //    素直に書くと Raised -> 到達予定 と<b>逆戻りして見える</b>。
                        EarthquakeRows.SetLayer2(_tsunamiLabel,
                            (TsunamiChain.StillOwes(TrenchQuakeSlot.LastId)
                                ? Strings.EarthquakeTsunamiPending
                                : Strings.EarthquakeTsunamiRaised)
                            + RaisedProgress()
                            + "   (#" + TrenchQuakeSlot.LastId + ")");
                        return;
                    }

                    // Idle（陸の震源を含む）と Failed。行を出さない。
                    EarthquakeRows.SetPlain(_tsunamiLabel, "");
                    return;
            }
        }

        /// <summary>断られた理由に合う 1 行（<see cref="TsunamiRing.Refusal"/>）。</summary>
        private static string TsunamiRefusalText()
        {
            switch (TsunamiRing.LastRefusal)
            {
                case TsunamiRing.Refusal.NotEnoughRoom:
                    return Strings.EarthquakeTsunamiNoRoom;

                case TsunamiRing.Refusal.Busy:
                    return Strings.EarthquakeTsunamiBusy;

                case TsunamiRing.Refusal.NotSea:
                default:
                    return Strings.EarthquakeTsunamiNoSea;
            }
        }

        /// <summary>
        /// 波を出しているあいだの<b>動く数字</b>。
        ///
        /// ★★ **静止した 1 行は「壊れている」と読まれる。**（2026-08-31、相互検証）
        ///   発生源は 768 水ステップ＝約 14 実分、そのあと波が着くまでさらに 10 分ほど
        ///   掛かる。そのあいだ画面が「津波発生」のまま一切変わらないと、
        ///   待っている人には止まって見える —— <c>Idle</c> の空白行で一度踏んだのと
        ///   同じ穴である（この下の default 節のコメント）。
        ///
        ///   出すのは<b>いま海面をどれだけ持ち上げているか</b>と<b>進み具合</b>。
        ///   発生源が終わったあとは「波が進行中」であることだけ言う ——
        ///   到達時刻は地形しだいなので、嘘になる数字は出さない。
        /// </summary>
        private static string RaisedProgress()
        {
            if (!TsunamiRing.Running) return "  " + Strings.EarthquakeTsunamiTravelling;

            int total = TsunamiRing.TotalSteps;
            if (total <= 0) return "";

            int done = TsunamiRing.ElapsedSteps;
            if (done > total) done = total;

            // ★★ **残りは実時間で出す。**（2026-08-31、第 4 回検証）
            //    上の行の「◯分後」はゲーム内の分（≒4 実秒）なのに、
            //    実際に水が動くまでは 10 分以上かかる。
            //    1 水ステップ ＝ 64 sim フレーム ≒ 1.07 実秒。
            float leftMinutes = (total - done) * 64f / 3600f;

            return "  " + (done * 100 / total) + "%  "
                   + (TsunamiRing.OffsetMetres >= 0f ? "+" : "")
                   + TsunamiRing.OffsetMetres.ToString("F0") + " m  "
                   + Strings.EarthquakeTsunamiSourceLeft + " "
                   + leftMinutes.ToString("F0") + " " + Strings.EarthquakeRealMinutes;
        }

        /// <summary>
        /// 残り時間。**換算は必ず <see cref="FeatureHost.FramesPerMinute"/> から出す**
        /// （定数を直書きして 4 倍ずれた前科がある）。換算できないときや予定が過ぎている
        /// ときは、数字の代わりに「予定」とだけ言う —— 負の残り時間を出さない。
        /// </summary>
        private static string PendingText(EarthquakeSnapshot snapshot)
        {
            float framesPerMinute = FeatureHost.FramesPerMinute;
            if (framesPerMinute <= 0f || snapshot.TsunamiDueFrame <= snapshot.CurrentFrame)
            {
                return Strings.EarthquakeTsunamiPending + "   (#" + snapshot.TsunamiQuakeId + ")";
            }

            float minutes = (snapshot.TsunamiDueFrame - snapshot.CurrentFrame) / framesPerMinute;
            return Strings.EarthquakeTsunamiPending + ": " + minutes.ToString("F0") + " "
                   + Strings.EarthquakeMinutes + "   (#" + snapshot.TsunamiQuakeId + ")";
        }
    }
}
