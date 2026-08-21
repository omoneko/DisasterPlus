using ColossalFramework.UI;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 他 MOD やバニラのボタンと重ならない位置を実行時に探す。
    ///
    /// CS1 は左上に MOD のボタンが積み上がるのが慣習で、固定座標は必ずいつか衝突する
    /// （ユーザーからの明示要件）。
    ///
    /// ★★ **呼び出し元は 2 か所だけであり、その 2 つは互いに独立ではない。**
    ///
    ///   1. <see cref="InfoHub"/> —— 左上のショートカット 1 個。**常に画面に居る。**
    ///   2. <see cref="DisasterPanelBar"/> の退避バー —— バニラの災害パネルが
    ///      どうしても見つからない環境でだけ現れる。**自分で preferred を
    ///      決めず、1 の真下から探し始める**（あちらの <c>EnsureFallbackBar</c>）。
    ///
    /// 2 が 1 の真下から探すのは、下の事故を構造的に不可能にするためである ——
    /// **探索は下方向にしか進まないので、2 が 1 の位置を返す経路が存在しない。**
    /// ①〜⑤が個別にここを呼ぶ形には戻さないこと。**新しい呼び出し元を足すなら、
    /// 既存のどれかの真下から探すこと**（同じ preferred を共有しないこと）。
    ///
    /// この doc は以前「②〜⑤も同じ問題を持つので共用する」と書いていたが、
    /// 実際に②〜⑤がそれぞれ同じ preferred 座標から呼んだ結果が、初回の実機テストの
    /// output_log.txt に残っている ——
    /// <c>no free UI slot found after 30 tries</c> が 4 回出て、4 個のボタンが
    /// (8,50) に積み上がった。**探索が誤っていたのではなく、探索する主体が
    /// 4 つあったことが誤りだった。** いま①②④⑤のボタンはバニラの災害パネルの中に
    /// 並び（位置はパネルの autolayout が決める）、ここが呼ばれるのは
    /// **そのパネルがどうしても見つからない環境で、退避用のバー 1 本の起点を
    /// 1 回だけ決めるとき**に限られる。バーの中のボタンは起点からの相対位置に
    /// 1 回のループで並ぶので、ここへ 5 回問い合わせることはもう無い。
    ///
    /// ── そして 2 度目の壊れ方（実機 2 回目） ─────────────────────────
    ///
    /// 上を直したあと、今度は逆側に外れた ——
    /// <c>Disaster + info button installed at (8,1094)</c>。高さ 1080 の画面で
    /// y = 1094 である。探索は下へ 30 回ぶん無条件に降り、**画面の外に出たところを
    /// 「空いている」と正しく判定していた**（外なのだから何とも重ならない）。
    /// 重なって使えないのと、見えなくて使えないのは、どちらも同じだけ壊れている。
    ///
    /// そこで<b>「画面の外へは 1 歩も出ない」を探索の上位の制約に置いた</b>。
    /// 候補の数は <see cref="DisasterPlus.Core.Common.ScreenSlot"/> が決め
    /// （Core、テストが境界を固定している）、画面の広さは
    /// <c>UIView.GetScreenResolution()</c> から読む —— **1080 を仮定しない。**
    /// 画面内に空きが 1 つも無ければ preferred（これも画面内へ丸めてある）へ落ちる。
    /// <b>その分岐は本当に到達する</b>ようになった。以前は到達しても
    /// 画面外の「空き」が先に見つかるので、事実上死んでいた。
    ///
    /// API 実測（docs/tools/ilload.ps1 + ildasm.ps1 で ColossalManaged.dll / UnityEngine.dll を
    /// 直接確認。詳細は task-2-report.md）:
    ///   - UIView.GetAView() は static。
    ///   - UIComponent は UnityEngine.MonoBehaviour 派生（Behaviour → Component → Object）なので
    ///     GetComponentsInChildren&lt;T&gt;() は UnityEngine.Component 側の総称メソッドが解決する
    ///     （ColossalManaged 固有のオーバーロードではない）。既定の includeInactive=false を
    ///     そのまま使う（理由は下記 Find のコメント）。
    ///   - UIComponent.isVisible の実装は m_IsVisible フィールドと親の isVisible を再帰的に
    ///     見ているだけで、GameObject.activeSelf は一切参照しない（IL 実測）。
    ///   - name は UnityEngine.Object 由来（string, 読み取り可）。
    /// </summary>
    public static class FreeSlotFinder
    {
        /// <summary>
        /// DisasterPlus 製 UI コンポーネントの命名接頭辞。ログや識別のために
        /// 名前を揃えるのに使う。後続タスクはこの定数からコンポーネント名を
        /// 組み立てること（文字列 "DisasterPlus" をそれぞれの箇所で書き直さない）。
        ///
        /// **衝突判定の除外には使わない**（全体レビュー指摘 I4）。以前はこの接頭辞を
        /// 持つコンポーネント（と、その子孫）を丸ごと走査対象から外していたが、
        /// それは「自分の中身と衝突しない」ためのつもりが、実際には
        /// **この MOD が置いた全てのボタンを、以後のあらゆる配置探索から見えなくする**
        /// 動作だった。設計書 §6 はこのクラスを②〜⑤の共用基盤と定めており、
        /// ②が同じ preferred で Find を呼ぶと予報ボタンが見えないまま真上に重ねて
        /// 置くことになる——このファイルが存在する理由そのものを、このファイルが
        /// 引き起こす形になっていた。除外は「今まさに配置しようとしている当人」
        /// だけに限る（<see cref="Find"/> の owner 引数）。
        /// </summary>
        public const string SelfPrefix = "DisasterPlus";

        /// <summary>parent を辿る際の上限。循環参照があっても sim を止めない安全弁。</summary>
        private const int MaxAncestorDepth = 32;

        /// <summary>
        /// preferred から下方向へ stepY ずつずらし、可視要素と重ならない最初の位置を返す。
        /// 見つからなければ preferred を返し foundFree=false にする
        /// （隠れて見つからないより、見えて重なる方がマシ）。
        ///
        /// ★★ **候補は必ず画面の中に限る**（<see cref="ScreenSlot"/>）。
        ///   実機の初回テストで <c>installed at (8,1094)</c>——高さ 1080 の画面で
        ///   y = 1094——が出た。探索は下端を歩いて画面の外へ出て、そこを
        ///   「空いている」と**正しく**判定していた。空いていたのは画面の外だからである。
        ///   以前の壊れ方（4 個が同じ座標に積み上がる）と、今度の壊れ方
        ///   （見えない）はどちらも使えないが、**重なるほうがまだ押せる**。
        ///   だからここは「画面の外へは 1 歩も出ない」を上位の制約に置き、
        ///   空きが無ければ画面内に丸めた preferred へ落ちる。
        /// </summary>
        /// <param name="owner">
        /// 今まさに配置しようとしているコンポーネント。これ自身とその子孫だけを
        /// 衝突判定から外す（再配置のときに自分の現在位置と衝突しないため）。
        ///
        /// **初回配置のようにまだコンポーネントが存在しない場合は null を渡す。**
        /// その場合は何も除外しない。null を渡すこと自体は正常な使い方であって、
        /// 手抜きではない。
        ///
        /// 他の DisasterPlus 製コンポーネント（別機能のボタン、開いている予報パネル等）は
        /// **除外してはいけない**。それらは画面上の場所を実際に占有しており、
        /// 避けるべき相手である（SelfPrefix の doc 参照）。
        /// </param>
        public static Vector2 Find(Vector2 preferred, Vector2 size, float stepY,
                                   int maxTries, UIComponent owner, out bool foundFree)
        {
            foundFree = false;
            try
            {
                if (maxTries <= 0)
                {
                    Log.Warn("FreeSlotFinder.Find called with maxTries=" + maxTries
                             + " (<=0); no candidate was tested, using preferred position");
                    return preferred;
                }

                var view = UIView.GetAView();
                if (view == null) return preferred;

                // ★ 画面の広さ。**1080 を仮定しない。**
                //   UIView.GetScreenResolution() は IL 実測（ColossalManaged）で
                //   「UI 座標系での解像度」を返す —— uiCamera があれば
                //   pixelSize / (pixelHeight / fixedHeight * scale)、無ければ
                //   (fixedWidth, fixedHeight)。absolutePosition / relativePosition と
                //   同じ空間なので、そのまま比較してよい。
                //   読めない環境（0 や NaN）では ScreenSlot が「制限しない」側に倒れる。
                Vector2 screen = ReadScreenSize(view);

                // preferred 自体が画面の外を指していることもある（呼び出し元は
                // 別のボタンの真下から探し始めるので、そちらが下寄りなら起こりうる）。
                preferred = new Vector2(ScreenSlot.ClampInto(preferred.x, size.x, screen.x),
                                        ScreenSlot.ClampInto(preferred.y, size.y, screen.y));

                // includeInactive は既定の false のまま使う。UIComponent.set_isVisible を
                // IL 実測すると m_IsVisible を書き換えて可視キャッシュを更新するだけで
                // GameObject.SetActive は一切呼んでいない。つまり Hide() された（＝
                // 「非表示のまま常駐する」）パネルは GameObject としては常にアクティブなまま
                // であり、includeInactive=false でも配列に入ってくる。それを isVisible で
                // 弾くのが正しい経路。
                //
                // 逆に includeInactive=true にすると、UITemplateManager.Get/Instantiate が
                // まだ画面にアタッチしていないテンプレート複製（GameObject は非アクティブ、
                // GameObject.SetActive(true) は後で UIComponent.AttachUIComponent が呼ぶ）まで
                // 拾ってしまう。これらは m_IsVisible がプレハブのシリアライズ値
                // （多くの場合 true）を保持したままで、isVisible は activeSelf を見ないので
                // フィルタで弾けない。absolutePosition も画面に配置される前の未確定値
                // （原点付近＝この探索が動く左上寄りの領域）になりがちで、実際には画面上の
                // どこも占有していないのに「占有中」と誤判定し、maxTries を空費して
                // preferred（＝この機能が避けたい重なった位置）へフォールバックしてしまう。
                var all = view.GetComponentsInChildren<UIComponent>();
                if (all == null || all.Length == 0) { foundFree = true; return preferred; }

                // stepY <= 0 だと毎回同じ候補を検査することになり、探索として意味がない。
                // 1 回だけ検査して打ち切る。そうしないと「maxTries 回試した」という警告が
                // 実態（同じ点を繰り返しただけ）と食い違う。
                // 画面の下端も同じ理由で打ち切る —— そこから先の候補は、空いていても
                // 押せない（クラス doc の (8,1094)）。
                int effectiveTries = ScreenSlot.CandidatesInside(preferred.y, size.y, stepY,
                                                                screen.y, maxTries);
                if (effectiveTries <= 0)
                {
                    // 最初の候補すら画面に入らない。下へ進めばもっと外れるので探索しない。
                    Log.Warn("no on-screen UI slot is available for a "
                             + size.x + "x" + size.y + " button in a "
                             + screen.x + "x" + screen.y + " view; placing it at "
                             + preferred.x + "," + preferred.y + " (it may overlap)");
                    return preferred;
                }

                for (int attempt = 0; attempt < effectiveTries; attempt++)
                {
                    var candidate = new Vector2(preferred.x, preferred.y + stepY * attempt);
                    if (!OverlapsAny(all, candidate, size, owner))
                    {
                        foundFree = true;
                        return candidate;
                    }
                }

                // ★ ここがクラス doc の言う「隠れて見つからないより、見えて重なる方がマシ」
                //   の実体である。**この分岐は本当に到達する**（左上の列が他 MOD で
                //   埋まっている環境）。preferred は上で画面内へ丸めてあるので、
                //   戻り値が画面の外を指すことはない。
                Log.Warn("no free UI slot found after " + effectiveTries
                         + " on-screen tries (view " + screen.x + "x" + screen.y
                         + "); placing the button at the preferred position (it may overlap)");
                return preferred;
            }
            catch (System.Exception e)
            {
                Log.Error("free slot search failed", e);
                return preferred;
            }
        }

        /// <summary>
        /// UI 座標系での画面の広さ。**読めなければ (0,0) を返す** ——
        /// <see cref="ScreenSlot"/> はそれを「制限しない」と解釈するので、
        /// 寸法が読めない環境でボタンが 1 個も置けなくなることはない。
        ///
        /// <c>GetScreenResolution()</c> が例外を投げる経路（uiCamera が破棄済み等）は
        /// 実測できていないので、握って (0,0) に倒す。**Warn は出さない** ——
        /// ここは配置のたびに 1 回しか通らないが、出しても打つ手が無い。
        /// <c>fixedHeight</c> は保険で、こちらは常に読める整数である。
        /// </summary>
        private static Vector2 ReadScreenSize(UIView view)
        {
            try
            {
                Vector2 res = view.GetScreenResolution();
                if (ScreenSlot.IsUsableExtent(res.x) && ScreenSlot.IsUsableExtent(res.y))
                {
                    return res;
                }
            }
            catch (System.Exception e)
            {
                Log.Diag("freeSlot", "screen resolution unreadable: " + e.GetType().Name);
            }

            try
            {
                return new Vector2(view.fixedWidth, view.fixedHeight);
            }
            catch (System.Exception e)
            {
                Log.Diag("freeSlot", "fixed view size unreadable: " + e.GetType().Name);
                return new Vector2(0f, 0f);
            }
        }

        private static bool OverlapsAny(UIComponent[] all, Vector2 pos, Vector2 size, UIComponent owner)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;

                // 非表示の要素は無視する。CS は UI テンプレートを非表示のまま
                // 常駐させるので、これを数えると空きが永久に見つからない。
                if (!c.isVisible) continue;

                // 配置しようとしている当人（とその子孫）だけを無視する。
                // 複合パネルを再配置する場合、その内部ラベルやアイコンは既定の
                // 無接頭辞名を持つので、owner との参照一致だけでなく祖先チェーンも辿る。
                if (IsOwnedBy(c, owner)) continue;

                Vector2 cp = c.absolutePosition;
                Vector2 cs = c.size;
                if (cs.x <= 0f || cs.y <= 0f) continue;

                // 辺が接するだけ（境界が一致）は重なりに数えない。
                bool separated = pos.x + size.x <= cp.x
                              || cp.x + cs.x <= pos.x
                              || pos.y + size.y <= cp.y
                              || cp.y + cs.y <= pos.y;
                if (!separated) return true;
            }
            return false;
        }

        /// <summary>
        /// c が owner そのもの、または owner の子孫なら true。
        /// owner が null（まだ存在しない初回配置）なら常に false ＝ 何も除外しない。
        ///
        /// 名前ではなく参照の一致で見るのが要点。名前（接頭辞）で見ると、この MOD の
        /// **他の**コンポーネントまで巻き添えで除外され、後続機能が既存ボタンの真上に
        /// 配置されるようになる（SelfPrefix の doc、全体レビュー指摘 I4）。
        ///
        /// MaxAncestorDepth で打ち切るので、万一 parent が循環していてもハングしない。
        /// </summary>
        private static bool IsOwnedBy(UIComponent c, UIComponent owner)
        {
            if (owner == null) return false;

            UIComponent cur = c;
            int depth = 0;
            while (cur != null && depth < MaxAncestorDepth)
            {
                // UnityEngine.Object の == オーバーロード経由で比較する
                // （破棄済みの fake-null を素の参照比較で取り違えない）。
                if (cur == owner) return true;
                cur = cur.parent;
                depth++;
            }
            return false;
        }
    }
}
