using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 他 MOD やバニラのボタンと重ならない位置を実行時に探す。
    ///
    /// CS1 は左上に MOD のボタンが積み上がるのが慣習で、固定座標は必ずいつか衝突する
    /// （ユーザーからの明示要件）。②以降の機能も同じ問題を持つので、ここに置いて共用する。
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
        /// DisasterPlus 製 UI コンポーネントの命名接頭辞。自分自身（と自分の子孫）を
        /// 衝突判定から除外するのに使う。後続タスクはこの定数からコンポーネント名を
        /// 組み立てること（文字列 "DisasterPlus" をそれぞれの箇所で書き直さない）。
        /// </summary>
        public const string SelfPrefix = "DisasterPlus";

        /// <summary>parent を辿る際の上限。循環参照があっても sim を止めない安全弁。</summary>
        private const int MaxAncestorDepth = 32;

        /// <summary>
        /// preferred から下方向へ stepY ずつずらし、可視要素と重ならない最初の位置を返す。
        /// 見つからなければ preferred を返し foundFree=false にする
        /// （隠れて見つからないより、見えて重なる方がマシ）。
        /// </summary>
        public static Vector2 Find(Vector2 preferred, Vector2 size, float stepY,
                                   int maxTries, out bool foundFree)
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
                int effectiveTries = stepY > 0f ? maxTries : 1;

                for (int attempt = 0; attempt < effectiveTries; attempt++)
                {
                    var candidate = new Vector2(preferred.x, preferred.y + stepY * attempt);
                    if (!OverlapsAny(all, candidate, size))
                    {
                        foundFree = true;
                        return candidate;
                    }
                }

                Log.Warn("no free UI slot found after " + effectiveTries
                         + " tries; placing the button at the preferred position (it may overlap)");
                return preferred;
            }
            catch (System.Exception e)
            {
                Log.Error("free slot search failed", e);
                return preferred;
            }
        }

        private static bool OverlapsAny(UIComponent[] all, Vector2 pos, Vector2 size)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null) continue;

                // 非表示の要素は無視する。CS は UI テンプレートを非表示のまま
                // 常駐させるので、これを数えると空きが永久に見つからない。
                if (!c.isVisible) continue;

                // 我々自身のコンポーネント、またはその子孫は無視する
                // （再配置のたびに自分自身のパネルの中身と衝突しないように）。
                // ②以降で追加される複合パネルの内部ラベルやアイコンは既定の無接頭辞名を
                // 持つので、自分自身の name だけでなく祖先チェーンも見る必要がある。
                if (IsOwnComponentOrDescendant(c)) continue;

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
        /// c 自身、または祖先のいずれかが SelfPrefix で始まる名前を持てば true。
        /// MaxAncestorDepth で打ち切るので、万一 parent が循環していてもハングしない。
        /// </summary>
        private static bool IsOwnComponentOrDescendant(UIComponent c)
        {
            UIComponent cur = c;
            int depth = 0;
            while (cur != null && depth < MaxAncestorDepth)
            {
                if (cur.name != null && cur.name.StartsWith(SelfPrefix)) return true;
                cur = cur.parent;
                depth++;
            }
            return false;
        }
    }
}
