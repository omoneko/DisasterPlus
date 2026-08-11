using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ③火災旋風。密集火災を検出して竜巻を生成し、その場に留めて延焼を撒く。
    /// </summary>
    public class FireWhirlFeature : IDisasterFeature
    {
        public string Name { get { return "FireWhirl"; } }

        private readonly BurningBuildingScanner _scanner = new BurningBuildingScanner();

        /// <summary>強度は byte。竜巻としては中程度の 60 から始める（表示 6.0）。</summary>
        private const byte SpawnIntensityBase = 60;

        public void OnLevelLoaded()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            HarmonyBootstrap.Install();

            // ツール登録は毎レベルロード必要（ToolController.m_tools はレベル毎に再構築される）。
            ToolRegistration.Register<FireWhirlPlacementTool>();
            FireWhirlPanelButton.Install();
        }

        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            // Natural Disasters DLC が無いと竜巻の DisasterInfo が存在しない。
            // FindTornadoInfo はその都度警告を出すので、DLC 無しの都市では毎 tick 呼ばない。
            if (!ModCompat.NaturalDisastersOwned) return;

            // 保守処理（紐づけ・解体済みの回収・クールダウン）は設定に関係なく必ず回す。
            // ここを設定で止めると、機能を OFF にした瞬間にレジストリだけが残り、
            // VortexPinPatch が固定を続け FireWhirlFlameFx が描画を続ける不死の渦になる。
            FireWhirlPinner.AttachVehicles();
            FireWhirlPinner.CollectFinished();
            FireWhirlRegistry.AdvanceCooldowns(deltaMinutes);

            if (!ModSettings.FireWhirlEnabled.value)
            {
                // 途中で OFF にされた / OFF のままセーブを読んだ場合。
                // 生存中の旋風はバニラの解体経路に乗せて畳む。
                EndAllLiveWhirls();
                return;
            }

            _scanner.ScanSlice();

            var config = ModSettings.ToFireWhirlConfig();
            var burning = _scanner.Current;

            UpdateExisting(config, burning, deltaMinutes);
            TrySpawnNew(config, burning);

            FireWhirlDamage.Apply(frameIndex, deltaMinutes, config.SpreadStrength);

            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count);
        }

        /// <summary>設定が OFF になったときに、生存中の旋風をすべて終了処理へ送る。</summary>
        private static void EndAllLiveWhirls()
        {
            var views = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Ending) continue;
                FireWhirlPinner.BeginEnding(views[i]);
            }
        }

        private void UpdateExisting(FireWhirlConfig config, IList<BurningBuilding> burning, float deltaMinutes)
        {
            var views = FireWhirlRegistry.Snapshot();
            float r2 = config.DetectRadius * config.DetectRadius;

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                if (v.Ending) continue;

                // この旋風の周りにまだ発生条件ぶんの火災が残っているか。
                var centre2d = v.Center.ToVec2();
                int near = 0;
                for (int b = 0; b < burning.Count; b++)
                {
                    if (centre2d.DistanceSquaredTo(burning[b].Position) <= r2) near++;
                }
                // 手動発生は発生条件の割り込み判定を免除する。火の無い場所に置けるのが
                // 手動発生の存在意義で、免除しないと次 tick に near=0 と数えられて
                // 猶予ぶんだけで消える（絶対上限は下の Evaluate でそのまま効く）。
                bool conditionMet = v.Manual || near >= config.DetectCount;

                // 手動発生は初期規模より小さくしない。周囲に火が無いと RadiusFor(0) まで
                // 縮み、発生した次のフレームで目に見えて小さくなってしまう。
                int strengthCount = v.Manual && near < v.BurningCount ? v.BurningCount : near;

                FireWhirlRegistry.UpdateStrength(
                    v.DisasterId, FireWhirlStrength.RadiusFor(strengthCount), strengthCount);
                FireWhirlRegistry.AdvanceLife(v.DisasterId, deltaMinutes, conditionMet);
            }

            // 寿命判定は更新後の値で行う。
            // 判定そのものは Core の FireWhirlLifecycle.Evaluate が持っており、
            // Life をレジストリの外に出さないので評価もレジストリ経由で行う。
            var updated = FireWhirlRegistry.Snapshot();
            for (int i = 0; i < updated.Count; i++)
            {
                if (updated[i].Ending) continue;
                if (FireWhirlRegistry.EvaluateVerdict(updated[i].DisasterId, config)
                    != FireWhirlVerdict.Dissipate) continue;

                FireWhirlPinner.BeginEnding(updated[i]);
            }
        }

        private void TrySpawnNew(FireWhirlConfig config, IList<BurningBuilding> burning)
        {
            var candidates = FireWhirlDetector.Detect(burning, config, FireWhirlRegistry.Centers());
            if (candidates.Count == 0) return;

            // 1 tick に 1 基まで。連鎖的に湧いて都市が一瞬で消えるのを防ぐ。
            var c = candidates[0];

            float y = TerrainManager.instance.SampleDetailHeight(new Vector3(c.Center.X, 0f, c.Center.Z));
            var center = new Vec3(c.Center.X, y, c.Center.Z);

            ushort disasterId;
            if (!FireWhirlSpawner.TrySpawn(center, SpawnIntensityBase, out disasterId)) return;

            FireWhirlRegistry.Add(disasterId, 0, center,
                FireWhirlStrength.RadiusFor(c.BurningCount), c.BurningCount, false);
        }

        public void OnMainThreadUpdate()
        {
            FireWhirlFlameFx.Sync();

            // 災害パネルはレベルロード時点ではまだ構築されていないことがある。
            // Tick は間引き（120 フレーム毎）と試行上限を持つので毎フレーム呼んでよい。
            FireWhirlPanelButton.Tick();
        }

        public void OnLevelUnloading()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            FireWhirlRegistry.Clear();
            FireWhirlFlameFx.Clear();
            HarmonyBootstrap.Uninstall();
            FireWhirlPanelButton.Remove();
        }
    }
}
