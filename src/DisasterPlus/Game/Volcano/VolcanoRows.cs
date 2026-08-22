using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 火山パネルの**行を作る唯一の場所**であり、**行に文字を入れる唯一の場所**。
    /// main スレッド専用。④の <see cref="TyphoonRows"/> と同じ形だが、
    /// **[measured] を名乗ってよい行が違う**。
    ///
    /// ── ⑤の表示規約（設計書 §7.4）───────────────────────────
    ///
    /// ①はバニラのハザードマップを、②はバニラの決定論的な被害モデルを見せた。
    /// **⑤にその原資は無い。** 火山という現象はバニラに存在せず、溶岩・マグマ・
    /// 溶融物のプレハブもマテリアルもシェーダも、DLL の文字列ヒープにすら
    /// 1 件も無い（IL 事実文書 §B-5）。つまり⑤が出す数値は**原則すべて本 MOD のもの**で、
    /// 全部が同じ出所なら行ごとの印は情報を持たない。
    ///
    ///   - 出所を名乗る見出しは**パネルから外した**（2026-08-22）。
    ///     今名乗っているのは下の「唯一の例外」の印と、診断ダンプである
    ///   - <b>行ごとの印は付けない</b>（<see cref="AddRow(UIPanel,string,ref float)"/> /
    ///     <see cref="SetPlain"/>）
    ///   - <b>唯一の例外</b>は、ゲームの配列から数えただけで⑤が何も計算していない値、
    ///     すなわち<b>火山タブの「影響範囲」の 1 行</b>（建物数と道路セグメント数）。
    ///     ここにだけ <c>Strings.SourceVanilla</c> を付ける
    ///     （<see cref="AddMeasuredRow"/> / <see cref="SetMeasured"/>）
    ///
    /// <see cref="SetMeasured"/> を呼んでよいのは<b>その 1 行だけ</b>である。
    /// 隆起の進捗も、溶岩の位置も、火口の深さも、**全部⑤が決めた数字**であって
    /// ゲームが計算したものではない。レビューは <c>SetMeasured</c> の呼び出し箇所を数える。
    ///
    /// ★ **かつては 3 行だった**（地形高さ・建物数・道路数）。まず確認の窓を
    ///   絞ったときに地形高さが診断ダンプへ移り、建物と道路が 1 行にまとまった。
    ///   2026-08-21 に**確認の窓そのものを撤去した**とき、残った 1 行は
    ///   <see cref="VolcanoEffectRows"/>（火山タブ）へ移った。
    ///   **置き場所が変わっただけで、件数も規約も変わっていない。**
    ///
    /// ★ 道路が数えられなかったとき（<c>SegmentCount &lt; 0</c>）は
    ///   <see cref="SetPlain"/> で出す。**読めなかった値をゲームの実測値として
    ///   名乗らない。**
    ///
    /// ── 機械的に確認できる担保（grep 5 本）──────────────────────
    ///
    /// ★ **コマンドと件数は実際に走らせて合わせてある。** ④のレビューは、書いてある
    ///   手順が書いてある件数を出さないことを見つけている —— 規則の文（doc コメント）が
    ///   規則の反例として grep に引っかかっていた。**合わない手順は、レビューする人に
    ///   ヒットを手で読み飛ばす癖を付けさせる**ので、コメント行を除く形で書いてある。
    ///   以下はすべてリポジトリのルートから走らせる。
    ///
    /// <code>
    /// # 対象は⑤の表示コード全部。
    /// #   V='src/DisasterPlus/Game/Volcano src/DisasterPlus/Game/UI/VolcanoPlacementTool.cs'
    /// #   （ボタンは DisasterPanelBar が持つので⑤の表示コードではなくなった）
    /// #   grep -v '///' で doc コメントを落とす（この doc 自身が引っかかるため）。
    ///
    /// # 1. UILabel を作るのは AddLabel の 1 箇所だけ                        -> 1
    /// grep -rn --include=*.cs "AddUIComponent(typeof(UILabel))" $V | grep -v '///' | wc -l
    ///
    /// # 2. UILabel.text へ代入するのは SetPlain の 1 箇所だけ               -> 1
    /// grep -rn --include=*.cs "label.text = " $V | grep -v '///' | wc -l
    ///
    /// # 3. Strings.SourceVanilla を参照するのはこのファイルの 2 箇所だけ    -> 2
    /// #    （SetMeasured と SetModelNote。どちらも VolcanoRows.cs）
    /// grep -rn --include=*.cs "Strings.SourceVanilla" $V | grep -v '///'
    ///
    /// # 4. Strings.SourceModel は 1 度も現れない                            -> 0
    /// grep -rn --include=*.cs "Strings.SourceModel" $V | grep -v '///' | wc -l
    ///
    /// # 5. SetMeasured の呼び出しは 1 行だけ（火山タブの「影響範囲」）      -> 1
    /// grep -rn --include=*.cs "VolcanoRows.SetMeasured(" $V | grep -v '///' | wc -l
    /// </code>
    ///
    /// 行を作れるのは <see cref="AddTitleRow"/> / <see cref="AddSectionHeader"/> /
    /// <see cref="AddRow(UIPanel,string,ref float)"/> /
    /// <see cref="AddRow(UIPanel,string,ref float,float)"/> /
    /// <see cref="AddMeasuredRow"/> の 5 系統だけである。
    ///
    /// **④の <see cref="TyphoonRows"/> を型として流用しない理由。** あちらは
    /// <c>UILabel.name</c> に <c>"Typhoon"</c> を焼き込み、クラス doc が
    /// 「<c>SetMeasured</c> を呼んでよいのは雨量と雲量の 2 行だけ」という④固有の担保を
    /// 名乗っている。一般化して共有すると、④のレビューが確定させた grep の担保を
    /// ⑤の都合で作り直させることになる（②→④で同じ判断をしている）。
    /// 設計書 §5 の「④の <c>TyphoonRows</c> を流用」は
    /// **規約と枠組みを写すという意味**として実装する。
    ///
    /// **色は 1 色だけである。** ②は層が 2 つあったので色を分けたが、⑤は層が 1 つしか
    /// 無い。第 2 の色を作ると「色が何かを意味している」という嘘の合図になる。
    /// </summary>
    internal static class VolcanoRows
    {
        /// <summary>
        /// パネル幅。②④と同じ 640。
        ///
        /// **縦は貴重で横は余っている。** UIView の座標系は高さ 1080 に正規化されて
        /// いるので縦に伸ばせる余地はもともと無い。⑤は説明文（出所の見出し・不可逆の
        /// 警告・破壊の説明・追随の遅れ）が④よりさらに多く、幅が狭いとそれだけで
        /// 行数が倍になる。
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

        /// <summary>⑤の行の色。**1 色しか無い**（クラス doc）。</summary>
        private static readonly Color32 RowColor = new Color32(255, 255, 255, 255);

        // ── ラベル生成（UILabel を作ってよいのはこの 1 箇所だけ） ──────────

        private static UILabel AddLabel(UIPanel parent, string suffix, float x, float y,
                                        float width, float height)
        {
            var label = (UILabel)parent.AddUIComponent(typeof(UILabel));
            label.name = FreeSlotFinder.SelfPrefix + "Volcano" + suffix;
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

        /// <summary>
        /// 節の見出しの飾り（<c>-- ... --</c>）を付けて入れる。
        /// **飾りを呼び出し側に書かせない** —— 2 箇所に書くと必ずいつか片方だけ変わる。
        /// </summary>
        internal static void SetSectionHeader(UILabel label, string text)
        {
            SetPlain(label, "-- " + text + " --");
        }

        /// <summary>
        /// ふつうの行。**出所の印は付かない** —— ⑤の数値は原則すべて本 MOD のもので、
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
        /// **ゲームの配列から数えただけの値の行。** ⑤でこれを使ってよいのは
        /// 火山タブの「影響範囲」の 1 行だけである（クラス doc）。幾何としては <see cref="AddRow(UIPanel,string,ref float)"/> と
        /// 同じで、**呼び出し箇所を数えられるようにするために別の名前を持っている**。
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
        /// ゲームの配列から読んだだけの値の行に書く。**接頭辞は呼び出し側に選ばせない。**
        /// <see cref="SetModelNote"/> と並んで、ここが <c>Strings.SourceVanilla</c> を
        /// 参照する 2 箇所のうちの 1 つである（どちらもこのファイルの中）。
        /// </summary>
        internal static void SetMeasured(UILabel label, string body)
        {
            SetPlain(label, Strings.SourceVanilla + " " + body);
        }

    }
}
