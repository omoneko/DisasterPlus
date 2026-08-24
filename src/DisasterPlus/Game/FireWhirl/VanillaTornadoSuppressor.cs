using System.Collections.Generic;
using DisasterPlus.Core.Common;

namespace DisasterPlus.Game
{
    /// <summary>
    /// バニラ（ND DLC）の竜巻を<b>ランダム発生から外す</b>。sim スレッド専用。
    ///
    /// ── 所有者の指示（2026-08-22）───────────────────────────────
    ///
    /// &gt; DLC の竜巻が発生して消えないバグが発生しています。
    /// &gt; バニラの竜巻は発生しないようにしてください。
    ///
    /// ── ★★ Harmony パッチは要らない（IL で確かめた）─────────────────────
    ///
    /// ランダム災害の抽選は <c>DisasterManager.FindRandomDisasterInfo</c> ただ 1 つで、
    /// 中身は<b>重み付き抽選</b>である:
    ///
    /// <code>
    /// total = Σ (解放済みの DisasterInfo).m_finalRandomProbability     // IL_0036-003F
    /// if (total == 0) return null                                      // IL_004B-0052
    /// pick  = SimulationManager.m_randomizer.Int32(total)              // IL_0053-0063
    /// ...重みの累積で 1 つ選ぶ
    /// </code>
    ///
    /// つまり <c>m_finalRandomProbability</c> を 0 にすれば、その災害は
    /// <b>抽選の候補から完全に消える</b>。
    ///
    /// ★★ <b>その値を書くのはゲーム側でただ 1 箇所である。</b>
    ///   <c>docs/tools/findwriters.ps1</c> で全メソッドの IL を走査した結果:
    ///
    /// <code>
    ///   WRITE  DisasterManager::InitializeProperties
    ///   read   DisasterManager::FindRandomDisasterInfo
    /// </code>
    ///
    ///   <c>InitializeProperties</c> は<b>レベルロード時に 1 回だけ</b>走る。
    ///   だから<b>ロード後に 1 度 0 を入れれば、そのセッションのあいだ保たれる</b> ——
    ///   毎 tick 塗り直す必要も、パッチを当てる必要も無い。
    ///   （**推測でこれを決めない。** 毎フレーム再計算される値を 1 度だけ書くのは、
    ///   何も起きないのに動いたつもりになる典型的な失敗である。）
    ///
    /// ── 火災旋風は止まらない ────────────────────────────────────
    ///
    /// ③（火災旋風）は <c>DisasterManager.CreateDisaster(out id, info)</c> を
    /// <b>直に</b>呼んで竜巻の災害を作る（<c>FireWhirlSpawner</c>）。
    /// あちらは抽選を 1 度も通らないので、この型の影響を受けない。
    /// **止まるのは「ゲームが勝手に起こす竜巻」だけである。**
    ///
    /// ── 元へ戻す ──────────────────────────────────────────
    ///
    /// 設定を切ったときのために元の値を控える。控えないと、切ったあとも
    /// 竜巻が二度と起きない ——<b>設定で戻せない変更を黙って残さない。</b>
    /// レベルアンロードでは戻さなくてよい（次のロードで
    /// <c>InitializeProperties</c> が計算し直す）が、控えは捨てる。
    /// </summary>
    public static class VanillaTornadoSuppressor
    {
        /// <summary>止めたプレハブと、その元の重み。<b>戻せるようにするため。</b></summary>
        private static readonly Dictionary<DisasterInfo, int> _original =
            new Dictionary<DisasterInfo, int>();

        private static bool _logged;

        /// <summary>今この型がランダム発生を止めているか。</summary>
        public static bool Suppressing { get { return _original.Count > 0; } }

        /// <summary>止めたプレハブの数（診断用）。</summary>
        public static int SuppressedCount { get { return _original.Count; } }

        /// <summary>直近の顛末（診断用）。**黙って何もしないをやらない。**</summary>
        public static string Detail { get; private set; }

        /// <summary>
        /// **sim スレッド。** 設定に合わせて止める／戻す。冪等なので毎 tick 呼んでよい
        /// （実際には状態が変わったときしか何も書かない）。
        /// </summary>
        public static void Apply(bool suppress)
        {
            try
            {
                if (suppress) Suppress();
                else Restore();
            }
            catch (System.Exception e)
            {
                Detail = "the vanilla tornado could not be suppressed ("
                         + e.GetType().Name + ")";
                if (!_logged)
                {
                    _logged = true;
                    Log.Error("vanilla tornado suppression failed", e);
                }
            }
        }

        private static void Suppress()
        {
            if (_original.Count > 0) return;   // もう止めてある

            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            if (count <= 0)
            {
                Detail = "no DisasterInfo prefabs are loaded yet";
                return;
            }

            int found = 0;
            for (uint i = 0; i < count; i++)
            {
                DisasterInfo info = PrefabCollection<DisasterInfo>.GetLoaded(i);
                if (info == null) continue;
                if (!(info.m_disasterAI is TornadoAI)) continue;

                found++;
                // ★ **既に 0 のものも控える。** 控えないと、戻すときに
                //   「元は 0 だった」のか「触っていない」のか区別が付かない。
                _original[info] = info.m_finalRandomProbability;
                info.m_finalRandomProbability = 0;
            }

            if (found == 0)
            {
                // ND DLC 非所持など。**それは異常ではない**ので、そう言うだけにする。
                Detail = "no TornadoAI prefab is loaded; nothing to suppress "
                         + "(Natural Disasters DLC not present?)";
                return;
            }

            Detail = found + " TornadoAI prefab(s) removed from the random disaster draw";
            Log.Info("vanilla tornadoes suppressed: " + Detail
                     + ". The fire whirl still spawns its own vortex (it calls "
                     + "CreateDisaster directly and never goes through the draw)");
        }

        private static void Restore()
        {
            if (_original.Count == 0) return;

            foreach (KeyValuePair<DisasterInfo, int> pair in _original)
            {
                // ★ Unity の fake-null。都市を出入りするとプレハブ参照が死ぬことがある。
                if (pair.Key == null) continue;
                pair.Key.m_finalRandomProbability = pair.Value;
            }

            Log.Info("vanilla tornadoes restored to the random disaster draw ("
                     + _original.Count + " prefab(s))");
            _original.Clear();
            Detail = "restored";
        }

        /// <summary>
        /// **レベルアンロードで呼ぶ。** 控えを捨てるだけで、値は書き戻さない ——
        /// 次のロードで <c>DisasterManager.InitializeProperties</c> が計算し直すので、
        /// 死んだプレハブ参照へ書きに行くほうが危ない。
        /// </summary>
        public static void Forget()
        {
            _original.Clear();
            Detail = null;
            // _logged は戻さない（この環境に対する事実である）。
        }
    }
}
