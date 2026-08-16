using System;
using System.Collections.Generic;
using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ①〜⑤のボタンを **バニラの災害パネルの中** にまとめて置く、唯一の持ち主。
    ///
    /// なぜ 1 つの型が 5 個ぜんぶを持つのか
    /// ----------------------------------
    /// 以前は 5 個のボタンがそれぞれ別の型で、それぞれが同じ preferred 座標 (8,50) から
    /// <see cref="FreeSlotFinder"/> で空きを探していた。初回の実機テストの output_log.txt は
    /// その結果をそのまま記録している:
    ///
    ///   no free UI slot found after 30 tries; ... (it may overlap)
    ///   forecast panel button installed at (8,50)
    ///   earthquake panel button installed at (8,50)
    ///   typhoon panel button installed at (8,50)
    ///   volcano panel button installed at (8,50)
    ///
    /// 4 個が同じ 1 点に積み上がった。**位置を決める主体が 5 つある限り、この事故は
    /// 形を変えて何度でも起きる。** そこで位置を決める主体を 1 つにし、順序の付いた
    /// 1 本の並びを 1 回のループで配置する —— 2 個が同じ位置に来ることが構造として
    /// あり得なくなる。③のボタン（旧 FireWhirlPanelButton）が持っていた固定座標
    /// (8,8) も、例外を残さずこの並びに畳み込んである。
    ///
    /// 置き場所（IL 実測）
    /// ------------------
    /// バニラの災害パネルは <c>DisastersPanel</c>（Assembly-CSharp、名前空間なし、
    /// public sealed、<c>GeneratedScrollPanel</c> 継承）。名前文字列 "DisastersPanel" を
    /// 推測せず、型で探す（<see cref="SceneObjects.FindInScene{T}"/> 経由。
    /// <c>Object.FindObjectOfType</c> は Unity 5.6 では非アクティブな GameObject を返さない）。
    ///
    /// <c>DisastersPanel</c> は UIPanel 本体ではなく <c>UICustomControl</c> 派生で、
    /// <c>component</c> が同じ GameObject 上の実 UI コンポーネントを返す。災害アイコンが
    /// 並んでいるのはその配下の <c>UIScrollablePanel</c> で、
    /// <c>GeneratedScrollPanel.Awake</c> は IL 実測で
    /// <c>m_ScrollablePanel = this.GetComponentInChildren&lt;UIScrollablePanel&gt;()</c> と
    /// しているだけなので、こちらも同じ経路（<c>GetComponentInChildren</c>、自分自身も含む）で
    /// 同じ 1 個に辿り着く。<c>m_ScrollablePanel</c> は private だが、だからこそ
    /// リフレクションは要らない。
    ///
    /// **その行に子として足せば、位置はバニラの autolayout が決める。** バニラ自身の
    /// <c>CreateButton</c> も <c>relativePosition</c> を一度も書かず <c>zOrder</c> しか
    /// 触らない（IL 実測）ので、この行が自動配置であることは IL から言える。行は
    /// <c>UIScrollablePanel</c>（横スクロールバー付き。Awake が
    /// <c>horizontalScrollbar.incrementAmount = 109</c> を設定している）なので、
    /// **5 個増えて入り切らなくてもパネルを広げる必要は無い。溢れた分はスクロールする。**
    ///
    /// バニラの索引再利用に巻き込まれないための唯一の条件
    /// ------------------------------------------------
    /// <c>GeneratedScrollPanel.CreateButton</c> は IL 実測でこう書かれている:
    ///
    ///   if (m_ScrollablePanel.childCount &gt; m_ObjectIndex)
    ///       button = m_ScrollablePanel.components[m_ObjectIndex] as UIButton;   // ← 使い回す
    ///   else
    ///       button = ... AttachUIComponent(UITemplateManager.GetAsGameObject(kPlaceableItemTemplate));
    ///
    /// つまり **行の先頭から m_ObjectIndex 個までの子は、バニラがいつでも自分のタイルに
    /// 作り替える**（名前もスプライトも上書きされる。こちらが付けた eventClick だけは
    /// 残るので、バニラの災害タイルを押すとこちらの動作まで走る、という壊れ方をする）。
    /// これを避ける条件は 1 つだけ:
    /// **バニラのタイルが既に 1 個以上並んでいることを確認してから、その後ろに足す。**
    /// そうすればこちらの子は常に索引 m_ObjectIndex 以降に居る。
    /// <see cref="FirstVanillaTile"/> が null を返す間は 1 個も足さない。
    ///
    /// それでも作り替えられた場合（バニラのタイル数が後から増える環境）を
    /// <see cref="DropRecycled"/> が毎回検査し、名前が変わっていたら
    /// **破棄せずに eventClick だけ外して手放す**。破棄するとバニラの並びが壊れる。
    ///
    /// アイコンについて
    /// ----------------
    /// **前景スプライトの名前を 1 つも指定しない。** スプライト名はアトラスのデータで
    /// あってアセンブリからは読めないため、名前を当てにいくと「見えないボタン」に
    /// なり得る。背景だけは隣のバニラタイルから *実物のオブジェクト経由で* 借りる
    /// （文字列を発明していないので、存在しない名前になりようがない）。
    /// 見分けは <c>text</c>（各機能の短い名前）とツールチップで付ける。
    /// <c>UITextComponent.font</c> は IL 実測で null のとき
    /// <c>GetUIView().defaultFont</c> を入れて返すので、フォントを指定しなくても
    /// 文字は出る。**背景が取れなくても、文字だけは必ず残る。**
    ///
    /// 言語切替
    /// --------
    /// ラベルは <c>static readonly string[]</c> に凍らせず、保守パスのたびに
    /// <see cref="Entry.Label"/> デリゲート経由で <c>Strings</c> から読み直す。
    /// ゲーム内で言語を変えると次の保守パス（0.5 秒以内）で追従する。
    ///
    /// スレッド
    /// --------
    /// main スレッド専用。sim スレッドから呼んではいけない。
    /// </summary>
    public static class DisasterPanelBar
    {
        // 診断が機能ごとの設置状況を引くための識別子。表示文字列ではない。
        public const string IdForecast = "forecast";
        public const string IdEarthquake = "earthquake";
        public const string IdFireWhirl = "firewhirl";
        public const string IdTyphoon = "typhoon";
        public const string IdVolcano = "volcano";

        /// <summary>行が見つかるまでの再試行間隔（main スレッド更新の回数）。</summary>
        private const int SearchIntervalFrames = 120;

        /// <summary>設置後の保守間隔。作り替え検出・ラベル更新・設定変更の反映。</summary>
        private const int MaintainIntervalFrames = 30;

        /// <summary>行の探索を諦めるまでの試行回数。永久に探し続けない。</summary>
        private const int MaxSearchAttempts = 100;

        /// <summary>この回数だけ行が見つからなければ、浮遊バーへ退避する。</summary>
        private const int FallbackAfterAttempts = 20;

        /// <summary>タイルが 1 個も無い環境での既定の大きさ（バニラのタイルはおよそこの値）。</summary>
        private static readonly Vector2 DefaultTileSize = new Vector2(109f, 100f);

        /// <summary>autolayout が切られていた場合に自前で並べるときの間隔。</summary>
        private const float ManualGap = 4f;

        private delegate string TextSource();
        private delegate bool Gate();
        private delegate void Command();

        /// <summary>
        /// ボタン 1 個ぶんの記述。**表示文字列はここに値として持たない**（言語切替で
        /// 凍るため）。<see cref="Button"/> は Unity オブジェクトなので、
        /// 「並びが null でないこと」ではなく **要素ごとに <c>!= null</c> を見る**
        /// （破棄済みの fake-null は要素側にしか出ない）。
        /// </summary>
        private sealed class Entry
        {
            public readonly string Id;
            public readonly string ComponentName;
            public readonly TextSource Label;
            public readonly TextSource Tooltip;
            public readonly Gate Wanted;
            public readonly Command Activate;

            /// <summary>ボタンが消えるときに閉じるパネル。無ければ null。</summary>
            public readonly Command HideBody;

            public UIButton Button;
            public MouseEventHandler Handler;

            public Entry(string id, TextSource label, TextSource tooltip,
                         Gate wanted, Command activate, Command hideBody)
            {
                Id = id;
                ComponentName = FreeSlotFinder.SelfPrefix + id + "Button";
                Label = label;
                Tooltip = tooltip;
                Wanted = wanted;
                Activate = activate;
                HideBody = hideBody;
            }
        }

        /// <summary>
        /// **並ぶ順序はこの 1 本の並びだけが決める。** 追加するときはここに 1 行足す
        /// （座標を発明しないこと）。番号順（①予報 ②地震 ③火災旋風 ④台風 ⑤火山）。
        ///
        /// ③だけ <c>Wanted</c> が常に true なのは、以前からそうだったからである
        /// （旧 FireWhirlPanelButton は設定を見ずに常に設置していた）。ここで
        /// 挙動を変えると、この変更が「配置の付け替え」以外のことをしたことになる。
        ///
        /// この並びは都市をまたいで生き残る static だが、持っている Unity オブジェクトは
        /// <see cref="Remove"/> が要素ごとに null へ戻す（並びそのものを見て
        /// 「まだ生きている」と判断しないこと）。
        /// </summary>
        private static readonly List<Entry> Entries = new List<Entry>
        {
            new Entry(IdForecast,
                      delegate { return Strings.ForecastTitle; },
                      delegate { return Strings.ForecastTitle; },
                      delegate { return ModSettings.ForecastEnabled.value; },
                      ForecastPanel.Toggle,
                      ForecastPanel.Hide),

            new Entry(IdEarthquake,
                      delegate { return Strings.EarthquakeTitle; },
                      delegate { return Strings.EarthquakeTitle; },
                      delegate { return ModSettings.EarthquakeEnabled.value; },
                      EarthquakePanel.Toggle,
                      EarthquakePanel.Hide),

            new Entry(IdFireWhirl,
                      delegate { return Strings.FireWhirlName; },
                      delegate { return Strings.FireWhirlTooltip; },
                      delegate { return true; },
                      FireWhirlPlacementTool.Activate,
                      null),

            new Entry(IdTyphoon,
                      delegate { return Strings.TyphoonTitle; },
                      delegate { return Strings.TyphoonTitle; },
                      delegate { return ModSettings.TyphoonEnabled.value; },
                      TyphoonPanel.Toggle,
                      TyphoonPanel.Hide),

            new Entry(IdVolcano,
                      delegate { return Strings.VolcanoButtonLabel; },
                      delegate { return Strings.VolcanoButtonTooltip; },
                      delegate { return ModSettings.VolcanoEnabled.value; },
                      VolcanoPanel.Toggle,
                      VolcanoPanel.Hide),
        };

        private static UIScrollablePanel _row;
        private static UIPanel _fallbackBar;

        /// <summary>例外で降りた。以後この型は何もしない。</summary>
        private static bool _dead;

        /// <summary>行の探索を打ち切った。浮遊バーの保守だけは続ける。</summary>
        private static bool _searchStopped;

        private static bool _manualLayout;
        private static int _frames;
        private static int _attempts;
        private static Vector2 _fallbackOrigin;
        private static bool _fallbackFoundFreeSlot;

        /// <summary>退避したことを 1 回だけ警告する（Log.Warn にスロットルは無い）。</summary>
        private static bool _fallbackAnnounced;

        /// <summary>
        /// 診断向け。id のボタンが今、画面に居るか。
        ///
        /// ★ ここと <see cref="Placement"/> だけは sim スレッド（各機能の
        ///   <c>WriteDiagnostics</c>）から読まれる。**読むだけで、UI には触らない** ——
        ///   <c>UnityEngine.Object</c> の <c>==</c> はネイティブポインタの
        ///   フィールド比較で、Unity の API 呼び出しを 1 つも伴わない。
        ///   旧 <c>*PanelButton.Installed</c> と同じ扱いである。
        ///   **ここから UI を変更する口を足さないこと。**
        /// </summary>
        public static bool IsInstalled(string id)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Id == id) return Entries[i].Button != null;
            }
            return false;
        }

        /// <summary>
        /// 診断向け。ボタン群が今どこに居るかを 1 行で。
        /// **「互いに重なっているか」を疑う必要はもう無い** ——
        /// 位置を決めるのは 1 本の並びに対する 1 回のループだけなので、
        /// 2 個が同じ位置に来る経路が存在しない。
        /// </summary>
        public static string Placement
        {
            get
            {
                if (_fallbackBar != null)
                {
                    return "floating fallback bar at (" + _fallbackOrigin.x + "," + _fallbackOrigin.y
                           + ") " + (_fallbackFoundFreeSlot ? "(free slot found)" : "(may overlap other mods)");
                }
                if (_dead) return "disabled after an error (see the log)";
                if (_row == null)
                {
                    return _searchStopped
                        ? "not installed (gave up looking for the vanilla disasters panel)"
                        : "not installed yet (still looking for the vanilla disasters panel)";
                }
                return _manualLayout
                    ? "inside the vanilla disasters panel, laid out by Disaster + (row autoLayout is off)"
                    : "inside the vanilla disasters panel, laid out by the panel's own autoLayout";
            }
        }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の仕事は間引く。
        ///
        /// **この経路で <c>Log.Warn</c> / <c>Log.Error</c> を毎回出さないこと**
        /// （どちらもスロットルが無い）。「まだ無い」は <c>Log.Diag</c>、
        /// 恒久的な断念は状態フラグで 1 回だけに閉じる。
        /// </summary>
        public static void Tick()
        {
            if (_dead) return;

            int interval = _row != null ? MaintainIntervalFrames : SearchIntervalFrames;
            if (_frames++ < interval) return;
            _frames = 0;

            try
            {
                Maintain();
            }
            catch (Exception e)
            {
                // 例外で毎フレーム再突入しないよう、ここで恒久的に降りる。
                _dead = true;
                Log.Error("disaster panel bar failed", e);
            }
        }

        /// <summary>レベルアンロード時。次の都市が必ず 1 個ずつの状態で始まるようにする。</summary>
        public static void Remove()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, true);

            if (_fallbackBar != null)
            {
                UnityEngine.Object.Destroy(_fallbackBar.gameObject);
                _fallbackBar = null;
            }

            _row = null;
            _dead = false;
            _searchStopped = false;
            _manualLayout = false;
            _frames = 0;
            _attempts = 0;
            _fallbackOrigin = Vector2.zero;
            _fallbackFoundFreeSlot = false;
            _fallbackAnnounced = false;
        }

        // ------------------------------------------------------------------
        // 保守
        // ------------------------------------------------------------------

        private static void Maintain()
        {
            DropRecycled();

            if (_row == null && !_searchStopped) _row = FindRow();

            if (_row == null)
            {
                // 行が無い間は浮遊バーの側を保つ（言語切替への追従もここで効く）。
                if (!_searchStopped) CountFailedSearch();

                // 退避先でも設定の切り替えに追従する。ここを飛ばすと、行が
                // 見つからない環境でだけ「機能を切ったのにボタンが残る」——
                // しかもボタンが残れば押せるので、切ったはずのパネルが開く。
                if (_fallbackBar != null && !WantedMatchesInstalled()) RebuildFallbackBar();

                RefreshText();
                return;
            }

            // 行が見つかったら浮遊バーは畳む。両方に同じボタンが出ている状態を作らない。
            if (_fallbackBar != null) DismissFallbackBar();

            UIButton sample = FirstVanillaTile(_row);
            if (sample == null)
            {
                // バニラのタイルがまだ 1 個も並んでいない。ここで足すと
                // CreateButton の索引再利用に巻き込まれる（クラス doc）。
                Log.Diag("panelBar", "the disaster row has no vanilla tile yet; not attaching");
                return;
            }

            SyncButtons(_row, sample);
            RefreshText();
        }

        private static void CountFailedSearch()
        {
            _attempts++;

            if (_attempts >= FallbackAfterAttempts) EnsureFallbackBar();

            if (_attempts > MaxSearchAttempts)
            {
                _searchStopped = true;
                Log.Warn("gave up looking for the vanilla disasters panel after "
                         + MaxSearchAttempts + " attempts");
                return;
            }

            Log.Diag("panelBar", "vanilla disasters panel not found yet (attempt " + _attempts + ")");
        }

        /// <summary>
        /// 望まれている集合と実際に居る集合を一致させる。食い違ったときは
        /// **こちらのボタンを全部作り直す** —— 足りない分だけ後ろに足すと、
        /// 設定を切り替えた順序でこちらの並び順が変わってしまう。5 個なので安い。
        /// </summary>
        private static void SyncButtons(UIScrollablePanel row, UIButton sample)
        {
            if (WantedMatchesInstalled()) return;

            // 作り直す間だけ手放す。**この直後に作り直す分のパネルは閉じない**
            // （関係の無い機能の設定を触っただけで開いていたパネルが閉じるのを避ける）。
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, !Entries[i].Wanted());

            Vector2 size = (sample.size.x > 1f && sample.size.y > 1f) ? sample.size : DefaultTileSize;
            _manualLayout = !row.autoLayout;

            // autolayout が切られている環境のためだけの起点。**1 回のループで
            // 順番に置くので、autolayout の有無に関わらず 2 個が重なることはない。**
            float x = RightEdgeOfTiles(row) + ManualGap;
            float y = sample.relativePosition.y;

            int placed = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (!e.Wanted()) continue;

                Create(e, row, sample, size);
                if (_manualLayout && e.Button != null)
                {
                    e.Button.relativePosition = new Vector3(x + placed * (size.x + ManualGap), y);
                }
                placed++;
            }

            Log.Info("disaster panel buttons installed: " + placed + " inside the vanilla disasters panel ("
                     + (_manualLayout ? "laid out by Disaster +" : "panel autoLayout") + ")");
        }

        private static void Create(Entry e, UIScrollablePanel row, UIButton sample, Vector2 size)
        {
            // 前の都市や別経路の取りこぼしが残っていたら、必ず捨ててから作る。
            // **名前がこちらのものである子だけ**を捨てる（バニラが作り替えて
            // 名前を変えた子は、もうバニラの持ち物なので触らない）。
            UIComponent stale = row.Find<UIComponent>(e.ComponentName);
            if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);

            UIButton b = row.AddUIComponent<UIButton>();
            b.name = e.ComponentName;
            b.size = size;

            // 背景は隣のバニラタイルから *実物のオブジェクト経由で* 借りる。
            // スプライト名の文字列をこちらで発明しないので、存在しない名前になりようがない。
            b.atlas = sample.atlas;
            b.normalBgSprite = sample.normalBgSprite;
            b.hoveredBgSprite = sample.hoveredBgSprite;
            b.pressedBgSprite = sample.pressedBgSprite;
            b.focusedBgSprite = sample.focusedBgSprite;
            b.disabledBgSprite = sample.disabledBgSprite;

            // **前景スプライトは 1 つも指定しない**（クラス doc）。見分けは文字で付ける。
            b.textScale = 0.7f;
            b.wordWrap = true;
            b.textHorizontalAlignment = UIHorizontalAlignment.Center;
            b.textVerticalAlignment = UIVerticalAlignment.Middle;
            b.textPadding = new RectOffset(4, 4, 4, 4);
            b.text = e.Label();
            b.tooltip = e.Tooltip();

            // ラムダを直接渡さずフィールドに持つ。作り替えを検出したときに
            // eventClick から外せる形でないと、バニラのタイルにこちらの動作が残る。
            Entry captured = e;
            captured.Handler = delegate(UIComponent c, UIMouseEventParameter p) { OnClick(captured, p); };
            b.eventClick += captured.Handler;

            e.Button = b;
        }

        /// <summary>
        /// 表示文字列を読み直す。ゲーム内で言語を切り替えたときの追従はここが担う
        /// （<c>Strings</c> の値は <c>LocaleLoader</c> が書き換える）。
        /// </summary>
        private static void RefreshText()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (e.Button == null) continue;

                string label = e.Label();
                if (e.Button.text != label) e.Button.text = label;

                string tip = e.Tooltip();
                if (e.Button.tooltip != tip) e.Button.tooltip = tip;
            }
        }

        /// <summary>
        /// 破棄された、あるいは **バニラに作り替えられた** ボタンを手放す。
        ///
        /// 作り替えられた側は <c>Object.Destroy</c> してはいけない —— それはもう
        /// バニラの並びの一員で、消すと索引がずれる。外すのは eventClick だけ
        /// （そのままだと、バニラの災害タイルを押したときにこちらの動作まで走る）。
        /// 作り直しは同じ保守パスの <see cref="SyncButtons"/> が行うので、
        /// ここでパネルを閉じる必要は無い。
        /// </summary>
        private static void DropRecycled()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (e.Button == null) { Release(e); continue; }
                if (e.Button.name == e.ComponentName) continue;

                Log.Diag("panelBar", "the game reused the " + e.Id + " button as one of its own tiles; releasing it");
                Detach(e, false, false);
            }
        }

        /// <summary>
        /// ボタンを手放す。
        /// </summary>
        /// <param name="destroy">
        /// 実際に破棄するか。**バニラに作り替えられたボタンには false を渡すこと**
        /// （<see cref="DropRecycled"/> の doc）。
        /// </param>
        /// <param name="closePanel">
        /// このボタンが開けるパネルも閉じるか。ボタンだけ先に消えると、開いたままの
        /// パネルを閉じる手段が画面から無くなる（①のレビュー指摘）。
        /// 直後に作り直す場合は false。
        /// </param>
        private static void Detach(Entry e, bool destroy, bool closePanel)
        {
            if (e.Button != null)
            {
                if (e.Handler != null) e.Button.eventClick -= e.Handler;
                if (destroy) UnityEngine.Object.Destroy(e.Button.gameObject);
            }

            Release(e);

            if (closePanel && e.HideBody != null)
            {
                try { e.HideBody(); }
                catch (Exception ex) { Log.Error("closing the " + e.Id + " panel failed", ex); }
            }
        }

        private static void Release(Entry e)
        {
            e.Button = null;
            e.Handler = null;
        }

        /// <summary>
        /// 設定が望んでいる集合と、いま実際に居る集合が一致しているか。
        /// **要素ごとに <c>Button != null</c> を見る**（破棄済みの fake-null は
        /// 要素側にしか出ないので、並びを見て判断しないこと）。
        /// </summary>
        private static bool WantedMatchesInstalled()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Wanted() != (Entries[i].Button != null)) return false;
            }
            return true;
        }

        private static void OnClick(Entry e, UIMouseEventParameter p)
        {
            try
            {
                // こちらのボタンは行の子なので、押した入力は行にも伝わる。
                //
                // IL 実測: DisastersPanel.OnButtonClicked は
                // <c>component.objectUserData as DisasterInfo</c> が null なら**何もしない**。
                // こちらのボタンに objectUserData は設定していないので、この経路で
                // バニラの災害が構えられることは無い。以下の 2 行はその上での念押しで、
                // 実際に効くのは「押したタイルが選択状態のまま残らない」ことである
                // （GeneratedScrollPanel.SelectByIndex は -1 を Mathf.Max で受けて
                //  全タイルの state を Normal に戻すだけ。IL 実測、例外にならない）。
                if (p != null) p.Use();
                ClearVanillaSelection();
                e.Activate();
            }
            catch (Exception ex)
            {
                Log.Error("the " + e.Id + " button click failed", ex);
            }
        }

        /// <summary>
        /// バニラ側の「今どのタイルを選んでいるか」を解除する。
        /// <c>GeneratedScrollPanel.selectedIndex</c> は IL 実測で public な setter を持つ
        /// （リフレクションは要らない）。
        /// </summary>
        private static void ClearVanillaSelection()
        {
            DisastersPanel panel = SceneObjects.FindInScene<DisastersPanel>();
            if (panel != null) panel.selectedIndex = -1;
        }

        // ------------------------------------------------------------------
        // 行の探索
        // ------------------------------------------------------------------

        private static UIScrollablePanel FindRow()
        {
            DisastersPanel panel = SceneObjects.FindInScene<DisastersPanel>();
            if (panel == null) return null;

            UIComponent container = panel.component;
            if (container == null) return null;

            // GeneratedScrollPanel.Awake と同じ経路（自分自身も含む）。private な
            // m_ScrollablePanel をリフレクションで覗く必要はない。
            return container.GetComponentInChildren<UIScrollablePanel>();
        }

        /// <summary>
        /// 行に並んでいる **バニラの** タイルを 1 個返す。1 個も無ければ null。
        /// 大きさ・アトラス・背景スプライトの借り元であり、同時に
        /// 「もう索引再利用の射程外だ」という確認でもある（クラス doc）。
        /// </summary>
        private static UIButton FirstVanillaTile(UIScrollablePanel row)
        {
            IList<UIComponent> children = row.components;
            if (children == null) return null;

            for (int i = 0; i < children.Count; i++)
            {
                UIButton b = children[i] as UIButton;
                if (b == null) continue;
                if (IsOurs(b.name)) continue;
                if (b.size.x <= 1f || b.size.y <= 1f) continue;
                return b;
            }
            return null;
        }

        /// <summary>行に並ぶバニラタイルの右端。autolayout が切られている環境でのみ使う。</summary>
        private static float RightEdgeOfTiles(UIScrollablePanel row)
        {
            float right = 0f;
            IList<UIComponent> children = row.components;
            if (children == null) return right;

            for (int i = 0; i < children.Count; i++)
            {
                UIComponent c = children[i];
                if (c == null) continue;
                if (IsOurs(c.name)) continue;

                float edge = c.relativePosition.x + c.size.x;
                if (edge > right) right = edge;
            }
            return right;
        }

        private static bool IsOurs(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith(FreeSlotFinder.SelfPrefix, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------
        // 退避先（バニラのパネルが見つからない環境）
        // ------------------------------------------------------------------

        /// <summary>
        /// 災害パネルが見つからないまま <see cref="FallbackAfterAttempts"/> 回過ぎたら、
        /// 画面に浮かぶ 1 本のバーへ退避する。**ボタンが 1 個も出ない方が悪い。**
        ///
        /// ここが <see cref="FreeSlotFinder"/> の唯一の呼び出し元である。呼ぶのは
        /// **バー全体の起点 1 点** についてだけで、5 個ぶんの探索はしない ——
        /// 中のボタンは起点からの相対位置に 1 回のループで並べる。実機テストで
        /// 4 個が同じ点に積み上がったのは「5 つの主体がそれぞれ探した」からであって、
        /// 探索そのものが誤りだったからではない。
        /// </summary>
        private static void EnsureFallbackBar()
        {
            if (_fallbackBar != null) return;

            UIView view = UIView.GetAView();
            if (view == null)
            {
                Log.Diag("panelBar", "no UIView yet; the fallback bar cannot be created");
                return;
            }

            int wanted = 0;
            for (int i = 0; i < Entries.Count; i++) if (Entries[i].Wanted()) wanted++;
            if (wanted == 0) return;

            const float w = 150f;
            const float h = 28f;
            const float gap = 4f;
            const float pad = 4f;

            Vector2 size = new Vector2(w + pad * 2f, wanted * h + (wanted - 1) * gap + pad * 2f);

            bool foundFree;
            // owner は null。バーはこの呼び出しの後に生成されるので、除外すべき
            // 「自分自身」がまだ画面に存在しない（FreeSlotFinder.Find の doc）。
            Vector2 origin = FreeSlotFinder.Find(new Vector2(8f, 50f), size, h + gap, 30, null, out foundFree);
            _fallbackOrigin = origin;
            _fallbackFoundFreeSlot = foundFree;

            UIPanel bar = (UIPanel)view.AddUIComponent(typeof(UIPanel));
            bar.name = FreeSlotFinder.SelfPrefix + "FallbackBar";
            bar.size = size;
            bar.relativePosition = new Vector3(origin.x, origin.y);
            bar.backgroundSprite = "GenericPanel";
            // 中は下のループが完全に決めきるので autolayout には任せない。
            bar.autoLayout = false;
            _fallbackBar = bar;

            int placed = 0;
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                if (!e.Wanted()) continue;

                UIButton b = bar.AddUIComponent<UIButton>();
                b.name = e.ComponentName;
                b.size = new Vector2(w, h);
                b.relativePosition = new Vector3(pad, pad + placed * (h + gap));
                b.normalBgSprite = "ButtonMenu";
                b.hoveredBgSprite = "ButtonMenuHovered";
                b.pressedBgSprite = "ButtonMenuPressed";
                b.text = e.Label();
                b.tooltip = e.Tooltip();

                Entry captured = e;
                captured.Handler = delegate(UIComponent c, UIMouseEventParameter p) { OnClick(captured, p); };
                b.eventClick += captured.Handler;

                e.Button = b;
                placed++;
            }

            // ★ 1 回だけ。設定を切り替えるたびにバーを作り直すので（RebuildFallbackBar）、
            //   ここで無条件に Warn を出すと、スロットルの無い警告が繰り返し出る。
            if (_fallbackAnnounced) return;
            _fallbackAnnounced = true;
            Log.Warn("the vanilla disasters panel was not found after " + _attempts
                     + " attempts; the Disaster + buttons were placed on a floating bar at ("
                     + origin.x + "," + origin.y + ") instead");
        }

        /// <summary>
        /// 退避先のバーを作り直す。設定で機能を切った／入れたときに呼ばれる。
        /// **バーごと作り直す**のは、行に置く場合と同じ理由 —— 足りない分だけ
        /// 後ろに足すと、切り替えた順序で並び順が変わる。
        /// </summary>
        private static void RebuildFallbackBar()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, !Entries[i].Wanted());

            UnityEngine.Object.Destroy(_fallbackBar.gameObject);
            _fallbackBar = null;

            EnsureFallbackBar();
        }

        /// <summary>行が見つかったので浮遊バーを畳む。ボタンは同じ保守パスで行の側に作り直す。</summary>
        private static void DismissFallbackBar()
        {
            for (int i = 0; i < Entries.Count; i++) Detach(Entries[i], true, false);

            UnityEngine.Object.Destroy(_fallbackBar.gameObject);
            _fallbackBar = null;
            _fallbackOrigin = Vector2.zero;
            _fallbackFoundFreeSlot = false;
            _manualLayout = false;

            Log.Info("the vanilla disasters panel appeared; moving the Disaster + buttons into it");
        }
    }
}
