using CitiesHarmony.API;
using HarmonyLib;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Harmony パッチの入れ物。**他の機能は読み取りと自前 AI で済ませ、
    /// パッチ面を意図的に最小に保つ。**
    ///
    /// 今あるのは 3 つだけ:
    ///
    /// <list type="number">
    /// <item>③ <c>VortexAI.SimulationStep</c> の Postfix（渦をその場に留める）</item>
    /// <item>② <c>EarthquakeAI.SimulationStep</c> の Prefix/Postfix
    ///   （海溝型のあいだだけ旗を立てる）</item>
    /// <item>② <c>DisasterHelpers.MakeCrack</c> の Prefix
    ///   （海溝型では地面を割らない。<c>TrenchQuakeStepPatch</c> のクラス doc）</item>
    /// </list>
    ///
    /// ★★ <b>この型は③の持ち物ではない。</b> ファイルが <c>FireWhirl/</c> に在るのは
    ///   最初のパッチが③のものだったからで、**②もここに依存している** ——
    ///   <see cref="Install"/> は③と②の両方の <c>OnLevelLoaded</c> から呼ぶ
    ///   （冪等）。片方だけにすると、その機能を外した日に
    ///   <b>もう片方のパッチが黙って効かなくなる</b>。
    /// </summary>
    public static class HarmonyBootstrap
    {
        /// <summary>internal: Assumptions がパッチ所有者を突き合わせて確認するのに使う。</summary>
        internal const string HarmonyId = "jp.disasterplus.mod";

        private static Harmony _harmony;

        /// <summary>DoOnHarmonyReady にコールバックを二重登録しないためのフラグ。</summary>
        private static bool _requested;

        public static bool Installed { get { return _harmony != null; } }

        /// <summary>
        /// パッチ適用を要求する。
        ///
        /// PatchAll をその場で呼ばず HarmonyHelper.DoOnHarmonyReady に預ける。
        /// IsHarmonyInstalled が true でも HarmonyLib のアセンブリがまだ読み込まれていないこと
        /// があり、直接呼ぶと型初期化で落ちる。同じマシンの既存 MOD もこの形で使っている。
        /// </summary>
        public static void Install()
        {
            if (_harmony != null || _requested) return;
            _requested = true;

            if (!HarmonyHelper.IsHarmonyInstalled)
            {
                Log.Warn("CitiesHarmony not installed; fire whirls will drift instead of staying put");
                return;
            }

            HarmonyHelper.DoOnHarmonyReady(PatchAll);
        }

        private static void PatchAll()
        {
            if (_harmony != null) return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(HarmonyBootstrap).Assembly);
                Log.Info("Harmony patches installed");
            }
            catch (System.Exception e)
            {
                _harmony = null;
                Log.Error("Harmony patch failed", e);
            }
        }

        public static void Uninstall()
        {
            _requested = false;
            if (_harmony == null) return;
            try { _harmony.UnpatchAll(HarmonyId); }
            catch (System.Exception e) { Log.Error("Harmony unpatch failed", e); }
            _harmony = null;
        }
    }
}
