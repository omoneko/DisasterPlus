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
        private const int FireWhirlCheckCount = 4;

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

            // 炎のシェーダ。
            //
            // ★ **③にはこの検査が無かった。** ④と⑤は同じ検査を持っていたので
            //   「シェーダが取れない」を FAIL として綺麗に報告できたが、③は
            //   検査を持たない代わりに new Material(null) で毎フレーム落ちていた
            //   （実機ログに同じ NullReferenceException が 6,938 行）。
            //
            // ★★ **述語に「Standard が取れた」を混ぜない**（④⑤と同じ規律）。
            //   ここが見るのは **粒子系（加算 / アルファブレンド）が取れたか**で、
            //   取れなければ Standard を透過モードにして描く（＝見えるが光らない）。
            //   1 つも取れなければ描かない —— それは名前のほうに出る。
            //
            // ★ 述語は FireWhirlFlameFx が実際に門にしている式そのものである
            //   （同じ ShaderPool を同じ preference で呼ぶ。順序をここへ写すと、
            //   検査が報告する名前と実際に使うシェーダが黙ってずれる）。
            //   **⑤の噴煙も同じ preference なので、この 1 件はあちらの答えでもある。**
            ShaderPick flame = ResolveFlameShader();
            Check("an additive or alpha-blended particle shader resolves for the fire whirl "
                  + "flames, by name or by borrowing the shader off a loaded material "
                  + "(resolved: " + flame.Describe() + ")",
                  "the flames fall back to the Standard shader forced into transparent mode, so "
                  + "they draw but do not glow; if nothing resolves at all they are not drawn. "
                  + "The fire whirl still spins, stays pinned and still spreads fire either way",
                  delegate { return flame.Particle; });

            // DLC 非所持環境では FAIL するのが正常（プレハブごと存在しない）。
            Check("TornadoAI disaster prefab is available",
                  "fire whirls cannot be created. This also FAILs when the Natural Disasters "
                  + "DLC is not owned, which is expected.",
                  delegate { return FireWhirlSpawner.HasTornadoPrefab(); },
                  true);
        }

        /// <summary>
        /// ③の炎が実際に使うシェーダ。**<c>FireWhirlFlameFx.FlameMaterial</c> と
        /// 同じ <c>ShaderPool</c> を同じ preference で呼ぶ。**
        ///
        /// ★ **自分で try/catch する。** ここは検証の*名前*を組み立てるために
        ///   <c>Check()</c> の外側（＝あの try/catch の外）で呼ばれる。
        ///   <c>Assumptions.Run()</c> は <c>DisasterPlusLoading.OnLevelLoaded</c> から
        ///   素で呼ばれているので、ここから例外を投げるとレベルロードが壊れる
        ///   （④の <c>ResolveCloudShader</c> が同じ理由で同じ形をしている）。
        /// </summary>
        private static ShaderPick ResolveFlameShader()
        {
            try
            {
                return ShaderPool.Resolve(ShaderPreference.Additive);
            }
            catch
            {
                return new ShaderPick(null, false, false, false);
            }
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
