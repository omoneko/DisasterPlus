using ColossalFramework.UI;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地震パネルを開閉するトグルボタン。①の <see cref="ForecastPanelButton"/> と
    /// 同じ形で、差分は名前・保存キー・クリック先だけ。
    ///
    /// **preferred 座標は予報ボタンと同じ (8, 50) のままにしてある。これは手抜きではない。**
    /// <see cref="FreeSlotFinder"/> はそこが既に占有されていれば下へずらすので、
    /// 「同じ preferred から始めて、先に置かれたボタンを避ける」のがこの仕組みの
    /// 想定どおりの使い方である。①の全体レビューは、この探索が
    /// <c>SelfPrefix</c> による一括除外のせいで**この MOD 自身のボタンを見落とす**
    /// 欠陥を持っていたことを見つけて撤去した（<c>FreeSlotFinder.SelfPrefix</c> の doc）。
    /// ここで新しい preferred 座標を発明すると、その修正が効いているかどうかを
    /// 誰も確かめられなくなる上に、「他 MOD のボタンと重ならない」という当初の要件
    /// （ユーザーからの明示要件）を、②以降の機能が固定座標で再び壊すことになる。
    /// </summary>
    public static class EarthquakePanelButton
    {
        private const string ButtonName = FreeSlotFinder.SelfPrefix + "EarthquakeButton";

        /// <summary>再試行の間隔（main スレッド更新の回数）。①・③のボタンと同じ間引きに揃える。</summary>
        private const int RetryIntervalFrames = 120;

        /// <summary>再試行の上限。UIView が現れない環境で永久に探し続けない。</summary>
        private const int MaxAttempts = 100;

        private static readonly Vector2 ButtonSize = new Vector2(150f, 28f);
        private static readonly Vector2 PreferredPosition = new Vector2(8f, 50f);
        private const float StepY = 34f;
        private const int MaxSlotTries = 30;

        private static UIButton _button;
        private static bool _gaveUp;
        private static int _framesSinceTry;
        private static int _attempts;

        /// <summary>診断向け。ボタンが設置済みか。</summary>
        public static bool Installed { get { return _button != null; } }

        /// <summary>診断向け。今回の設置で保存済み座標を再利用したか（false なら新規探索）。</summary>
        public static bool UsedSavedPosition { get; private set; }

        /// <summary>診断向け。新規探索だった場合、空きが見つかったか。</summary>
        public static bool FoundFreeSlot { get; private set; }

        /// <summary>
        /// main スレッドから毎フレーム呼ばれる。実際の試行は間引き、かつ設定で無効なら
        /// 何もしない（既に設置済みなら撤去する）。
        /// </summary>
        public static void Tick()
        {
            if (!ModSettings.EarthquakeEnabled.value)
            {
                if (_button != null) Remove();
                return;
            }

            if (_button != null || _gaveUp) return;
            if (_framesSinceTry++ < RetryIntervalFrames) return;
            _framesSinceTry = 0;
            Install();
        }

        /// <summary>main スレッドから呼ぶ。</summary>
        public static void Install()
        {
            if (_button != null || _gaveUp) return;

            if (++_attempts > MaxAttempts)
            {
                _gaveUp = true;
                Log.Warn("gave up looking for a UIView after "
                         + MaxAttempts + " attempts; earthquake button not added");
                return;
            }

            try
            {
                var view = UIView.GetAView();
                if (view == null)
                {
                    Log.Diag("earthquakeButton", "UIView not ready yet");
                    return;
                }

                Vector2 pos = ResolvePosition();

                var component = view.AddUIComponent(typeof(UIButton));
                var button = (UIButton)component;
                button.name = ButtonName;
                button.text = Strings.EarthquakeTitle;
                button.tooltip = Strings.EarthquakeTitle;
                button.width = ButtonSize.x;
                button.height = ButtonSize.y;
                button.relativePosition = new Vector3(pos.x, pos.y);
                button.normalBgSprite = "ButtonMenu";
                button.hoveredBgSprite = "ButtonMenuHovered";
                button.pressedBgSprite = "ButtonMenuPressed";

                button.eventClick += (c, e) => EarthquakePanel.Toggle();

                _button = button;
                Log.Info("earthquake panel button installed at (" + pos.x + "," + pos.y + ")");
            }
            catch (System.Exception e)
            {
                // 例外で毎フレーム再突入しないよう、ここで諦める。
                _gaveUp = true;
                Log.Error("earthquake panel button install failed", e);
            }
        }

        /// <summary>
        /// 保存済み座標があればそれを使い、無ければ <see cref="FreeSlotFinder"/> で探して保存する。
        /// 設定画面の「ボタン位置をリセット」は両方を -1 に戻すだけ（ModSettings 側）なので、
        /// ここでの再探索は次回のインストール（次のレベルロード）で自然に起きる。
        /// </summary>
        private static Vector2 ResolvePosition()
        {
            if (ModSettings.EarthquakeButtonX.value >= 0 && ModSettings.EarthquakeButtonY.value >= 0)
            {
                UsedSavedPosition = true;
                FoundFreeSlot = false;
                return new Vector2(ModSettings.EarthquakeButtonX.value, ModSettings.EarthquakeButtonY.value);
            }

            UsedSavedPosition = false;
            bool foundFree;
            // owner は null。ここは初回配置で、ボタンはこの呼び出しの後に生成される
            // （＝除外すべき「自分自身」がまだ画面に存在しない）。**予報ボタンを
            // owner に渡してはいけない** —— それは今まさに避けたい相手である
            // （FreeSlotFinder.Find の owner 引数の doc）。
            Vector2 pos = FreeSlotFinder.Find(
                PreferredPosition, ButtonSize, StepY, MaxSlotTries, null, out foundFree);
            FoundFreeSlot = foundFree;

            if (!foundFree)
            {
                Log.Diag("earthquakeButton", "no free slot found; using preferred position (may overlap)");
            }

            ModSettings.EarthquakeButtonX.value = Mathf.RoundToInt(pos.x);
            ModSettings.EarthquakeButtonY.value = Mathf.RoundToInt(pos.y);
            return pos;
        }

        /// <summary>レベルアンロード時。次の都市で必ず探し直せるよう再試行状態も戻す。</summary>
        public static void Remove()
        {
            if (_button != null)
            {
                Object.Destroy(_button.gameObject);
                _button = null;
            }

            _gaveUp = false;
            _framesSinceTry = 0;
            _attempts = 0;
            UsedSavedPosition = false;
            FoundFreeSlot = false;
        }
    }
}
