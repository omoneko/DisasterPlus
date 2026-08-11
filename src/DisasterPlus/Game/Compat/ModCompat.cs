using ColossalFramework.Plugins;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 競合MOD の検出。起動時に 1 回だけ判定してキャッシュする。
    ///
    /// PluginManager はメインメニューが立ち上がる時点で既に埋まっているので、
    /// OnSettingsUI から参照してよい（SteamHelper.IsDLCOwned と同じ扱い）。
    /// </summary>
    public static class ModCompat
    {
        /// <summary>Natural Disasters Renewal の Workshop ID。</summary>
        private const ulong NdrWorkshopId = 2957578256UL;

        /// <summary>同 MOD のアセンブリ名。ローカル配置・開発コピーには Workshop ID が無いので併用する。</summary>
        private const string NdrAssemblyName = "NaturalDisastersRenewal";

        private static bool _evaluated;
        private static bool _ndrPresent;

        public static bool NdrPresent
        {
            get
            {
                if (!_evaluated) Evaluate();
                return _ndrPresent;
            }
        }

        /// <summary>
        /// Natural Disasters DLC を持っているか。
        ///
        /// SteamHelper.IsDLCOwned は起動時から使えるので、OnSettingsUI
        /// （メインメニューで 1 回だけ実行）から参照してよい。
        /// レベルロード後にしか分からない情報でオプションを組み立ててはいけない。
        /// </summary>
        public static bool NaturalDisastersOwned
        {
            get
            {
                try { return SteamHelper.IsDLCOwned(SteamHelper.DLC.NaturalDisastersDLC); }
                catch (System.Exception e)
                {
                    // 判定できないときは「持っている」に倒す。
                    // 機能を永久に隠す偽陰性より、実行時に諦める偽陽性の方が害が小さい
                    // （DisasterInfo が見つからなければ Task 10 が警告を出して黙って止まる）。
                    Log.Error("DLC check failed; assuming owned", e);
                    return true;
                }
            }
        }

        private static void Evaluate()
        {
            _evaluated = true;
            _ndrPresent = false;

            try
            {
                foreach (var p in PluginManager.instance.GetPluginsInfo())
                {
                    if (p == null || !p.isEnabled) continue;

                    if (p.publishedFileID.AsUInt64 == NdrWorkshopId) { _ndrPresent = true; break; }

                    bool matched = false;
                    try
                    {
                        foreach (var asm in p.GetAssemblies())
                        {
                            if (asm != null && asm.GetName().Name == NdrAssemblyName) { matched = true; break; }
                        }
                    }
                    catch { /* 壊れた MOD のアセンブリ列挙で落ちない */ }

                    if (matched) { _ndrPresent = true; break; }
                }
            }
            catch (System.Exception e)
            {
                // 判定に失敗したら「居ない」に倒す。
                // 偽陽性で機能を隠すより、偽陰性で余分な設定が出る方が害が小さい。
                Log.Error("plugin scan failed; assuming NDR absent", e);
                _ndrPresent = false;
            }

            Log.Info("Natural Disasters Renewal detected: " + _ndrPresent);
        }
    }
}
