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

            // Assumptions はレベルロード後に走るので、初回起動時はまだ空。
            // つまり警告は「一度都市を読み込んだ後、次にオプションを開いたとき」に出る。
            // OnSettingsUI はメインメニュー起動時に 1 回しか走らないため、これは避けられない。
            //
            // これが成立するのは Assumptions.Reset()（レベルアンロード時）が結果を
            // 消さないから。ここは必ずアンロードより後に走るので、Reset() でクリアすると
            // LastResults は常に空になり、この警告は原理的に出せなくなる。
            var failures = Assumptions.LastResults;
            bool anyFailed = false;
            for (int i = 0; i < failures.Count; i++) { if (!failures[i].Passed) anyFailed = true; }

            if (anyFailed)
            {
                var warn = helper.AddGroup(Strings.AssumptionsFailedTitle);
                for (int i = 0; i < failures.Count; i++)
                {
                    if (failures[i].Passed) continue;
                    warn.AddGroup("- " + failures[i].Impact);
                }
                warn.AddGroup(Strings.AssumptionsFailedHint);
            }

            if (ModCompat.NaturalDisastersOwned)
            {
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
            }
            else
            {
                helper.AddGroup(Strings.GroupFireWhirl).AddSpace(4);
                // グループ名の下に理由を出す。設定が「消えた」ように見えないようにする。
                helper.AddGroup(Strings.FireWhirlNeedsDlc);
            }

            var general = helper.AddGroup(Strings.GroupGeneral);
            general.AddCheckbox(Strings.IntensityUnlock, ModSettings.IntensityUnlock.value,
                v => ModSettings.IntensityUnlock.value = v);

            // 競合MOD が居るときだけ出す。居ないときはこの設定に意味が無く、
            // 値は保存したまま既定動作（Disaster + が担当）に戻る。
            if (ModCompat.NdrPresent)
            {
                // 強度解放が既定 OFF になっている理由を明示する（仕様 3.3(a)）。
                // 黙って OFF だと「設定が効いていない」と見える。
                helper.AddGroup(Strings.IntensityUnlockHandledByOther);

                var compat = helper.AddGroup(Strings.NdrDetected);

                // ラベル配列は static readonly にしてはいけない。型初期化時の言語で凍結する。
                // 毎回組み直すことで言語切替に追従する。
                string[] owners = { Strings.EarthquakeOwnerOther, Strings.EarthquakeOwnerSelf };

                int current = ModSettings.EarthquakeDamageOwner.value;
                if (current < 0 || current >= owners.Length) current = ModSettings.EarthquakeOwnerOther;

                compat.AddDropdown(Strings.EarthquakeDamageOwner, owners, current,
                    v => ModSettings.EarthquakeDamageOwner.value = v);
            }

            var dbg = helper.AddGroup(Strings.GroupDebug);
            dbg.AddCheckbox(Strings.OverlayEnabled, ModSettings.OverlayEnabled.value,
                v => ModSettings.OverlayEnabled.value = v);

            // ラベル配列は static にしないこと。型初期化時の言語で凍結する。
            string[] keys = { "F9", "F10", "F11", "F12" };
            int[] codes = { (int)UnityEngine.KeyCode.F9, (int)UnityEngine.KeyCode.F10,
                            (int)UnityEngine.KeyCode.F11, (int)UnityEngine.KeyCode.F12 };
            int current2 = 2;
            for (int i = 0; i < codes.Length; i++)
                if (codes[i] == ModSettings.OverlayHotkey.value) current2 = i;

            dbg.AddDropdown(Strings.OverlayHotkey, keys, current2,
                v => ModSettings.OverlayHotkey.value = codes[v]);

            // Assembly-CSharp にも同名の LogChannel (ゲーム側の別物) があるため、
            // using を足すと解決が衝突する。常に完全修飾で参照する。
            //
            // ここに出すのは「実際にそのチャンネルのログを出している機能」だけにする。
            // Log.Diag(key, msg) は General へ委譲されるので General は本物のスイッチだが、
            // FireWhirl チャンネルを付けた呼び出しは 1 件も無い（設計書 5.2 が本フェーズでの
            // 移行を禁じている: 移行すると既定 OFF になり docs/playtest-checklist.md の
            // 手順が壊れる）。チェックボックスだけ置くと「切っても何も変わらない」
            // 死んだ設定になるので、②〜⑤がチャンネル付きログを出すまで UI から外す。
            // ビットと保存キーは公開契約なので消さない（LogChannel.FireWhirl は据え置き）。
            var channels = dbg.AddGroup(Strings.LogChannels);
            channels.AddCheckbox(Strings.LogChannelGeneral,
                DisasterPlus.Core.Diagnostics.LogChannel.IsEnabled(
                    DisasterPlus.Core.Diagnostics.LogChannel.General, ModSettings.LogChannelMask.value),
                v => ModSettings.LogChannelMask.value =
                     v ? (ModSettings.LogChannelMask.value | DisasterPlus.Core.Diagnostics.LogChannel.General)
                       : (ModSettings.LogChannelMask.value & ~DisasterPlus.Core.Diagnostics.LogChannel.General));
        }
    }
}
