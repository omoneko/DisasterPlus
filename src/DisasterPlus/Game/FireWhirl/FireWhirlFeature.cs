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
        /// <summary>FeatureHost.NoteDegraded に渡すキー。Name と必ず同じ文字列にすること。</summary>
        public const string FeatureName = "FireWhirl";

        /// <summary>
        /// 自己申告した Degraded の識別キー。1 機能が複数の理由で Degraded に
        /// なりうるので、回復時に「自分が立てた分だけ」を下ろせるようにする。
        /// </summary>
        private const string EndingStallNote = "endingStall";
        private const string BarrenSpreadNote = "barrenSpread";

        public string Name { get { return FeatureName; } }

        private readonly BurningBuildingScanner _scanner = new BurningBuildingScanner();

        /// <summary>強度は byte。竜巻としては中程度の 60 から始める（表示 6.0）。</summary>
        private const byte SpawnIntensityBase = 60;

        /// <summary>終了処理が終わらない旋風を一度でも報告したか。ログを 1 回に留めるため。</summary>
        private bool _endingStallLogged;

        public void OnLevelLoaded()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            _endingStallLogged = false;
            HarmonyBootstrap.Install();

            // ツール登録は毎レベルロード必要（ToolController.m_tools はレベル毎に再構築される）。
            ToolRegistration.Register<FireWhirlPlacementTool>();
            // ボタンの設置は DisasterPanelBar が 5 個まとめて行う（FeatureHost が呼ぶ）。
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
            FireWhirlRegistry.AdvanceEnding(deltaMinutes);
            CheckEndingStall();

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

            // 「選ばれているのに 1 棟も燃えない」が続いた瞬間を 1 回だけ拾う。
            // m_fireIntensity 直接書込という 3 件目の IL 誤りが再発したときの
            // 唯一の兆候がこれで、例外もログも出ないまま延焼だけが死ぬ。
            if (FireWhirlDamage.ConsumeBarrenAlert())
            {
                Log.Warn("fire spread attempted ignition but lit nothing in "
                         + FireWhirlDamage.BarrenThreshold + " consecutive passes"
                         + " (last pass: candidates=" + FireWhirlDamage.LastCandidates
                         + " selected=" + FireWhirlDamage.LastSelected
                         + " attempted=" + FireWhirlDamage.LastAttempted + ")"
                         + "; BuildingAI.BurnBuilding is refusing every call");
                FeatureHost.NoteDegraded(FeatureName, BarrenSpreadNote,
                    "fire spread lit nothing in " + FireWhirlDamage.BarrenThreshold
                    + " consecutive passes");
            }

            // 延焼が戻ったら申告も取り下げる。Core 側の streak だけを 0 に戻しても
            // オーバーレイのバッジは Degraded のまま残り、「barren passes」の行が
            // 消えているのにセクションだけ赤い、という自己矛盾になる。
            if (FireWhirlDamage.ConsumeBarrenRecovery())
            {
                Log.Info("fire spread ignited again; clearing the barren-spread alert");
                FeatureHost.ClearDegraded(FeatureName, BarrenSpreadNote);
            }

            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count);
        }

        /// <summary>
        /// 終了処理に入ったまま終わらない旋風を検出する。
        ///
        /// これは m_targetPos0 の取り違え（＝終了処理が原理的に発火しない）が
        /// 再発したときのシグネチャそのもの。パッチが当たっているかという
        /// 存在検査は通ってしまうので、実際に終わったかどうかで見るしかない。
        /// </summary>
        private void CheckEndingStall()
        {
            int maxLifetime = ModSettings.MaxLifetimeMinutes.value;
            float framesPerMinute = FeatureHost.FramesPerMinute;
            var views = FireWhirlRegistry.Snapshot();

            for (int i = 0; i < views.Count; i++)
            {
                if (!views[i].Ending) continue;
                if (!EndingStall.IsStuck(views[i].EndingMinutes, maxLifetime, framesPerMinute)) continue;

                if (!_endingStallLogged)
                {
                    _endingStallLogged = true;
                    Log.Warn("fire whirl " + views[i].DisasterId + " has been ending for "
                             + views[i].EndingMinutes.ToString("F1") + " in-game minutes"
                             + " (threshold "
                             + EndingStall.ThresholdMinutes(maxLifetime, framesPerMinute)
                                 .ToString("F1")
                             + " min; a healthy teardown is "
                             + (EndingStall.TeardownFrames / framesPerMinute).ToString("F1")
                             + " min); vanilla teardown never completed");
                }

                FeatureHost.NoteDegraded(FeatureName, EndingStallNote,
                    "a fire whirl is stuck in the ending state");
                return;
            }

            // 詰まりが解消した（あるいは詰まった旋風が回収された）。バッジを下ろす。
            // 下ろさないと、オーバーレイが Degraded のまま「STUCK な旋風は 1 基も無い」
            // 本文を出し続けて自己矛盾する。
            FeatureHost.ClearDegraded(FeatureName, EndingStallNote);
            _endingStallLogged = false;
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
            // その再試行は DisasterPanelBar が 5 個ぶんまとめて持つ（FeatureHost が呼ぶ）。
        }

        public void OnLevelUnloading()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            FireWhirlRegistry.Clear();
            FireWhirlFlameFx.Clear();
            HarmonyBootstrap.Uninstall();
            // ボタンの撤去は FeatureHost.LevelUnloading が DisasterPanelBar.Remove で行う。
            _endingStallLogged = false;
        }

        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.FireWhirlEnabled.value ? "yes" : "no");
            // ③のボタンも①②④⑤と同じ並びに居る（DisasterPanelBar）。以前は
            // (8,8) 固定の 1 個だけ別扱いだったので、その例外が残っていないことを出す。
            b.Line(1, "button", (DisasterPanelBar.IsInstalled(DisasterPanelBar.IdFireWhirl)
                ? "installed" : "not installed") + "  (" + DisasterPanelBar.Placement + ")");
            b.Line(1, "scan", _scanner.DiagnosticSummary());

            var views = FireWhirlRegistry.Snapshot();
            b.Line(1, "active", views.Count.ToString());

            int maxLifetime = ModSettings.MaxLifetimeMinutes.value;
            float framesPerMinute = FeatureHost.FramesPerMinute;

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                string s = "#" + v.DisasterId
                    + "  (" + (int)v.Center.X + "," + (int)v.Center.Z + ")"
                    + "  r=" + (int)v.Radius
                    + "  " + v.ElapsedMinutes.ToString("F1")
                    + "/" + maxLifetime + "min"
                    + "  n=" + v.BurningCount
                    + (v.VehicleId != 0 ? "  pinned" : "  NO VEHICLE")
                    + (v.Manual ? "  manual" : "")
                    + (v.Ending ? "  ending " + v.EndingMinutes.ToString("F1") + "min" : "")
                    + (v.Ending && EndingStall.IsStuck(v.EndingMinutes, maxLifetime, framesPerMinute)
                        ? "  STUCK" : "");
                b.Line(2, s);
            }

            // ★ 炎のシェーダ。**「渦は出ているのに何も見えない」の唯一の手がかり**である。
            //   ④⑤は最初からこの行を持っていて、③だけが持っていなかった
            //   （そして③だけが FAIL を報告する代わりに毎フレーム落ちていた）。
            b.Line(1, "flame material", FireWhirlFlameFx.ShaderDetail);

            // 設計書 7.1 のオーバーレイ例の末尾。「大火災なのに旋風が出ない」の
            // 最有力の原因なので必ず出す。
            b.Line(1, "cooldown", FireWhirlRegistry.CoolingCount.ToString());

            WriteSpreadDiagnostics(b);
        }

        /// <summary>
        /// 延焼の診断。
        ///
        /// ここが無いあいだ、WriteDiagnostics は enabled / scan / active しか出しておらず、
        /// 「延焼が 1 棟も燃やしていない」と「近くに燃やす物が無い」が区別できなかった。
        /// SpreadStrength が 0 なら Apply() は即 return するので、そこも明示する。
        /// </summary>
        private static void WriteSpreadDiagnostics(DiagnosticBuilder b)
        {
            int strength = ModSettings.SpreadStrength.value;
            b.Line(1, "spread strength",
                   strength + (strength <= 0 ? "  (OFF - no ignition at all)" : ""));

            b.Line(1, "spread passes", FireWhirlDamage.Passes.ToString());
            b.Line(2, "last pass",
                   "candidates=" + FireWhirlDamage.LastCandidates
                   + " selected=" + FireWhirlDamage.LastSelected
                   + " attempted=" + FireWhirlDamage.LastAttempted
                   + " refused=" + FireWhirlDamage.LastRefused
                   + " ignited=" + FireWhirlDamage.LastIgnited);
            b.Line(2, "session ignited", FireWhirlDamage.TotalIgnited.ToString());

            if (FireWhirlDamage.BarrenStreak > 0 || FireWhirlDamage.SpreadLooksBroken)
            {
                b.Line(2, "barren passes",
                       FireWhirlDamage.BarrenStreak + "/" + FireWhirlDamage.BarrenThreshold
                       + (FireWhirlDamage.SpreadLooksBroken ? "  SPREAD LOOKS BROKEN" : ""));
            }
        }
    }
}
