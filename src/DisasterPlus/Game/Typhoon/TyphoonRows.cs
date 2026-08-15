using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 台風パネルの**行を作る唯一の場所**であり、**行に文字を入れる唯一の場所**。
    /// main スレッド専用。②の <see cref="EarthquakeRows"/> と同じ形だが、
    /// **接頭辞の規約が違う**。
    ///
    /// ── ④の表示規約（②とは違う。設計書 §1.2 / §7.1）────────────────
    ///
    /// ①はバニラのハザードマップを、②はバニラの決定論的な被害モデルを見せた。
    /// **④にはその原資が無い。** 台風という現象も、風による破壊も、ワールド座標を持つ
    /// 雲も、洪水災害もバニラには存在しない（IL 事実文書 §B5 / §C7 / §D9）。つまり
    /// ④が出す数値は**原則すべて本 MOD のもの**であり、全部が同じ出所なら行ごとの印は
    /// 情報を持たない。
    ///
    ///   - 出所は<b>見出しで一度だけ</b>言う（<c>Strings.TyphoonModelHeader</c> と
    ///     <c>Strings.TyphoonModelNote</c>）
    ///   - <b>行ごとの印は付けない</b>（<see cref="AddRow(UIPanel,string,ref float)"/> /
    ///     <see cref="SetPlain"/>）
    ///   - <b>唯一の例外</b>は <c>WeatherManager</c> から読んだ雨量・雲量。これは①と同じ
    ///     バニラの実測値なので <c>Strings.SourceVanilla</c> を付ける
    ///     （<see cref="AddMeasuredRow"/> / <see cref="SetMeasured"/>）
    ///
    /// ── 機械的に確認できる担保（grep 4 本）──────────────────────
    ///
    ///   1. <c>AddUIComponent(typeof(UILabel))</c> が現れるのは <see cref="AddLabel"/> の
    ///      1 箇所だけ。行を作れるのは <see cref="AddTitleRow"/> /
    ///      <see cref="AddSectionHeader"/> / <see cref="AddRow(UIPanel,string,ref float)"/> /
    ///      <see cref="AddRow(UIPanel,string,ref float,float)"/> /
    ///      <see cref="AddMeasuredRow"/> の 5 系統だけ
    ///   2. <c>UILabel.text</c> への代入が現れるのは <see cref="SetPlain"/> の 1 箇所だけ
    ///   3. <c>Strings.SourceVanilla</c> が現れるのは <see cref="SetMeasured"/> の中だけ。
    ///      **接頭辞は呼び出し側に選ばせない**
    ///   4. <c>Strings.SourceModel</c> は <b>Game/Typhoon/ と Game/UI/TyphoonPanelButton.cs
    ///      に 1 度も現れてはいけない</b>
    ///
    /// <see cref="SetMeasured"/> を呼んでよいのは<b>雨量と雲量の 2 行だけ</b>である。
    /// それ以外の行が <c>[measured]</c> を名乗ったら、それは④が「バニラが計算した」と
    /// 嘘をついている。レビューは <c>SetMeasured</c> の**呼び出し箇所を数える**
    /// （定義 1 ＋ 雨量 ＋ 雲量 ＝ 3 件）。
    ///
    /// **②の <see cref="EarthquakeRows"/> を流用しない理由。** あちらは
    /// <c>SetLayer1</c> / <c>SetLayer2</c> という④が使ってはいけない 2 つの接頭辞を
    /// 型として持ち、ラベル名に <c>"Earthquake"</c> を焼き込んでいる。一般化して共有すると、
    /// ②のレビューが確定させた grep の担保を④の都合で作り直させることになる。
    ///
    /// **色は 1 色だけである。** ②は層が 2 つあったので色を分けたが、④は層が 1 つしか
    /// 無い。第 2 の色を作ると「色が何かを意味している」という嘘の合図になる。
    /// </summary>
    internal static class TyphoonRows
    {
        /// <summary>
        /// パネル幅。②の <see cref="EarthquakeRows.PanelWidth"/> と同じ 640。
        ///
        /// **縦は貴重で横は余っている。** UIView の座標系は高さ 1080 に正規化されて
        /// いるので縦に伸ばせる余地はもともと無い。④は説明文（出所の見出し・上陸予測の
        /// 根拠・風向の制約）が多く、幅が狭いとそれだけで行数が倍になる。
        /// </summary>
        internal const float PanelWidth = 640f;

        /// <summary>行の左端。パネルの左端からの余白。</summary>
        internal const float RowLeft = 12f;

        /// <summary>1 行ぶんの横幅。左右に <see cref="RowLeft"/> ずつ余白を取る。</summary>
        internal const float RowWidth = PanelWidth - 2f * RowLeft;

        /// <summary>折り返さない 1 行が y を進める量。</summary>
        internal const float RowStep = 22f;

        /// <summary>折り返さない 1 行の高さ。これを超える高さの行は自動的に折り返す。</summary>
        internal const float RowHeight = 20f;

        /// <summary>④の行の色。**1 色しか無い**（クラス doc）。</summary>
        private static readonly Color32 RowColor = new Color32(255, 255, 255, 255);

        // ── ラベル生成（UILabel を作ってよいのはこの 1 箇所だけ） ──────────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Typhoon" + suffix;
            label.relativePosition = new Vector3(x, y);
            label.width = width;
            label.height = height;
            label.textColor = RowColor;
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
            return AddLabel(p, suffix, x, y, width, height);
        }

        /// <summary>節の見出し。中身は構築時に決まるのでここで入れてしまう。</summary>
        internal static UILabel AddSectionHeader(UIPanel p, string suffix, ref float y, string text)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight);
            SetPlain(label, "-- " + text + " --");
            y += 26f;
            return label;
        }

        /// <summary>
        /// ふつうの行。**出所の印は付かない** —— ④の数値は原則すべて本 MOD のもので、
        /// それはパネルの見出しが一度だけ名乗っている（クラス doc）。
        /// 中身は <see cref="SetPlain"/> で入れる。
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, RowHeight);
            y += RowStep;
            return label;
        }

        /// <summary>
        /// 折り返す行。**新しいラベル生成経路ではない**（<see cref="AddLabel"/> を
        /// 共有している）。1 行に収まらない説明文のためにある。
        /// </summary>
        internal static UILabel AddRow(UIPanel p, string suffix, ref float y, float height)
        {
            var label = AddLabel(p, suffix, RowLeft, y, RowWidth, height);
            y += height + 4f;
            return label;
        }

        /// <summary>
        /// **<c>WeatherManager</c> から読んだ実測値の行。** ④でこれを使ってよいのは
        /// 雨量と雲量の 2 行だけである（クラス doc）。幾何としては
        /// <see cref="AddRow(UIPanel,string,ref float)"/> と同じで、**呼び出し箇所を
        /// 数えられるようにするために別の名前を持っている**。
        /// 中身は <see cref="SetMeasured"/> でしか書かないこと。
        /// </summary>
        internal static UILabel AddMeasuredRow(UIPanel p, string suffix, ref float y)
        {
            return AddRow(p, suffix, ref y);
        }

        // ── テキスト設定（UILabel.text への代入はこの 1 箇所だけ） ──────────

        internal static void SetPlain(UILabel label, string text)
        {
            if (label == null) return;
            label.text = text == null ? "" : text;
        }

        /// <summary>
        /// バニラの実測値の行に書く。**接頭辞は呼び出し側に選ばせない。**
        /// ここが <c>Strings.SourceVanilla</c> を参照する唯一の場所である。
        /// </summary>
        internal static void SetMeasured(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }
    }
}
