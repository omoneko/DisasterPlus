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
    ///     GetComponentsInChildren&lt;T&gt;(bool) は UnityEngine.Component 側の総称メソッドが解決する
    ///     （ColossalManaged 固有のオーバーロードではない）。
    ///   - UIComponent.isVisible の実装は m_IsVisible フィールドと親の isVisible を再帰的に
    ///     見ているだけで、GameObject.activeSelf とは完全に独立（IL 実測）。CS の UI パネルは
    ///     Show()/Hide() でこのフラグを切り替えるだけで GameObject 自体は常駐させたままにする
    ///     実装が多く、これが「非表示のまま常駐する UI テンプレート」の正体。
    ///   - name は UnityEngine.Object 由来（string, 読み取り可）。
    /// </summary>
    public static class FreeSlotFinder
    {
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
                var view = UIView.GetAView();
                if (view == null) return preferred;

                // includeInactive: true で走査する。isVisible は GameObject の非/アクティブとは
                // 別軸のフラグなので（上記コメント参照）、非アクティブな GameObject 上の
                // UIComponent を最初から除外してしまうと、将来アクティブ化されて実際に
                // 表示されるボタンを見落とす方向に倒れる。逆に、非アクティブなまま古い
                // isVisible=true が残っている要素を誤って「占有中」に数えても、結果は
                // 「本当は空いている枠を避けて別の枠を選ぶ」だけで安全側（overlap を作らない）。
                // isVisible による絞り込みは後段の OverlapsAny で行う。
                var all = view.GetComponentsInChildren<UIComponent>(true);
                if (all == null || all.Length == 0) { foundFree = true; return preferred; }

                for (int attempt = 0; attempt < maxTries; attempt++)
                {
                    var candidate = new Vector2(preferred.x, preferred.y + stepY * attempt);
                    if (!OverlapsAny(all, candidate, size))
                    {
                        foundFree = true;
                        return candidate;
                    }
                }

                Log.Warn("no free UI slot found after " + maxTries
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

                // 我々自身のボタンは無視する（再配置のたびに自分と衝突しないように）。
                // 命名規約: DisasterPlus 製の UI コンポーネントは名前を "DisasterPlus" で
                // 始める（既存の FireWhirlPanelButton も ButtonName = "DisasterPlusFireWhirlButton"）。
                if (c.name != null && c.name.StartsWith("DisasterPlus")) continue;

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
    }
}
