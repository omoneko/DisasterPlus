using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地震パネルの**行を作る唯一の場所**であり、**行に文字を入れる唯一の場所**。
    /// main スレッド専用（<see cref="EarthquakePanel"/> と同じ）。
    ///
    /// ── なぜ型として切り出したか ────────────────────────────────
    ///
    /// この機能の誠実さは「第 1 層（バニラの実測値）と第 2 層（本 MOD の発明）を
    /// 取り違えない」ことの上に乗っている。第 1 層の全体レビューは、その担保を
    /// <c>EarthquakePanel.cs</c> 1 ファイル内の grep でしか確認できない形に置いており、
    /// 「第 2 層が行を足すときに、ラベル生成と <c>.text</c> 代入だけを持つ小さな型を
    /// 切り出して担保を型で移す」ことを次の担当者への申し送りにしていた
    /// （<c>layer1-fix-report.md</c> の「残した緊張点」）。**Task 9 がその時点である。**
    ///
    /// ── 機械的に確認できる担保（grep 3 本） ──────────────────────
    ///
    ///   1. <c>AddUIComponent(typeof(UILabel))</c> が現れるのは
    ///      <see cref="AddLabel"/> の 1 箇所だけ。ラベルを作れるのは
    ///      <see cref="AddSectionHeader"/> / <see cref="AddPlainRow"/> /
    ///      <see cref="AddLayer1Row(UIPanel,string,ref float)"/> /
    ///      <see cref="AddLayer2Row(UIPanel,string,ref float)"/> の 4 系統だけ。
    ///   2. <c>UILabel.text</c> への代入が現れるのは <see cref="SetPlain"/> の 1 箇所だけ。
    ///   3. <c>Strings.SourceVanilla</c> / <c>Strings.SourceModel</c> が現れるのは
    ///      <see cref="SetLayer1"/> / <see cref="SetLayer2"/> の中だけ。
    ///      **接頭辞は呼び出し側に選ばせない** —— どちらを名乗るか選べる状態にすると、
    ///      この分離は必ずいつか崩れる。
    ///
    /// ファイルが分かれても担保は弱まらない。1 ファイル内の規律だったものが
    /// 「この型の外に <c>UILabel</c> の生成も <c>.text</c> 代入も存在しない」という
    /// **アセンブリ全体に対する grep** になっただけで、確認はむしろ強くなっている。
    ///
    /// **色だけに頼らない。** 接頭辞・セクション見出し・色の 3 つを同時に使う。
    /// 色覚や UI テーマの違いで区別が消える可能性に、この担保を賭けない。
    /// </summary>
    internal static class EarthquakeRows
    {
        /// <summary>
        /// パネル幅。420 →（全体レビュー）520 →（震度分布オーバーレイ）**640**。
        ///
        /// **縦は貴重で横は余っている。** UIView の座標系は高さ 1080 に正規化されて
        /// いるので、縦に伸ばせる余地はもともと無い。一方 x=600 + 640 = 1240 は、
        /// 16:9（幅 1920）でも 4:3（1440）でも 5:4（1350）でも内側に収まり、
        /// ①の予報パネル（x=200、幅 380 ＝ 右端 580）とも重ならない。
        ///
        /// 幅を 520 → 640 にすると 1 行あたりの文字数が約 24% 増えるので、
        /// 同じ説明文が少ない行数で収まる。
        ///
        /// **縦の問題そのものはタブで解いた**（<see cref="EarthquakePanelLayout"/>）。
        /// 幅をこれ以上広げて縦を稼ぐ必要はもう無い。
        /// </summary>
        internal const float PanelWidth = 640f;

        /// <summary>行の左端。パネル（およびページ）の左端からの余白。</summary>
        internal const float RowLeft = 12f;

        /// <summary>1 行ぶんの横幅。左右に <see cref="RowLeft"/> ずつ余白を取る。</summary>
        internal const float RowWidth = PanelWidth - 2f * RowLeft;

        /// <summary>折り返さない 1 行が y を進める量。</summary>
        internal const float RowStep = 22f;

        /// <summary>折り返さない 1 行の高さ。これを超える高さの行は自動的に折り返す。</summary>
        internal const float RowHeight = 20f;

        /// <summary>第 1 層 ＝ バニラが実際に計算している量。</summary>
        private static readonly Color32 Layer1Color = new Color32(255, 255, 255, 255);

        /// <summary>
        /// 第 2 層 ＝ この MOD が発明した数字。白と明確に違う色にするが、
        /// **色だけには頼らない**（接頭辞とセクション見出しが本体）。
        /// </summary>
        private static readonly Color32 Layer2Color = new Color32(150, 190, 255, 255);

        // ── ラベル生成（UILabel を作ってよいのはこの 1 箇所だけ） ──────────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height, Color32 color)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Earthquake" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = color;
            label.autoSize = false;
            // 1 行に収まらない説明文が途中で切れないようにする。
            label.wordWrap = height > RowHeight;
            return label;
        }

        /// <summary>
        /// パネル見出し。位置と幅を呼び出し側が決める唯一の行で、閉じるボタンと
        /// 重ならないよう幅を狭めるために在る（他の行は全て <see cref="RowLeft"/> /
        /// <see cref="RowWidth"/> に揃う）。中身は <see cref="SetPlain"/> が入れる。
        /// </summary>
        internal static UILabel AddTitleRow(UIPanel p, string suffix, float x, float y,
                                            float width, float height)
        {
            return AddLabel(p, suffix, x, y, width, height, Layer1Color);
        }

        internal static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y, string text)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer1Color);
            SetPlain(label, "-- " + text + " --");
            y += 26f;
            return label;
        }

        /// <summary>出典の接頭辞を持たない行（見出しの注記・状態の説明）。</summary>
        internal static UILabel AddPlainRow(UIPanel p, string suffix, ref float y,
                                            string text, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer1Color);
            SetPlain(label, text);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **第 1 層の行。** バニラ自身の式と定数だけから導いた量にのみ使う。
        /// 中身は <see cref="SetLayer1"/> でしか書けない。
        /// </summary>
        internal static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer1Color);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// 折り返す第 1 層の行。**新しいラベル生成経路ではない**（<see cref="AddLabel"/> を
        /// 共有している）。1 行に収まらない内容を持つ行のためにあり、
        /// 中身は同じく <see cref="SetLayer1"/> でしか書けない。
        /// </summary>
        internal static UILabel AddLayer1Row(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer1Color);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **第 2 層の行。** この MOD が発明した物理にのみ使う。
        /// 最初の呼び出し側は Task 9（<see cref="EarthquakeLayer2Rows"/>）である。
        /// </summary>
        internal static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight, Layer2Color);
            y += RowStep;
            return label;
        }

        /// <summary>折り返す第 2 層の行。<see cref="AddLayer1Row(UIPanel,string,ref float,float)"/> と同じ理由で在る。</summary>
        internal static UILabel AddLayer2Row(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height, Layer2Color);
            y += height + 4f;
            return label;
        }

        // ── テキスト設定（UILabel.text への代入はこの 1 箇所だけ） ──────────

        internal static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>第 1 層の行に書く。接頭辞は呼び出し側に選ばせない。</summary>
        internal static void SetLayer1(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }

        /// <summary>第 2 層の行に書く。接頭辞は呼び出し側に選ばせない。</summary>
        internal static void SetLayer2(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceModel + " " + body);
        }
    }
}
