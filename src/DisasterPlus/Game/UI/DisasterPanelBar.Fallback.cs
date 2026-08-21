using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="DisasterPanelBar"/> の**退避先**だけを切り出した半分。
    ///
    /// バニラの災害パネルがどうしても見つからない環境で、④⑤のタイルを
    /// 画面に浮かぶ 1 本のバーに置く。**ボタンが 1 個も出ない方が悪い**という
    /// 判断で在る経路であり、通常のプレイでは 1 度も通らない。
    ///
    /// ★ ファイルを分けたのは 800 行の上限のためだけで、**規律は本体と同じ**である
    ///   —— 位置を決める主体は 1 つ、探索は起点 1 点についてだけ、
    ///   <see cref="InfoHub"/> の真下から探す（<see cref="EnsureFallbackBar"/> の doc）。
    /// </summary>
    public static partial class DisasterPanelBar
    {
        // ------------------------------------------------------------------
        // 退避先（バニラのパネルが見つからない環境）
        // ------------------------------------------------------------------

        /// <summary>
        /// 災害パネルが見つからないまま <see cref="FallbackAfterAttempts"/> 回過ぎたら、
        /// 画面に浮かぶ 1 本のバーへ退避する。**ボタンが 1 個も出ない方が悪い。**
        ///
        /// ★★ **ここは <see cref="FreeSlotFinder"/> の 2 番目の呼び出し元である。**
        /// 1 番目は <see cref="InfoHub"/>（左上のショートカット）で、あちらは常に
        /// 画面に居る。<c>FreeSlotFinder</c> のクラス doc が「呼び出し元は 1 か所だけ」と
        /// 定めているのは、**全員が空きを見つけられなかったときに全員が同じ
        /// preferred へ落ちる**からである —— 初回の実機テストで 4 個のボタンが
        /// (8,50) に積み上がったのがそれだった。
        ///
        /// そこでこのバーは**自分で preferred を決めない。**
        /// <see cref="InfoHub.TryGetBelowAnchor"/> が返す「ショートカットより下」の
        /// 点から探し始める。探索は下方向にしか進まないので、
        /// **このバーがショートカットの位置を返すことは構造として起きない。**
        /// ショートカットがまだ置かれていないあいだは**作らずに待つ**
        /// （<see cref="InfoHub.Abandoned"/> なら、そもそもボタンが存在しないので
        /// 従来どおりの preferred から探してよい）。
        ///
        /// 呼ぶのは **バー全体の起点 1 点** についてだけで、2 個ぶんの探索はしない ——
        /// 中のボタンは起点からの相対位置に 1 回のループで並べる。
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

            Vector2 preferred;
            if (!InfoHub.TryGetBelowAnchor(out preferred))
            {
                if (!InfoHub.Abandoned)
                {
                    // ショートカットの位置が決まるまで待つ。**先に置くと、
                    // あちらが後からこのバーを避けることになり、探索する主体が
                    // 2 つある状態そのものが戻る。**
                    Log.Diag("panelBar", "waiting for the info shortcut before placing the fallback bar");
                    return;
                }
                // ショートカットは置かれない（画面にボタンは存在しない）。
                preferred = new Vector2(8f, 50f);
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
            Vector2 origin = FreeSlotFinder.Find(preferred, size, h + gap, 30, null, out foundFree);
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
                ApplyGate(e, b);

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
        }    }
}
