using ICities;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    public class Mod : IUserMod
    {
        public string Name { get { return "Disaster +"; } }

        public string Description
        {
            get { return "Adds realistic disaster phenomena: fire whirls, typhoons, volcanoes and hazard visualisation."; }
        }

        /// <summary>
        /// メインメニューの起動中に 1 回だけ呼ばれる（言語切替でも再実行される）。
        /// レベルロード後にしか分からない情報からオプションを組み立ててはいけない。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            ModSettings.Ensure();

            var fw = helper.AddGroup("Fire whirl");
            fw.AddCheckbox("Enable fire whirls", ModSettings.FireWhirlEnabled.value,
                v => ModSettings.FireWhirlEnabled.value = v);

            fw.AddSlider("Detection radius (m)", 50f, 400f, 10f, ModSettings.DetectRadius.value,
                v => ModSettings.DetectRadius.value = (int)v);

            fw.AddSlider("Buildings required", 4f, 40f, 1f, ModSettings.DetectCount.value,
                v => ModSettings.DetectCount.value = (int)v);

            fw.AddSlider("Maximum lifetime (in-game minutes)", 1f, 60f, 1f, ModSettings.MaxLifetimeMinutes.value,
                v => ModSettings.MaxLifetimeMinutes.value = (int)v);

            fw.AddSlider("Fire spread strength (0 = off)", 0f, IgnitionSpread.MaxStrength, 1f,
                ModSettings.SpreadStrength.value, v => ModSettings.SpreadStrength.value = (int)v);

            fw.AddSlider("Minimum separation (m)", 100f, 800f, 25f, ModSettings.MinSeparation.value,
                v => ModSettings.MinSeparation.value = (int)v);

            var general = helper.AddGroup("General");
            general.AddCheckbox("Unlock disaster intensity up to 25.5", ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);
        }
    }
}
