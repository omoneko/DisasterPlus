using System.Collections.Generic;
using ColossalFramework;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// ③火災旋風。密集火災を検出して竜巻を生成し、その場に留めて延焼を撒く。
    ///
    /// ── ★★ 火災旋風はプレイヤーが起こすものではない ────────────────────
    ///
    /// 以前は災害パネルに③のタイルがあり、<c>FireWhirlPlacementTool</c> で
    /// クリック地点に強制発生させられた。**その経路は撤去した**（設計書 §5.3）。
    /// 火災旋風は大火災の**結果**として自然に生まれる現象であって、召喚できる
    /// ものではない、というのが本 MOD の立場である。したがって
    /// <see cref="TrySpawnNew"/> が唯一の発生経路になった。
    ///
    /// 経路が 1 本になった代償は「既定の状態が『何も起きない』になった」ことで、
    /// 実機テストではそれが「壊れているのか、まだ火が足りないのか分からない」に
    /// 直結する（実際に <c>DIAG fireWhirl: burning=0 active=0</c> だけが出た
    /// セッションの報告がある）。だから**発生条件の不足そのものを診断に出す** ——
    /// <see cref="DisasterPlus.Core.FireWhirl.FireWhirlProspect"/> がその値で、
    /// <see cref="_prospect"/> に毎 tick 控えて <see cref="WriteDiagnostics"/> が出す。
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
        /// <summary>
        /// 渦を作るときの災害強度。
        ///
        /// ── ★★ 3 倍にした（2026-08-22、所有者の指示「火災旋風の竜巻を 3 倍に」）──
        ///
        /// <b>渦の見かけの大きさは強度そのものである。</b> IL 実測
        /// （<c>VortexAI.RenderExtraStuff</c>、IL_00C0〜00C6）:
        ///
        /// <code>
        ///   scale = DisasterData.m_intensity * 0.01454545      // = 強度 / 68.75
        ///   ... Mathf.Max(m_destructionRadiusMax, m_upgradeRadiusMax) と組み合わせる
        /// </code>
        ///
        /// 60 では 0.87 倍にしかならなかった。180 なら 2.62 倍で、**ちょうど 3 倍**である。
        ///
        /// ★ <b>壊す範囲は 3 倍にならない。</b> 破壊半径はプレハブ側の
        ///   <c>m_destructionRadiusMin/Max</c> で頭打ちなので、大きくなるのは見た目だけ
        ///   （火災旋風自身の被害は <c>FireWhirlDamage</c> が別に決めている）。
        ///
        /// ★ 生成位置は <c>targetPosition</c> から <c>強度×10 + 400</c> ＝ 2200 m
        ///   離れた点である（<c>TornadoAI.ActivateDisaster</c>）。
        ///   <c>FireWhirlPinner.MaxAttachDistance</c>（3000 m）の内側に収まっている ——
        ///   <b>ここを上げるときは必ずあちらも確かめること。</b>超えると渦が
        ///   永久に紐づかず、追跡不能なドリフト竜巻になる。
        ///
        /// ★ 255 を超えないこと（<c>m_intensity</c> は byte）。
        /// </summary>
        private const byte SpawnIntensityBase = 180;

        /// <summary>終了処理が終わらない旋風を一度でも報告したか。ログを 1 回に留めるため。</summary>
        private bool _endingStallLogged;

        /// <summary>
        /// 直近の判定パスが見た「発生条件の充足ぐあい」。**sim スレッドだけが読み書きする**
        /// （<see cref="OnSimulationTick"/> が書き、<c>WriteDiagnostics</c> が読む。
        /// どちらも sim スレッドなのでロックは要らない）。
        /// </summary>
        private FireWhirlProspect _prospect;

        /// <summary>この tick に判定パスを回したか。false のときの <see cref="_prospect"/> は古い。</summary>
        private bool _prospectFresh;

        public void OnLevelLoaded()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            _endingStallLogged = false;
            _prospect = new FireWhirlProspect();
            _prospectFresh = false;
            HarmonyBootstrap.Install();

            // ★ ここに ToolRegistration.Register<...>() は無い。③に配置ツールは無く、
            //   プレイヤーが火災旋風を起こす経路も無い（クラス doc）。
            // ボタンの設置は DisasterPanelBar が行う（FeatureHost が呼ぶ）。③のタイルは無い。
        }

        public void OnSimulationTick(uint frameIndex, float deltaMinutes)
        {
            // Natural Disasters DLC が無いと竜巻の DisasterInfo が存在しない。
            // FindTornadoInfo はその都度警告を出すので、DLC 無しの都市では毎 tick 呼ばない。
            if (!ModCompat.NaturalDisastersOwned) { _prospectFresh = false; return; }

            // ★★ **バニラの竜巻をランダム抽選から外す**（所有者の指示）。
            //    冪等で、状態が変わったときしか何も書かない。プレハブが読み込まれる
            //    のはレベルロードのあとなので、OnLevelLoaded ではなくここで呼ぶ ——
            //    ロード直後は LoadedCount が 0 のことがある。
            VanillaTornadoSuppressor.Apply(ModSettings.NoVanillaTornado.value);

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
                _prospectFresh = false;
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

            // ★ 「何も起きていない」を 1 行で読み解けるようにする。burning=0 active=0 だけを
            //   出していた頃は、これが「まだ火が足りない」なのか「壊れている」なのか
            //   実機テストから判断できなかった（クラス doc）。
            Log.Diag("fireWhirl",
                "burning=" + burning.Count + " active=" + FireWhirlRegistry.Count
                + " densest=" + _prospect.DensestCount + "/" + _prospect.RequiredCount
                + " cooldown=" + FireWhirlRegistry.CoolingCount);
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
            // ★ 判定と「なぜ出なかったか」は同じ 1 パスで受け取る。別に数え直すと、
            //   診断が本判定と食い違う（FireWhirlProspect のクラス doc）。
            var candidates = FireWhirlDetector.Detect(burning, config,
                                                      FireWhirlRegistry.Centers(), out _prospect);
            _prospectFresh = true;
            if (candidates.Count == 0) return;

            // 1 tick に 1 基まで。連鎖的に湧いて都市が一瞬で消えるのを防ぐ。
            var c = candidates[0];

            float y = TerrainManager.instance.SampleDetailHeight(new Vector3(c.Center.X, 0f, c.Center.Z));
            var center = new Vec3(c.Center.X, y, c.Center.Z);

            ushort disasterId;
            if (!FireWhirlSpawner.TrySpawn(center, SpawnIntensityBase, out disasterId)) return;

            FireWhirlRegistry.Add(disasterId, 0, center,
                FireWhirlStrength.RadiusFor(c.BurningCount), c.BurningCount);
        }

        public void OnMainThreadUpdate()
        {
            FireWhirlFlameFx.Sync();

            // ★ ③に災害パネルのタイルは無い（クラス doc）。ここで設置の再試行を
            //   することも、DisasterPanelBar に③の行があることも、もう無い。
        }

        public void OnLevelUnloading()
        {
            _scanner.Reset();
            FireWhirlSpawner.Reset();
            FireWhirlDamage.Reset();
            FireWhirlRegistry.Clear();
            FireWhirlFlameFx.Clear();
            HarmonyBootstrap.Uninstall();
            // ★ 控えを捨てるだけ。値は書き戻さない —— 次のロードで
            //   DisasterManager.InitializeProperties が計算し直す（あちらの doc）。
            VanillaTornadoSuppressor.Forget();
            // ボタンの撤去は FeatureHost.LevelUnloading が DisasterPanelBar.Remove で行う。
            _endingStallLogged = false;
        }

        public void WriteDiagnostics(DiagnosticBuilder b)
        {
            b.Line(1, "enabled", ModSettings.FireWhirlEnabled.value ? "yes" : "no");
            // ★ ③に災害パネルのタイルは無い。プレイヤーが起こす経路が無いことを
            //   診断でも名乗る（「ボタンが出ていない＝壊れている」と読まれないため）。
            b.Line(1, "trigger", "natural only - a fire whirl cannot be placed by hand");

            // ★ バニラの竜巻を止めているかを必ず名乗る。**「竜巻が起きない」は
            //   壊れているのか設定なのか、これが無いと区別できない。**
            b.Line(1, "vanilla tornado", ModSettings.NoVanillaTornado.value
                ? (VanillaTornadoSuppressor.Suppressing
                    ? "suppressed (" + VanillaTornadoSuppressor.SuppressedCount
                      + " prefab(s) removed from the random draw)"
                    : "NOT suppressed yet"
                      + (VanillaTornadoSuppressor.Detail != null
                         ? " (" + VanillaTornadoSuppressor.Detail + ")" : ""))
                : "allowed (setting)");
            b.Line(1, "scan", _scanner.DiagnosticSummary());
            WriteConditionDiagnostics(b);

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
        /// **「まだ火が足りない」と「壊れている」を見分けるための行。**
        ///
        /// ③は自然発生しか経路を持たないので、実機テストの既定の状態は「何も起きない」で
        /// ある。その状態で出せる情報が <c>burning=0 active=0</c> だけだと、テスターは
        /// 「どれだけ燃やせばいいのか」も「そもそも動いているのか」も判断できない ——
        /// 実際にそれで③の修正が確認できないまま 1 セッションが終わっている。
        ///
        /// 出す順序は**プレイヤーが動かせるものから**: 必要条件 → 今どこまで来ているか →
        /// 抑制（クールダウン／離隔）→ 前提（DLC・prefab）。
        /// </summary>
        private void WriteConditionDiagnostics(DiagnosticBuilder b)
        {
            var config = ModSettings.ToFireWhirlConfig();

            b.Line(1, "requirement",
                   config.DetectCount + " buildings burning within "
                   + config.DetectRadius.ToString("F0") + " m of each other"
                   + "  (min separation " + config.MinSeparation.ToString("F0")
                   + " m from a live or cooling fire whirl)");

            if (!ModCompat.NaturalDisastersOwned)
            {
                // ★ 前提が無いときは条件の話をしない。ここで「火が足りない」と出すと、
                //   DLC が無い環境のテスターが永久に火を増やすことになる。
                b.Line(1, "conditions", "not evaluated: " + Strings.FireWhirlNeedsDlc);
                return;
            }

            if (!ModSettings.FireWhirlEnabled.value)
            {
                b.Line(1, "conditions", "not evaluated: fire whirls are switched off in the settings");
                return;
            }

            if (!_prospectFresh)
            {
                b.Line(1, "conditions", "not evaluated yet (no simulation tick since the city loaded)");
                return;
            }

            b.Line(1, "conditions", _prospect.Describe());
            if (_prospect.DensestCount > 0)
            {
                b.Line(2, "densest group",
                       _prospect.DensestCount + "/" + _prospect.RequiredCount
                       + " at (" + _prospect.DensestCentre.X.ToString("F0")
                       + "," + _prospect.DensestCentre.Z.ToString("F0") + ")");
            }
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
