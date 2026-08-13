using System;
using System.Collections.Generic;
using System.Reflection;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 設計書 付録A の前提を、実行時に実際のゲームへ問い合わせて照合する。
    ///
    /// なぜ要るか: ③の実装中、IL から読んだ前提が 3 件も誤っていた
    /// (DAYTIME_FRAMES の 4 倍ズレ、m_targetPos0 の意味、m_fireIntensity 直接書込)。
    /// 共通点は「前提が破れても何も起きない」こと。ゲームは平然と動き、
    /// 挙動だけが静かに違う。ここで名指しでログに出すのが唯一の防波堤になる。
    ///
    /// 前提を 1 ファイルに集約しているのは、付録A と突き合わせて監査するため。
    /// ②〜⑤で前提が増えたらここに足すこと。
    /// </summary>
    public static class Assumptions
    {
        private static readonly List<AssumptionResult> _results = new List<AssumptionResult>();
        private static bool _ran;

        public static IList<AssumptionResult> LastResults { get { return _results; } }

        public static void Reset()
        {
            _results.Clear();
            _ran = false;
        }

        /// <summary>
        /// レベルロード完了後に 1 回だけ呼ぶ。起動時ではないのは、
        /// Harmony の適用状況と prefab の解決を見る必要があるため。
        /// </summary>
        public static void Run()
        {
            if (_ran) return;
            _ran = true;
            _results.Clear();

            Check("SimulationManager.DAYTIME_FRAMES == 65536",
                  "all in-game durations will be wrong",
                  delegate { return SimulationManager.DAYTIME_FRAMES == 65536; });

            Check("VortexAI.SimulationStep(6) is patched",
                  "fire whirls will drift instead of staying put",
                  delegate { return HarmonyBootstrap.Installed && VortexStepIsPatched(); });

            Check("BuildingAI.BurnBuilding is resolvable",
                  "fire spread will not work",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("BurnBuilding",
                          BindingFlags.Public | BindingFlags.Instance) != null;
                  });

            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); });

            Check("Disasters panel intensity slider is reachable",
                  "disaster intensity cannot be unlocked to 25.5",
                  delegate { return IntensityUnlock.SliderReachable(); });

            Report();
        }

        private static bool VortexStepIsPatched()
        {
            // Harmony が実際にこのメソッドを持っているかを見る。
            // [HarmonyPatch] の引数が実メソッドと 1 つでも食い違うと
            // パッチは無言で当たらず、MOD は正常に見えたまま竜巻だけが流れる。
            var target = typeof(VortexAI).GetMethod("SimulationStep",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[]
                {
                    typeof(ushort), typeof(Vehicle).MakeByRefType(),
                    typeof(Vehicle.Frame).MakeByRefType(), typeof(ushort),
                    typeof(Vehicle).MakeByRefType(), typeof(int)
                },
                null);
            if (target == null) return false;

            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            return info != null && info.Postfixes != null && info.Postfixes.Count > 0;
        }

        private static void Check(string name, string impact, Func<bool> predicate)
        {
            bool passed;
            string detail = impact;
            try
            {
                passed = predicate();
            }
            catch (Exception e)
            {
                // 検証そのものが落ちても起動を壊さない。FAIL として扱う。
                passed = false;
                detail = impact + " (check threw " + e.GetType().Name + ")";
            }
            _results.Add(new AssumptionResult(name, passed, passed ? "" : detail));
        }

        private static void Report()
        {
            int passed = 0, failed = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Passed) passed++; else failed++;
            }

            Log.Info("ASSUMPTIONS  " + passed + " passed, " + failed + " FAILED");
            for (int i = 0; i < _results.Count; i++)
            {
                var a = _results[i];
                if (a.Passed)
                {
                    Log.Info("  PASS  " + a.Name);
                }
                else
                {
                    Log.Warn("  FAIL  " + a.Name);
                    Log.Warn("        -> " + a.Impact);
                }
            }
        }
    }
}
