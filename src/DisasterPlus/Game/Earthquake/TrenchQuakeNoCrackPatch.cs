using DisasterPlus.Core.Common;
using HarmonyLib;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// **海溝型地震では地面を割らない。** sim スレッド専用。
    ///
    /// ── 実機報告（2026-08-22）────────────────────────────────────
    ///
    /// &gt; 海溝型地震でも断層（震源地の地形変更）が発生しています。海溝型地震の
    /// &gt; 震源は海の沖の方なので、地震の揺れは発生する一方で断層は発生させないで
    /// &gt; ください。
    ///
    /// そのとおりで、**プレート境界の破壊は海底の数十 km 下**で起きる。
    /// 地表に見える裂け目はできない（内陸の直下型・断層型はできる）。
    ///
    /// ── ★★ どこを止めるか（IL で確かめた）──────────────────────────
    ///
    /// 地面を割っているのは <c>EarthquakeAI.SimulationStep</c> の中の
    /// <c>DisasterHelpers.MakeCrack</c> ただ 1 箇所である（IL_03AD）。
    ///
    /// <b>止めるのはそれ「だけ」である。</b> 同じ <c>SimulationStep</c> は
    /// <c>DestroyBuildings</c>（IL_0298）・<c>DestroyNetSegments</c>（IL_02B6）・
    /// <c>SplashWater</c>（IL_024F）・<c>DetectDisaster</c> も呼んでいる。
    /// 揺れも被害も残すのが依頼なので、そちらには 1 つも触らない。
    ///
    /// ★★ <b><c>m_crackLength</c> / <c>m_crackWidth</c> を 0 にする手は採らない。</b>
    ///   あの 2 つは同じメソッドの中で<b>被害を配る帯の長さ</b>にも使われており
    ///   （IL_017E / IL_018A で強度を掛けた値が、そのあとの破壊のループを回す）、
    ///   0 にすると<b>揺れ以外の被害もまるごと消える</b>。
    ///   しかも <c>IsStillClearing</c> / <c>CanAffectAt</c> /
    ///   <c>GetMinimumEdgeDistance</c> / <c>GetPosition</c> / <c>UpdateHazardMap</c> が
    ///   同じフィールドを読むので、プレハブを書き換えると
    ///   <b>同時に走っているバニラの地震まで壊れる</b>。
    ///
    /// ── なぜ 2 つのパッチが要るのか ────────────────────────────────
    ///
    /// <c>MakeCrack</c> は <c>static</c> で、**どの災害のための呼び出しか分からない**
    /// （引数は座標と幅と深さだけ）。そこで
    ///
    /// <code>
    /// EarthquakeAI.SimulationStep の Prefix   → 「今は海溝型の中だ」を立てる
    /// DisasterHelpers.MakeCrack の Prefix     → 立っていたら本体を飛ばす
    /// EarthquakeAI.SimulationStep の Postfix  → 必ず下ろす
    /// </code>
    ///
    /// ★ 旗は <c>[ThreadStatic]</c> にしない。<c>SimulationStep</c> は sim スレッド
    ///   からしか呼ばれず（バニラの規約）、素の <c>static</c> で足りる。
    ///   <b>Postfix は例外が出ても走る</b>（Harmony の既定）ので、旗が立ちっぱなしに
    ///   なって<b>バニラの地震まで割れなくなる</b>ことは無い。
    /// </summary>
    [HarmonyPatch(typeof(EarthquakeAI), "SimulationStep",
        new[] { typeof(ushort), typeof(DisasterData) },
        new[] { ArgumentType.Normal, ArgumentType.Ref })]
    public static class TrenchQuakeStepPatch
    {
        /// <summary>
        /// 今まさに海溝型地震の <c>SimulationStep</c> の中か。
        /// <see cref="TrenchQuakeNoCrackPatch"/> だけが読む。
        /// </summary>
        internal static bool InTrenchQuake;

        /// <summary>直近に裂け目を止めた回数（診断用）。</summary>
        internal static int SuppressedCracks;

        public static void Prefix(ushort disasterID)
        {
            InTrenchQuake = TrenchQuakeSlot.IsTrenchQuake(disasterID);
        }

        public static void Postfix()
        {
            // ★★ **必ず下ろす。** 立ったままだと、次に走るバニラの地震も割れなくなる。
            InTrenchQuake = false;
        }
    }

    /// <summary>
    /// 地面を割る本体を、海溝型地震のあいだだけ飛ばす。
    /// <see cref="TrenchQuakeStepPatch"/> のクラス doc に全部書いてある。
    /// </summary>
    [HarmonyPatch(typeof(DisasterHelpers), "MakeCrack",
        new[] { typeof(Vector2), typeof(Vector2), typeof(float), typeof(float) })]
    public static class TrenchQuakeNoCrackPatch
    {
        /// <summary>false を返すと本体が走らない（Harmony の Prefix の約束）。</summary>
        public static bool Prefix()
        {
            if (!TrenchQuakeStepPatch.InTrenchQuake) return true;

            TrenchQuakeStepPatch.SuppressedCracks++;

            // ★ 毎フレームの経路なので、ログは最初の 1 回だけ。
            if (TrenchQuakeStepPatch.SuppressedCracks == 1)
            {
                Log.Info("trench earthquake: the terrain crack is suppressed on purpose "
                         + "(a megathrust ruptures tens of km below the sea floor; it does "
                         + "not open a fissure you can see). The shaking and the damage "
                         + "are unaffected");
            }

            return false;
        }
    }
}
