using System;
using System.Reflection;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="Assumptions"/> のうち③火災旋風 の前提。
    ///
    /// **このファイルには検証しか置かない。** <c>Check</c> / <c>SetResult</c> /
    /// <c>HasField</c> / <c>_gate</c> / <c>_results</c> は本体側の private のままで、
    /// partial なので可視性を 1 つも上げずに使える（分割の要件そのもの）。
    ///
    /// 件数は <see cref="FireWhirlCheckCount"/> がこのファイルの中で宣言する。
    /// **検証を足したらここも増やすこと** —— 本体の <c>TotalCheckCount</c> は
    /// これらの和である。
    /// </summary>
    public static partial class Assumptions
    {
        /// <summary>このファイルが持つ検証の数。</summary>
        private const int FireWhirlCheckCount = 3;

        private static void RunFireWhirl()
        {
            Check("VortexAI.SimulationStep(6) is patched",
                  "fire whirls will drift instead of staying put",
                  delegate { return HarmonyBootstrap.Installed && VortexStepIsPatched(); });

            Check("BuildingAI.BurnBuilding is resolvable",
                  "fire spread will not work",
                  delegate
                  {
                      return typeof(BuildingAI).GetMethod("BurnBuilding",
                          BindingFlags.Public | BindingFlags.Instance,
                          null,
                          new Type[]
                          {
                              typeof(ushort), typeof(Building).MakeByRefType(),
                              typeof(InstanceManager.Group), typeof(bool)
                          },
                          null) != null;
                  });

            // DLC 非所持環境では FAIL するのが正常（プレハブごと存在しない）。
            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created. This also FAILs when the Natural Disasters "
                  + "DLC is not owned, which is expected.",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); },
                  true);
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
            if (info == null || info.Postfixes == null) return false;

            // 「誰かの postfix が載っている」ではなく「自分の postfix が載っている」を見る。
            // 同じ private overload に別 MOD が偶然 postfix を当てていた場合、前者だと
            // 自分のパッチが無言で失敗していてもマスクされて PASS になってしまう。
            for (int i = 0; i < info.Postfixes.Count; i++)
            {
                if (info.Postfixes[i].owner == HarmonyBootstrap.HarmonyId) return true;
            }
            return false;
        }
    }
}
