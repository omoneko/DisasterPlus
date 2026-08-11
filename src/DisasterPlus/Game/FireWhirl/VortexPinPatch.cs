using DisasterPlus.Core.Common;
using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 渦車両をその場に留める。
    ///
    /// IL 実測（設計書 付録 A-1）で分かった VortexAI.SimulationStep(6 引数) の順序:
    ///   1. Frame.m_position を書く
    ///   2. m_targetPos0 までの距離を測る
    ///   3. ArriveAtDestination が true なら DeactivateNow + Unspawn
    ///   4. Frame.m_velocity を再計算
    ///   5. Frame.m_position = m_position + m_velocity * dt   ← 実際の移動
    ///   6. AddWind / DestroyStuff / BurnGround / UpgradeBuildings
    ///
    /// Postfix で m_position を発生地点に戻せば、5 の積算が毎ステップ帳消しになる。
    ///
    /// m_velocity は書き換えない。4 で毎ステップ再計算されるので無意味であり、
    /// 6 の AddWind が向きに使っているのでゼロにすると風の演出が死ぬ。
    /// </summary>
    [HarmonyPatch(typeof(VortexAI), "SimulationStep",
        new[] { typeof(ushort), typeof(Vehicle), typeof(Vehicle.Frame),
                typeof(ushort), typeof(Vehicle), typeof(int) },
        new[] { ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref,
                ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Normal })]
    public static class VortexPinPatch
    {
        public static void Postfix(ushort vehicleID, ref Vehicle.Frame frameData)
        {
            // 自分が作った渦だけを固定する。バニラの竜巻には一切触らない。
            Vec3 center;
            if (!FireWhirlRegistry.TryGetPinnedCenter(vehicleID, out center)) return;

            // y は書き戻さない。バニラが SampleRawHeightSmoothWithWater で地形に合わせているので、
            // 水平位置だけ固定すれば地面から浮いたり埋まったりしない。
            frameData.m_position = new Vector3(center.X, frameData.m_position.y, center.Z);
        }
    }
}
