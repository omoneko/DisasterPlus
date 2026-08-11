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
            if (!ModSettings.FireWhirlEnabled.value) return;

            // 渦車両の紐づけと、バニラに解体済みの旋風の掃除。走査より先に済ませる。
            FireWhirlPinner.AttachVehicles();
            FireWhirlPinner.CollectFinished();

            _scanner.ScanSlice();

            var config = ModSettings.ToFireWhirlConfig();
            var burning = _scanner.Current;

            // 消滅地点のクールダウンを進める。これが無いと、消えた直後に同じ大火災が
            // 同じ場所で再発生し続け、絶対上限の意味が無くなる。
            FireWhirlRegistry.AdvanceCooldowns(deltaMinutes);

            UpdateExisting(config, burning, deltaMinutes);
            TrySpawnNew(config, burning);

            FireWhirlDamage.Apply(frameIndex, config.SpreadStrength);

            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count);
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
                bool conditionMet = near >= config.DetectCount;

                FireWhirlRegistry.UpdateStrength(v.DisasterId, FireWhirlStrength.RadiusFor(near), near);
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

            FireWhirlRegistry.Add(disasterId, 0, center, FireWhirlStrength.RadiusFor(c.BurningCount), c.BurningCount);
        }

        public void OnMainThreadUpdate()
        {
            FireWhirlFlameFx.Sync();

            // 災害パネルはレベルロード時点ではまだ構築されていないことがある。
            // Install は _button != null で早期 return するので毎フレーム呼んでも安全。
            FireWhirlPanelButton.Install();
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
