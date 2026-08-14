using System.Collections.Generic;
using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.Earthquake;
using DisasterPlus.Core.Forecast;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 2 種類の地図（バニラのハザードビューと本 MOD の震度分布オーバーレイ）と、
    /// カーソル 1 点についてその 2 つが出す数字。**main スレッド専用。**
    ///
    /// <see cref="EarthquakePanel"/> から切り出したのは、あのファイルがプロジェクト規約の
    /// 800 行を超えていたためで、**内容は 1 文字も変えていない**。
    /// 行の生成も <c>.text</c> の代入もこのファイルには無く、
    /// <see cref="EarthquakeRows"/> を通してしか行えない。
    ///
    /// **2 つの地図は別の量を塗る。** バニラのハザードビューは亀裂**線分**までの距離・
    /// 2 次減衰・<c>Rmax = R + 400</c>、しかも <c>Located</c>（＝地震計）が無いと
    /// 1 セルも塗られない（§A-6）。オーバーレイは震央からの線形ランプで、
    /// 地震計が無くても出る。並べたうえで注記と凡例にその違いを名乗らせるのが、
    /// 取り違えを防ぐいちばん確実な形になる。
    /// </summary>
    internal static class EarthquakeMapRows
    {
        private static UILabel _hazardLabel;
        private static UILabel _cursorModelsNoteLabel;

        internal static void Build(UIPanel p, ref float y)
        {
            y += 6f;

            var showButton = (UIButton)p.AddUIComponent(typeof(UIButton));
            showButton.name = FreeSlotFinder.SelfPrefix + "EarthquakeShowOnMap";
            showButton.text = Strings.EarthquakeShowOnMap;
            showButton.tooltip = Strings.EarthquakeTitle;
            showButton.width = 150f;
            showButton.height = 24f;
            showButton.relativePosition = new Vector3(EarthquakeRows.RowLeft, y);
            showButton.normalBgSprite = "ButtonMenu";
            showButton.hoveredBgSprite = "ButtonMenuHovered";
            showButton.pressedBgSprite = "ButtonMenuPressed";
            // ①のラッパーは一切変更しない。②はそのまま使うだけ（計画 4.1）。
            showButton.eventClick += (c, e) =>
                InfoModeSwitch.ShowHazard(InfoManager.SubInfoMode.EarthquakeHazard);
            y += 30f;

            // この 1 行が、数値か「空である理由」かのどちらか一方だけを出す。
            // 2 つのラベルに分けないのは意図的で、「理由を書いたのに隣に数値も残っている」
            // という状態を構造的に作れなくするため。3 行ぶんの高さを取る。
            _hazardLabel = EarthquakeRows.AddPlainRow(p, "Hazard", ref y,
                Strings.EarthquakeSwitchHazardView, 42f);

            // ★ カーソル 1 点について 2 つの別モデルの数字が並ぶ（全体レビュー M3）。
            //    上は震央からの線形ランプ（R = 2000+20i）、こちらはバニラのハザード
            //    グリッド（亀裂**線分**までの距離・2 次減衰・Rmax = 2000+20i+400、§A-6）。
            //    どちらも実測なのに一致しないので、一致しない理由を画面で名乗る。
            _cursorModelsNoteLabel = EarthquakeRows.AddPlainRow(p, "CursorModelsNote", ref y,
                Strings.EarthquakeCursorModelsNote, 54f);

            // ── 震度分布の地図オーバーレイ ──────────────────────────
            // 上の「マップに表示」（バニラのハザードビュー）の**すぐ下**に置く。
            // 中身は EarthquakeOverlayRows（行を作るのは EarthquakeRows のヘルパー）。
            y += 6f;
            EarthquakeOverlayRows.Build(p, ref y);
        }

        /// <summary>レベルアンロード時。参照を捨てるだけ（実体はパネルごと消える）。</summary>
        internal static void Destroy()
        {
            _hazardLabel = null;
            _cursorModelsNoteLabel = null;
            EarthquakeOverlayRows.Destroy();
        }

        /// <summary>
        /// スナップショットが読めていないとき。**凡例は消さない**
        /// （絵の読み方は観測値ではない）。ボタンの表示だけは実状に合わせる。
        /// </summary>
        internal static void ShowUnavailable()
        {
            EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
            EarthquakeOverlayRows.Clear();
        }

        internal static void Refresh(EarthquakeSnapshot snapshot, bool hazardViewOn,
                                     bool haveCursor, Vec3 cursor)
        {
            RefreshHazardRow(snapshot, hazardViewOn, haveCursor, cursor);
            // hazardViewOn を渡すのは、同じフレームで InfoManager を 2 回引かないため。
            EarthquakeOverlayRows.Refresh(hazardViewOn);
        }

        /// <summary>
        /// ハザードマップの読み取り。**このメソッドが数値を出せる経路は 1 本だけで、
        /// そこへ至るには 4 つの門を全て通る必要がある。**
        ///
        ///   1. 地震のハザードビューが表示中であること
        ///      （<c>m_hazardAmount</c> は単一グリッドで、表示中のサブモード 1 種類ぶんの
        ///        値しか持たない。<see cref="HazardMapReader"/> のクラス doc）
        ///   2. スナップショットが有効であること（件数が読めていなければ何も断定しない）
        ///   3. <c>Located &amp;&amp; (Emerging|Active)</c> を満たす地震が 1 つ以上あること
        ///      （§A-6。**ここが 0 なら、グリッドは正常に全ゼロである**）
        ///   4. カーソルが地形の上にあり、グリッドの内側であること
        ///
        /// どこで落ちても**数値の代わりに落ちた理由**を出す。これが①の全体レビューが
        /// 突き止めた欠陥（全ゼロのグリッドを「落雷: 0」と表示していた）の直接の修正で、
        /// 地震は同じゲートを持つので同じ扱いが要る。
        /// </summary>
        private static void RefreshHazardRow(EarthquakeSnapshot snapshot, bool hazardViewOn,
                                             bool haveCursor, Vec3 cursor)
        {
            if (!hazardViewOn)
            {
                // 「読み取れません」を使い回さない。原因は特定できていて、
                // しかもワンクリックで直せる（①の ForecastSwitchHazardView と同じ扱い）。
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeSwitchHazardView);
                return;
            }

            if (CountPaintingQuakes(snapshot.Quakes) <= 0)
            {
                // ★ ここが本タスクでいちばん重要な分岐。数値は一切出さない。
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeNotLocated);
                return;
            }

            if (!haveCursor)
            {
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeCursorUnknown);
                return;
            }

            bool ok;
            byte value = HazardMapReader.SampleAt(
                new Vector3(cursor.X, cursor.Y, cursor.Z),
                InfoManager.SubInfoMode.EarthquakeHazard, out ok);
            if (!ok)
            {
                EarthquakeRows.SetPlain(_hazardLabel, Strings.EarthquakeUnavailable);
                return;
            }

            // ラベルは①と共用する（"Hazard at cursor" は災害種別に依らない文言で、
            // ここでは地震のハザードビューが表示中であることを 1 で確認済み）。
            EarthquakeRows.SetLayer1(_hazardLabel, Strings.ForecastAtCursor + ": " + value
                + "  [" + HazardLevel.BarOf(value) + "]");
        }

        /// <summary>
        /// 今ハザードマップに何かを塗っている地震の数（§A-6 の 2 段ゲート）。
        /// 判定は <see cref="DisasterPhases.PaintsHazardMap(bool, EarthquakePhase)"/> に
        /// 任せる —— このゲートをここで書き直すと、ゲートの定義が 2 箇所に分かれる。
        /// </summary>
        private static int CountPaintingQuakes(IList<EarthquakeReading> quakes)
        {
            int painting = 0;
            for (int i = 0; i < quakes.Count; i++)
            {
                if (DisasterPhases.PaintsHazardMap(quakes[i].Located, quakes[i].Phase)) painting++;
            }
            return painting;
        }
    }
}
