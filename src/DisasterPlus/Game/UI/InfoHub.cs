using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **画面左上のボタン 1 個と、その下に開くタブ帯 1 本。** main スレッド専用。
    /// ①予報 ②地震 ④台風 ⑤火山 と診断の「読む側」は、全部この 1 か所から開く。
    ///
    /// ── 依頼 ─────────────────────────────────────────
    ///
    /// 「情報画面はサイレン MOD や CS:WARFRONT と同様に左上のショートカット
    /// ボタンから開けるようにしてください」——所有者。
    ///
    /// ── ★★ ボタンは 1 個である（4 個にしないこと） ────────────────
    ///
    /// 以前この MOD は①②④⑤それぞれに浮遊ボタンを持っていて、4 個とも
    /// <see cref="FreeSlotFinder"/> に同じ preferred 座標から空きを問い合わせていた。
    /// 初回の実機テストの output_log.txt はその結果をそのまま記録している ——
    /// <c>no free UI slot found after 30 tries</c> が 4 回出て、4 個が (8,50) に
    /// 積み上がった。**探索が誤っていたのではなく、探索する主体が 4 つあったことが
    /// 誤りだった**（<see cref="FreeSlotFinder"/> のクラス doc）。
    ///
    /// だからここでも <see cref="FreeSlotFinder"/> を呼ぶのは **1 回だけ**、
    /// **ボタン 1 個ぶんについてだけ**である。タブ帯とパネルの位置はそこからの
    /// 相対で決まる —— 2 個が同じ位置に来る経路が構造として存在しない。
    /// **ここに 2 個目の探索を足さないこと。**
    ///
    /// ── 位置を決める主体は 1 つ ──────────────────────────────
    ///
    /// ①②④⑤のパネルは自分では位置を決めなくなった（<c>MoveTo</c>）。
    /// タブ帯の真下・同じ左端に置くのはこの型で、**同時に出るパネルは 1 枚だけ**
    /// である。だから「互いに避ける座標」はもう要らない。
    /// 下端がビューからはみ出すぶんは各パネルの <c>ClampToView</c> が縦に寄せる
    /// （その挙動は以前から変わっていない）。
    ///
    /// ── 閉じるボタンも 1 個 ──────────────────────────────
    ///
    /// パネルごとにあった X は撤去した。タブ帯の右端の X が全部を閉じる。
    ///
    /// ── アイコンを推測しない ──────────────────────────────
    ///
    /// **前景スプライトの名前を 1 つも指定しない。** スプライト名はアトラスの
    /// データであってアセンブリからは読めないため、名前を当てにいくと
    /// 「見えないボタン」になり得る（<c>DisasterPanelBar</c> のクラス doc）。
    /// 見分けは <c>text</c>（短い名前）とツールチップで付ける。
    ///
    /// ── セッション状態 ────────────────────────────────
    ///
    /// <see cref="Remove"/> がボタン・タブ帯・開閉状態を全部捨てる。
    /// 次の都市は**ボタン 1 個・タブ帯 0 本・開いているパネル 0 枚**で始まる。
    /// Unity オブジェクトを配列に持たない（fake-null が配列越しには治らない）。
    /// </summary>
    public static class InfoHub
    {
        private delegate string TextSource();
        private delegate bool Gate();
        private delegate void Command();
        private delegate float FloatSource();

        /// <summary>ボタンが置けるまでの再試行間隔（main スレッド更新の回数）。</summary>
        private const int SearchIntervalFrames = 120;

        /// <summary>設置後の保守間隔。ラベル更新・設定変更の反映。</summary>
        private const int MaintainIntervalFrames = 30;

        /// <summary>諦めるまでの試行回数。永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private const float ButtonSize = 32f;
        private const float StripHeight = 34f;

        /// <summary>
        /// ボタンを置く**画面最上段の y**。所有者の依頼（2026-08-22）:
        ///
        /// > D＋ボタンが左サイドメニューと重なる位置にあるので、バニラのサイドメニューを
        /// > 操作する際に邪魔になります。CSWARFRONT ボタンや SIREN Alert ボタンと
        /// > 同じ高さで並んで表示されるようにしてください（それぞれのボタンは一スクリプトを参照）
        ///
        /// 実際に 2 本のスクリプトを読んで合わせた値である:
        ///
        /// | MOD | 置き方 | 大きさ | y | 中心の y |
        /// |---|---|---|---|---|
        /// | CS:WARFRONT (`MilitaryBuildPanel`) | 固定 `new Vector3(150f, 10f)` | 36 | 10 | 28 |
        /// | SIREN Alert (`SirenButton`) | 最上段を左から走査 | 44 | 4 | 26 |
        /// | ⑤これ | 最上段を左から走査 | 32 | **10** | 26 |
        ///
        /// ★ **x は固定しない。** CS:WARFRONT は 150 に決め打っているが、それは
        ///   「他の MOD が居ない」を仮定している。こちらは SIREN Alert と同じく
        ///   左から空きを探す —— 先に居る者を避けるので、3 本が同時に入っていても
        ///   並ぶ（<see cref="FreeSlotFinder"/>）。
        /// </summary>
        private const float TopRowY = 10f;

        /// <summary>
        /// 最上段の探索の下限 x。**既に居るボタンが 1 つも無いときだけ**ここからになる。
        ///
        /// ★★ <b>通常はここからは始めない。</b>（2026-08-22、所有者の実機報告
        ///   「ボタンの位置がまだ左すぎます」）。左端から探すだけだと
        ///   **先に置けた者がいちばん左を取る**ので、この MOD がたまたま 1 番に
        ///   間に合うと画面の端に張り付いてしまう（実際にそうなった）。
        ///   <c>FreeSlotFinder.RightEdgeOfBand</c> で**既に居る一団の右端**を求め、
        ///   その右から探す。
        /// </summary>
        private const float TopRowStartX = 8f;

        /// <summary>隣のボタンとの隔て（px）。SIREN Alert の <c>Gap</c> と同じ値。</summary>
        private const float TopRowGap = 8f;

        /// <summary>
        /// 最上段の帯の高さ（px）。この帯にかかっているものを「隣人」と見なす。
        /// SIREN Alert の <c>bandBottom</c>（TopMargin 4 + Size 44 + Gap 8 = 56）と揃えてある ——
        /// あちらの 44 px のボタンもこちらの 32 px のボタンもこの帯に入る。
        /// </summary>
        private const float TopRowBandBottom = 56f;

        /// <summary>
        /// 置いたあとに位置を見直すフレーム。
        ///
        /// **他 MOD のボタンはそれぞれ別のタイミングで現れる**ので、
        /// 1 回決めて終わりにすると「その瞬間に居なかった者」を避けられない。
        /// SIREN Alert も同じことをしている（<c>SirenButton.RecheckFrames</c>）。
        ///
        /// ★ <b>プレイヤーが一度でも自分で動かしたら、二度と見直さない</b>
        ///   （<see cref="_userMoved"/>）。動かしたものを勝手に戻すのは、
        ///   重なっているのと同じだけ壊れている。
        /// </summary>
        private static readonly int[] RecheckFrames = { 60, 180, 420, 900 };

        /// <summary>最上段の探索の 1 歩（px）。ボタン幅 ＋ 隙間。</summary>
        private const float TopRowStepX = ButtonSize + TopRowGap;

        /// <summary>最上段の探索の上限回数。画面の右端で <see cref="ScreenSlot"/> が先に止める。</summary>
        private const int TopRowTries = 64;

        /// <summary>
        /// タブ帯の左端の**掴む場所**の幅（px）。
        ///
        /// 所有者の依頼（2026-08-22）:「タブの位置も移動できないことから非常に
        /// 操作性が悪い」。帯の全面を <c>UIDragHandle</c> で覆うと、その下のタブと X が
        /// 押せなくなる（CS:WARFRONT が実機で踏んだ不具合。あちらの
        /// <c>MilitaryBuildPanel</c> のコメントに残っている）ので、
        /// **左端のこの幅だけ**を掴む場所にして、タブはその右から並べる。
        /// </summary>
        private const float GripWidth = 16f;
        private const float TabHeight = 26f;
        private const float TabGap = 2f;
        private const float StripPad = 4f;
        private const float CloseWidth = 24f;

        /// <summary>タブ帯の既定の幅（開いているパネルが無いとき）。</summary>
        private const float DefaultStripWidth = 640f;

        /// <summary>タブ 1 枚ぶんの記述。**表示文字列を値として持たない**（言語切替で凍る）。</summary>
        private sealed class Tab
        {
            public readonly string Id;
            public readonly TextSource Label;
            public readonly Gate Wanted;
            public readonly Command Show;
            public readonly Command Hide;
            public readonly FloatSource Width;

            public UIButton Button;

            public Tab(string id, TextSource label, Gate wanted,
                       Command show, Command hide, FloatSource width)
            {
                Id = id;
                Label = label;
                Wanted = wanted;
                Show = show;
                Hide = hide;
                Width = width;
            }
        }

        /// <summary>
        /// **並ぶ順序はこの 1 本の並びだけが決める。** 足すときはここに 1 行足すこと
        /// （座標を発明しない）。番号順（①予報 ②地震 ④台風 ⑤火山）＋診断。
        ///
        /// ★ ③火災旋風のタブは無い。③は自然発生しかせず、読むべき状態も
        ///   診断のダンプにしか無い（<c>FireWhirlFeature</c> のクラス doc）。
        /// </summary>
        private static readonly List<Tab> Tabs = new List<Tab>
        {
            new Tab("forecast",
                    delegate { return Strings.ForecastTitle; },
                    delegate { return ModSettings.ForecastEnabled.value; },
                    ForecastPanel.Show, ForecastPanel.Hide,
                    delegate { return ForecastPanel.Width; }),

            new Tab("earthquake",
                    delegate { return Strings.EarthquakeTitle; },
                    delegate { return ModSettings.EarthquakeEnabled.value; },
                    EarthquakePanel.Show, EarthquakePanel.Hide,
                    delegate { return EarthquakePanel.Width; }),

            new Tab("typhoon",
                    delegate { return Strings.TyphoonTitle; },
                    delegate { return ModSettings.TyphoonEnabled.value; },
                    TyphoonPanel.Show, TyphoonPanel.Hide,
                    delegate { return TyphoonPanel.Width; }),

            new Tab("volcano",
                    delegate { return Strings.VolcanoTitle; },
                    delegate { return ModSettings.VolcanoEnabled.value; },
                    VolcanoPanel.Show, VolcanoPanel.Hide,
                    delegate { return VolcanoPanel.Width; }),

            // 診断はいつでも出す。**機能を全部切っても、切れていることを読む場所は要る。**
            new Tab("diagnostics",
                    delegate { return Strings.InfoTabDiagnostics; },
                    delegate { return true; },
                    DiagnosticsPanel.Show, DiagnosticsPanel.Hide,
                    delegate { return DiagnosticsPanel.Width; }),
        };

        private static UIButton _button;
        private static UIPanel _strip;
        private static UIButton _closeButton;
        private static UIDragHandle _grip;

        /// <summary>
        /// いま <see cref="OnStripMoved"/> の中に居るか。ドラッグで動いた帯を
        /// 画面内へ丸め直すとき、その書き込みで同じイベントがもう一度飛ぶ。
        /// **再入を 1 段で止める**（無限再帰にはならないが、1 フレームに
        /// 何度も追従の計算をする意味が無い）。
        /// </summary>
        private static bool _syncingStrip;

        /// <summary>ドラッグ追従で 1 度でも例外を出したか（毎フレーム鳴らさない）。</summary>
        private static bool _dragErrorLogged;

        /// <summary>
        /// プレイヤーが一度でも自分で窓を動かしたか。
        /// 立ったら <see cref="RecheckFrames"/> の見直しは二度と走らない ——
        /// 自分で置いた場所を勝手に戻されるのは壊れている。
        /// </summary>
        private static bool _userMoved;

        /// <summary>ボタンを置いてからのフレーム数。-1 は「まだ置いていない」。</summary>
        private static int _framesSinceCreate = -1;

        /// <summary>次に見直す <see cref="RecheckFrames"/> の添字。</summary>
        private static int _recheckIndex;

        /// <summary>
        /// 今帯を動かしているのは <see cref="RecheckPlacement"/> か。
        ///
        /// ★★ これが無いと、**自動の置き直しが自分を
        ///   「プレイヤーが動かした」と誤認して、その場で以後の見直しを
        ///   全部止めてしまう**（<c>_strip.relativePosition</c> への書き込みが
        ///   <see cref="OnStripMoved"/> を呼ぶため）。
        /// </summary>
        private static bool _programmaticMove;

        /// <summary>いま選ばれているタブの <see cref="Tab.Id"/>。null なら未選択。</summary>
        private static string _activeId;

        /// <summary>
        /// **いま実際に出しているパネル**の id。<see cref="_activeId"/> と食い違って
        /// いるあいだだけ、パネルの <c>Show</c> / <c>Hide</c> / <c>MoveTo</c> が走る。
        ///
        /// ★ この 1 本が無いと、保守パス（0.5 秒ごと）が毎回 4 枚に <c>Hide()</c> を
        ///   呼ぶことになる。②の <c>EarthquakePanel.Hide</c> は
        ///   <c>PublishCursor</c> と <c>EarthquakeOverlay.Disable</c> まで走らせるので、
        ///   **見ていないパネルのために 0.5 秒ごとに仕事をする**ことになっていた。
        /// </summary>
        private static string _shownId;

        private static bool _open;

        /// <summary>例外で降りた。以後この型は何もしない。</summary>
        private static bool _dead;

        /// <summary>置き場所の探索を打ち切った。</summary>
        private static bool _gaveUp;

        private static int _frames;
        private static int _attempts;
        private static Vector2 _origin;
        private static bool _foundFreeSlot;

        /// <summary>直近に組み立てたタブの集合（設定が変わったら組み直す）。</summary>
        private static string _builtSet;

        /// <summary>診断向け。ボタンが今、画面に居るか。</summary>
        public static bool IsInstalled { get { return _button != null; } }

        /// <summary>
        /// ★★ **この MOD で 2 番目に画面へ物を浮かせる者のための出口。**
        ///
        /// <see cref="FreeSlotFinder"/> のクラス doc は「呼び出し元は 1 か所だけ」と
        /// 定めている。理由は初回の実機テストで実証済みで、**探索する主体が複数ある
        /// 限り、全員が空きを見つけられなかったときに全員が同じ preferred へ落ちる。**
        ///
        /// <c>DisasterPanelBar</c> の退避バー（バニラの災害パネルがどうしても
        /// 見つからない環境でだけ現れる）が 2 番目の主体になる。そこで
        /// **こちらのボタンより下から探し始める点**を渡す —— 探索は下方向にしか
        /// 進まないので、退避バーがこのボタンの位置を返すことは構造として起きない。
        ///
        /// <c>false</c> を返すのは「ボタンをまだ置いていない（置けるかも分からない）」
        /// ときで、そのあいだ**退避バーは待つべきである**。諦めたあと
        /// （<see cref="_gaveUp"/>）はボタンが存在しないので、退避バーは自分の
        /// 好きな位置から探してよい —— <see cref="Abandoned"/> がそれを名乗る。
        /// </summary>
        public static bool TryGetBelowAnchor(out Vector2 point)
        {
            point = Vector2.zero;
            if (_button == null) return false;

            point = new Vector2(_origin.x, _origin.y + ButtonSize + 2f + StripHeight + 8f);
            return true;
        }

        /// <summary>
        /// ボタンを置くのを恒久的に諦めたか。<c>true</c> のあいだ画面に
        /// このボタンは存在しないので、<see cref="TryGetBelowAnchor"/> が
        /// <c>false</c> を返しても待つ意味は無い。
        /// </summary>
        public static bool Abandoned { get { return _gaveUp || _dead; } }

        /// <summary>診断向け。ボタンが今どこに居るかを 1 行で。</summary>
        public static string Placement
        {
            get
            {
                if (_dead) return "disabled after an error (see the log)";
                if (_button == null)
                {
                    return _gaveUp ? "not installed (gave up)" : "not installed yet";
                }
                return "top-left at (" + _origin.x + "," + _origin.y + ") "
                       + (_foundFreeSlot ? "(free slot found)" : "(may overlap other mods)")
                       + (_open ? "; open on the " + (_activeId ?? "?") + " tab" : "; closed");
            }
        }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の仕事は間引く。
        ///
        /// **この経路で <c>Log.Warn</c> / <c>Log.Error</c> を毎回出さないこと**
        /// （どちらもスロットルが無い）。恒久的な断念は状態フラグで 1 回だけに閉じる。
        /// </summary>
        public static void Tick()
        {
            if (_dead) return;

            // ★ 置いたあとの見直しは**間引かないフレーム数**で数える
            //   （SIREN Alert の RecheckFrames と同じ尺度にするため）。
            if (_button != null && _framesSinceCreate >= 0)
            {
                _framesSinceCreate++;
                if (!_userMoved
                    && _recheckIndex < RecheckFrames.Length
                    && _framesSinceCreate >= RecheckFrames[_recheckIndex])
                {
                    _recheckIndex++;
                    try { RecheckPlacement(); }
                    catch (Exception e)
                    {
                        _recheckIndex = RecheckFrames.Length;   // 二度と試さない
                        Log.Warn("re-checking the Disaster + button position failed: "
                                 + e.GetType().Name);
                    }
                }
            }

            int interval = _button != null ? MaintainIntervalFrames : SearchIntervalFrames;
            if (_frames++ < interval) return;
            _frames = 0;

            try { Maintain(); }
            catch (Exception e)
            {
                _dead = true;
                Log.Error("info hub failed", e);
            }
        }

        /// <summary>レベルアンロード時。次の都市が必ず「ボタン 1 個」で始まるようにする。</summary>
        public static void Remove()
        {
            DestroyTabButtons();

            if (_strip != null) UnityEngine.Object.Destroy(_strip.gameObject);
            if (_button != null) UnityEngine.Object.Destroy(_button.gameObject);

            _strip = null;
            _closeButton = null;
            _grip = null;
            _syncingStrip = false;
            _dragErrorLogged = false;
            _userMoved = false;
            _programmaticMove = false;
            _framesSinceCreate = -1;
            _recheckIndex = 0;
            _button = null;
            _activeId = null;
            _shownId = null;
            _open = false;
            _dead = false;
            _gaveUp = false;
            _frames = 0;
            _attempts = 0;
            _origin = Vector2.zero;
            _foundFreeSlot = false;
            _builtSet = null;
        }

        // ------------------------------------------------------------------
        // 保守
        // ------------------------------------------------------------------

        private static void Maintain()
        {
            if (_button == null)
            {
                if (_gaveUp) return;
                if (!CreateButton()) return;
            }

            // ボタンのラベルは言語切替で変わりうる。
            string tip = Strings.InfoButtonTooltip;
            if (_button.tooltip != tip) _button.tooltip = tip;

            if (!_open) return;

            string wanted = WantedSet();
            if (_builtSet != wanted)
            {
                RebuildStrip(wanted);
                // 見ていたタブが設定で消えたなら、先頭へ移る。**開いたまま
                // 中身が空になる**（タブ帯だけが残る）状態を作らない。
                if (FindTab(_activeId) == null) _activeId = FirstWantedId();
            }

            ApplySelection();
        }

        private static bool CreateButton()
        {
            var view = UIView.GetAView();
            if (view == null)
            {
                _attempts++;
                if (_attempts > MaxAttempts)
                {
                    _gaveUp = true;
                    Log.Warn("gave up waiting for the UIView; the Disaster + info button was not placed");
                }
                else
                {
                    Log.Diag("infoHub", "no UIView yet (attempt " + _attempts + ")");
                }
                return false;
            }

            // ★★ FreeSlotFinder を呼ぶのはこの 1 行だけである（クラス doc）。
            //    タブ帯とパネルの位置はここからの相対で決まる。
            //
            // ★★ **横に探す。下へは 1 歩も降りない**（<see cref="TopRowY"/>）。
            //    以前は (8,50) から下へ降りていたので、**バニラの左サイドメニュー
            //    （縦の列）の上に必ず載った** —— 所有者の実機報告そのものである。
            //    最上段を左から右へ探せば、CS:WARFRONT と SIREN Alert の隣に並ぶ。
            bool foundFree;
            _origin = SearchTopRow(null, out foundFree);
            _foundFreeSlot = foundFree;

            var b = (UIButton)view.AddUIComponent(typeof(UIButton));
            b.name = FreeSlotFinder.SelfPrefix + "InfoButton";
            b.size = new Vector2(ButtonSize, ButtonSize);
            b.relativePosition = new Vector3(_origin.x, _origin.y);
            // ★ 前景スプライトは指定しない（クラス doc）。文字だけは必ず残る。
            b.normalBgSprite = "ButtonMenu";
            b.hoveredBgSprite = "ButtonMenuHovered";
            b.pressedBgSprite = "ButtonMenuPressed";
            b.text = Strings.InfoButtonLabel;
            b.textScale = 0.8f;
            b.textHorizontalAlignment = UIHorizontalAlignment.Center;
            b.textVerticalAlignment = UIVerticalAlignment.Middle;
            b.tooltip = Strings.InfoButtonTooltip;
            b.eventClick += OnButtonClick;

            _button = b;
            _framesSinceCreate = 0;
            _recheckIndex = 0;
            Log.Info("Disaster + info button installed at (" + _origin.x + "," + _origin.y + ")"
                     + (foundFree ? "" : " (no free slot found; it may overlap another mod)"));
            return true;
        }

        /// <summary>
        /// 最上段の空きを探す。**既に居る一団の右から始める**。
        ///
        /// ★★ これが「ボタンの位置がまだ左すぎます」への答えである
        ///   （2026-08-22）。左端から探すだけの頃は、この MOD が他 MOD より
        ///   先に間に合った場合に**画面の左端を取ってしまっていた**。
        ///
        /// ★ <paramref name="owner"/> には**自分のボタン**を渡すこと（初回は null）。
        ///   渡さないと、見直すたびに自分の右端で自分を押しやることになり、
        ///   ボタンが右へ逃げ続ける。
        /// </summary>
        private static Vector2 SearchTopRow(UIComponent owner, out bool foundFree)
        {
            float right = FreeSlotFinder.RightEdgeOfBand(0f, TopRowBandBottom, owner);
            float startX = right > 0f ? right + TopRowGap : TopRowStartX;
            if (startX < TopRowStartX) startX = TopRowStartX;

            return FreeSlotFinder.Find(new Vector2(startX, TopRowY),
                                       new Vector2(ButtonSize, ButtonSize),
                                       TopRowStepX, 0f, TopRowTries, owner, out foundFree);
        }

        /// <summary>
        /// 他 MOD のボタンがあとから現れた場合に位置を取り直す
        /// （<see cref="RecheckFrames"/>）。**プレイヤーが自分で動かしたあとは走らない。**
        ///
        /// 動かすのはボタン・タブ帯・開いているパネルの 3 つで、
        /// 位置の主体は今も <see cref="_origin"/> ただ 1 つである。
        /// </summary>
        private static void RecheckPlacement()
        {
            if (_button == null) return;

            bool foundFree;
            Vector2 wanted = SearchTopRow(_button, out foundFree);

            float dx = wanted.x - _origin.x;
            float dy = wanted.y - _origin.y;
            if (dx * dx + dy * dy < 1f) return;

            _origin = wanted;
            _foundFreeSlot = foundFree;

            _programmaticMove = true;
            try
            {
                _button.relativePosition = new Vector3(_origin.x, _origin.y);

                // 帯を動かすと <see cref="OnStripMoved"/> が飛び、あそこがパネルを
                // 連れていってくれる。<see cref="_programmaticMove"/> が立っているので
                // 「人が動かした」とは数えられない。
                if (_strip != null)
                {
                    _strip.relativePosition =
                        new Vector3(_origin.x, _origin.y + ButtonSize + 2f);
                }
            }
            finally
            {
                _programmaticMove = false;
            }

            Log.Info("Disaster + info button moved to (" + _origin.x + "," + _origin.y
                     + ") after another mod's button appeared");
        }

        private static void OnButtonClick(UIComponent c, UIMouseEventParameter p)
        {
            try
            {
                if (p != null) p.Use();
                if (_open) CloseAll(); else Open();
            }
            catch (Exception e)
            {
                Log.Error("the Disaster + info button click failed", e);
            }
        }

        private static void Open()
        {
            _open = true;
            _builtSet = null;   // 次の ApplySelection の前に必ず組み直す

            string wanted = WantedSet();
            RebuildStrip(wanted);

            // 前に見ていたタブが今も居ればそれを、居なければ先頭を選ぶ。
            if (FindTab(_activeId) == null) _activeId = FirstWantedId();

            ApplySelection();
        }

        /// <summary>タブ帯と全部のパネルを畳む。**開いていたものを 1 枚も残さない。**</summary>
        private static void CloseAll()
        {
            _open = false;
            for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
            _shownId = null;
            if (_strip != null) _strip.isVisible = false;
        }

        /// <summary>
        /// 選ばれているタブのパネルだけを出し、残りを畳む。
        ///
        /// ★ **パネルへの <c>Show</c> / <c>Hide</c> は選択が変わったときだけ**
        ///   （<see cref="_shownId"/> の doc）。タブ帯の見た目は毎回そろえる
        ///   —— 言語切替に追従するのがここだからである。
        /// </summary>
        private static void ApplySelection()
        {
            if (_strip == null) return;

            Tab active = FindTab(_activeId);

            for (int i = 0; i < Tabs.Count; i++) ApplyTabSprites(Tabs[i], Tabs[i] == active);

            if (active == null)
            {
                if (_shownId != null)
                {
                    for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
                    _shownId = null;
                }
                _strip.isVisible = false;
                return;
            }

            // ★ 幅は出しているパネルに合わせる。タブ帯だけが広いと 1 枚の
            //   パネルには見えない（①だけ 380 幅である）。
            float width = active.Width();
            if (width <= 0f) width = DefaultStripWidth;
            if (_strip.width != width) LayoutStrip(width);

            _strip.isVisible = true;

            if (_shownId == active.Id) return;

            // ★ 前面へ出すのは切り替えたときだけ。保守パスのたびに呼ぶと、
            //   0.5 秒ごとに他 MOD の UI より前へ割り込み続けることになる。
            _strip.BringToFront();

            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i] != active) Tabs[i].Hide();
            }

            // ★ 左上を決めてから出す。逆にすると、既定の位置に 1 フレームだけ
            //   出てから飛ぶ。**位置を決めるのはこの 1 行だけである。**
            MoveActivePanel(active, new Vector3(_origin.x, _origin.y + ButtonSize + 2f + StripHeight));
            active.Show();
            _shownId = active.Id;

            // ★ 出したあとにもう一度前へ。**閉じる X を持っているのは帯だけ**なので、
            //   パネルが帯より手前に来ると閉じられなくなる（パネル側の
            //   <c>ClampToView</c> が帯より上に出ないようになったので重なること自体
            //   まず無いが、押せなくなる側に倒さない）。
            _strip.BringToFront();
        }

        /// <summary>
        /// 帯がドラッグで動いた。**ボタンと開いているパネルを連れていく。**
        ///
        /// 位置の主体は今も <see cref="_origin"/> ただ 1 つで、ここはそれを
        /// 帯の実際の位置から読み直しているだけである（<see cref="FreeSlotFinder"/> を
        /// 2 回目に呼ぶ経路をここに作らないこと）。
        ///
        /// **画面の外へは出さない。** 出ると (8,1094) と同じ壊れ方
        /// （見えないから使えない）になる。丸めの書き戻しで同じイベントが
        /// もう一度飛ぶので <see cref="_syncingStrip"/> で 1 段だけ止める。
        /// </summary>
        private static void OnStripMoved(UIComponent component, Vector2 value)
        {
            if (_syncingStrip || _strip == null) return;

            _syncingStrip = true;
            try
            {
                Vector3 pos = _strip.relativePosition;
                float x = pos.x;
                float y = pos.y - (ButtonSize + 2f);

                Vector2 screen = FreeSlotFinder.ScreenSize();

                // 横はいちばん広いもの（帯）で、縦はボタン＋帯の高さで丸める。
                // **パネルの高さは入れない** —— 入れると背の高いパネルのときに
                // 帯が画面の上のほうへ張り付いて、掴んでも動かなくなる。
                float widest = _strip.width > ButtonSize ? _strip.width : ButtonSize;
                x = ScreenSlot.ClampInto(x, widest, screen.x);
                y = ScreenSlot.ClampInto(y, ButtonSize + 2f + StripHeight, screen.y);

                _origin = new Vector2(x, y);

                // ★ プレイヤーが自分で動かしたので、以後自動で置き直さない。
                //   **自動の置き直し（<see cref="RecheckPlacement"/>）もここを通る**ので、
                //   そちらは <see cref="_programmaticMove"/> で除外する ——
                //   除外しないと、最初の見直しが自分を「人が動かした」と誤認して
                //   以後の見直しを全部止めてしまう。
                if (!_programmaticMove) _userMoved = true;

                Vector3 stripAt = new Vector3(x, y + ButtonSize + 2f);
                if (_strip.relativePosition != stripAt) _strip.relativePosition = stripAt;
                if (_button != null) _button.relativePosition = new Vector3(x, y);

                Tab active = FindTab(_activeId);
                if (active != null && _shownId == active.Id)
                {
                    MoveActivePanel(active,
                                    new Vector3(x, y + ButtonSize + 2f + StripHeight));
                }
            }
            catch (Exception e)
            {
                // 毎フレーム走りうる経路。1 回だけ大きく鳴らして以後は黙る。
                if (!_dragErrorLogged)
                {
                    _dragErrorLogged = true;
                    Log.Error("moving the Disaster + info window failed", e);
                }
            }
            finally
            {
                _syncingStrip = false;
            }
        }

        /// <summary>
        /// 出すパネルの左上を決める。<c>MoveTo</c> は <c>Show</c> より先に呼ぶ ——
        /// 逆にすると、既定の位置に 1 フレームだけ出てから飛ぶ。
        /// </summary>
        private static void MoveActivePanel(Tab tab, Vector3 origin)
        {
            switch (tab.Id)
            {
                case "forecast": ForecastPanel.MoveTo(origin); break;
                case "earthquake": EarthquakePanel.MoveTo(origin); break;
                case "typhoon": TyphoonPanel.MoveTo(origin); break;
                case "volcano": VolcanoPanel.MoveTo(origin); break;
                case "diagnostics": DiagnosticsPanel.MoveTo(origin); break;
            }
        }

        // ------------------------------------------------------------------
        // タブ帯
        // ------------------------------------------------------------------

        private static void RebuildStrip(string wantedSet)
        {
            HideAllPanels();
            DestroyTabButtons();

            if (_strip == null)
            {
                var view = UIView.GetAView();
                if (view == null) return;

                var strip = (UIPanel)view.AddUIComponent(typeof(UIPanel));
                strip.name = FreeSlotFinder.SelfPrefix + "InfoTabStrip";
                strip.backgroundSprite = "MenuPanel2";
                strip.color = new Color32(255, 255, 255, 240);
                // 中はこの型が完全に決めきるので autolayout には任せない。
                strip.autoLayout = false;
                strip.height = StripHeight;
                strip.relativePosition = new Vector3(_origin.x, _origin.y + ButtonSize + 2f);
                _strip = strip;

                // ★ 掴む場所。**左端のこの幅だけ**（<see cref="GripWidth"/> の doc）。
                //   帯の全面を覆うとタブと X が押せなくなる。
                _grip = (UIDragHandle)strip.AddUIComponent(typeof(UIDragHandle));
                _grip.name = FreeSlotFinder.SelfPrefix + "InfoGrip";
                _grip.target = strip;
                _grip.width = GripWidth;
                _grip.height = TabHeight;
                _grip.relativePosition = new Vector3(StripPad * 0.5f, StripPad);
                _grip.tooltip = Strings.InfoDragTooltip;

                var gripMark = (UILabel)_grip.AddUIComponent(typeof(UILabel));
                gripMark.name = FreeSlotFinder.SelfPrefix + "InfoGripMark";
                // ★ **罫線素片を使わない**。CS の UI フォントに
                //   ある保証が無い（①の <c>HazardLevel.FilledChar</c> を ASCII に固定したのと
                //   同じ規律）。見えない文字にすると「掴む場所がある」自体が伝わらない。
                gripMark.text = "::";
                gripMark.textScale = 0.9f;
                gripMark.autoSize = false;
                gripMark.width = GripWidth;
                gripMark.height = TabHeight;
                gripMark.textAlignment = UIHorizontalAlignment.Center;
                gripMark.verticalAlignment = UIVerticalAlignment.Middle;
                // ★ ラベルがクリックを飲むと掴めなくなる。
                gripMark.isInteractive = false;

                // ★ 帯が動いたらボタンとパネルを連れていく。**位置の主体は今も
                //   <see cref="_origin"/> 1 つだけ**で、ここはそれを帯から読み直す。
                strip.eventPositionChanged += OnStripMoved;

                _closeButton = (UIButton)strip.AddUIComponent(typeof(UIButton));
                _closeButton.name = FreeSlotFinder.SelfPrefix + "InfoCloseButton";
                _closeButton.text = "X";
                _closeButton.height = TabHeight;
                _closeButton.width = CloseWidth;
                _closeButton.normalBgSprite = "ButtonMenu";
                _closeButton.hoveredBgSprite = "ButtonMenuHovered";
                _closeButton.pressedBgSprite = "ButtonMenuPressed";
                _closeButton.eventClick += (c, e) => CloseAll();
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                Tab t = Tabs[i];
                if (!t.Wanted()) continue;

                var b = (UIButton)_strip.AddUIComponent(typeof(UIButton));
                b.name = FreeSlotFinder.SelfPrefix + "InfoTab_" + t.Id;
                b.height = TabHeight;
                b.text = t.Label();
                b.tooltip = t.Label();
                // 日本語の見出しは英語より横に長い。既定倍率だと 5 タブで溢れうる。
                b.textScale = 0.8f;
                b.textHorizontalAlignment = UIHorizontalAlignment.Center;
                b.textVerticalAlignment = UIVerticalAlignment.Middle;

                Tab captured = t;
                b.eventClick += delegate(UIComponent c, UIMouseEventParameter p)
                {
                    if (p != null) p.Use();
                    Select(captured);
                };

                t.Button = b;
            }

            LayoutStrip(_strip.width > 1f ? _strip.width : DefaultStripWidth);
            _builtSet = wantedSet;
        }

        /// <summary>
        /// タブの幅を均等割りする。**1 回のループで順番に置くので、2 個が同じ位置に
        /// 来ることが構造として起きない**（<c>DisasterPanelBar</c> と同じ規律）。
        /// </summary>
        private static void LayoutStrip(float width)
        {
            if (_strip == null) return;

            _strip.width = width;

            int n = 0;
            for (int i = 0; i < Tabs.Count; i++) if (Tabs[i].Button != null) n++;

            if (_closeButton != null)
            {
                _closeButton.relativePosition =
                    new Vector3(width - StripPad - CloseWidth, StripPad);
            }

            if (n <= 0) return;

            // ★ 左端の掴む場所ぶんだけ、タブの並びを右へずらす（<see cref="GripWidth"/>）。
            float tabsLeft = StripPad * 0.5f + GripWidth + StripPad * 0.5f;
            float available = width - tabsLeft - StripPad - CloseWidth - StripPad;
            float tabWidth = (available - TabGap * (n - 1)) / n;
            if (tabWidth < 20f) tabWidth = 20f;

            int placed = 0;
            for (int i = 0; i < Tabs.Count; i++)
            {
                UIButton b = Tabs[i].Button;
                if (b == null) continue;

                b.width = tabWidth;
                b.relativePosition = new Vector3(tabsLeft + placed * (tabWidth + TabGap), StripPad);
                placed++;
            }
        }

        private static void ApplyTabSprites(Tab t, bool active)
        {
            if (t.Button == null) return;
            // 使うスプライトは既にこの MOD が使っている 3 種だけに限る。存在を
            // 確かめていない名前を増やすと、名前が違ったときにボタンが透明になる。
            t.Button.normalBgSprite = active ? "ButtonMenuPressed" : "ButtonMenu";
            t.Button.hoveredBgSprite = "ButtonMenuHovered";
            t.Button.pressedBgSprite = "ButtonMenuPressed";

            string label = t.Label();
            if (t.Button.text != label)
            {
                t.Button.text = label;
                t.Button.tooltip = label;
            }
        }

        private static void Select(Tab t)
        {
            if (t == null) return;
            _activeId = t.Id;
            ApplySelection();
        }

        /// <summary>
        /// タブ帯を組み直すときは、出していたパネルも 1 枚残らず畳む。
        /// **<see cref="_shownId"/> だけを消して畳み忘れると、タブが消えたのに
        /// パネルだけが画面に残る。**
        /// </summary>
        private static void HideAllPanels()
        {
            for (int i = 0; i < Tabs.Count; i++) Tabs[i].Hide();
            _shownId = null;
        }

        private static void DestroyTabButtons()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                Tab t = Tabs[i];
                // ★ 要素ごとに != null を見る（破棄済みの fake-null は要素側にしか出ない）。
                if (t.Button != null) UnityEngine.Object.Destroy(t.Button.gameObject);
                t.Button = null;
            }
        }

        // ------------------------------------------------------------------
        // 補助
        // ------------------------------------------------------------------

        /// <summary>いま出したいタブの集合を 1 本の文字列で表す（組み直しの判定用）。</summary>
        private static string WantedSet()
        {
            string s = "";
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Wanted()) s += Tabs[i].Id + ",";
            }
            return s;
        }

        private static Tab FindTab(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Id == id) return Tabs[i].Button != null ? Tabs[i] : null;
            }
            return null;
        }

        private static string FirstWantedId()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].Wanted() && Tabs[i].Button != null) return Tabs[i].Id;
            }
            return null;
        }
    }
}
