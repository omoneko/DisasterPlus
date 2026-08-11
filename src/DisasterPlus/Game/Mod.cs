using ICities;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description { get { return Strings.ModDescription; } }

        /// <summary>
        /// メインメニューの起動中に 1 回だけ呼ばれる（言語切替でも再実行される）。
        /// レベルロード後にしか分からない情報からオプションを組み立ててはいけない。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            // 言語切替でもこのメソッドは再実行される。ここで読み直せばオプション画面が追従する。
            // ただしゲーム内ボタンのツールチップはレベルロード時に一度設定されるだけなので、
            // 次のロードまで前の言語のままになる。
            LocaleLoader.Apply();
            ModSettings.Ensure();

            var fw = helper.AddGroup(Strings.GroupFireWhirl);
            fw.AddCheckbox(Strings.FireWhirlEnabled, ModSettings.FireWhirlEnabled.value,
                v => ModSettings.FireWhirlEnabled.value = v);

            fw.AddSlider(Strings.DetectRadius, 50f, 400f, 10f, ModSettings.DetectRadius.value,
                v => ModSettings.DetectRadius.value = (int)v);

            fw.AddSlider(Strings.DetectCount, 4f, 40f, 1f, ModSettings.DetectCount.value,
                v => ModSettings.DetectCount.value = (int)v);

            fw.AddSlider(Strings.MaxLifetime, 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
                v => ModSettings.MaxLifetimeMinutes.value = (int)v);

            fw.AddSlider(Strings.SpreadStrength, 0f, IgnitionSpread.MaxStrength, 1f,
                ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);

            fw.AddSlider(Strings.MinSeparation, 100f, 800f, 25f, ModSettings.MinSeparation.value,
                v => ModSettings.MinSeparation.value = (int)v);

            var general = helper.AddGroup(Strings.GroupGeneral);
            general.AddCheckbox(Strings.IntensityUnlock, ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);
        }
    }
}
