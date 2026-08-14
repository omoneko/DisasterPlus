using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework.Globalization;
using ColossalFramework.Plugins;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Locales/&lt;lang&gt;.txt を読み、Strings の public static string フィールドを
    /// フィールド名をキーに上書きする。
    ///
    /// 英語の既定値を最初に 1 回だけ退避し、別言語を適用する前に必ず復元する。
    /// そうしないと部分翻訳の言語を行き来したとき、前の言語の訳が残る。
    /// </summary>
    public static class LocaleLoader
    {
        private static Dictionary<string, string> _englishDefaults;
        private static string _appliedLanguage;

        private static FieldInfo[] Fields()
        {
            return typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.Static);
        }

        private static void CaptureDefaults()
        {
            if (_englishDefaults != null) return;
            _englishDefaults = new Dictionary<string, string>();
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                _englishDefaults[f.Name] = (string)f.GetValue(null);
            }
        }

        private static void RestoreDefaults()
        {
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                string v;
                if (_englishDefaults.TryGetValue(f.Name, out v)) f.SetValue(null, v);
            }
        }

        /// <summary>MOD の配置フォルダ。Workshop 版とローカル版の両方に対応する。</summary>
        public static string ModDirectoryPath()
        {
            foreach (var p in PluginManager.instance.GetPluginsInfo())
            {
                if (p == null || !p.isEnabled) continue;
                try
                {
                    foreach (var inst in p.GetInstances<Mod>())
                    {
                        if (inst != null) return p.modPath;
                    }
                }
                catch { /* 壊れた MOD の列挙で落ちない */ }
            }
            return null;
        }

        public static void Apply()
        {
            try
            {
                CaptureDefaults();

                string lang = LocaleManager.exists ? LocaleManager.instance.language : "en";
                if (string.IsNullOrEmpty(lang)) lang = "en";
                if (lang == _appliedLanguage) return;

                RestoreDefaults();

                if (lang == "en")
                {
                    // RestoreDefaults() is pure in-memory reflection (no I/O), so there is nothing
                    // left that can fail here. Safe to mark "applied" immediately.
                    _appliedLanguage = lang;
                    return;
                }

                // IMPORTANT: do NOT set _appliedLanguage until the overlay below has fully loaded.
                // Every return between here and the end of the read loop is a "could not apply yet"
                // outcome, not a "nothing to apply" outcome — _appliedLanguage must stay whatever it
                // was so the next Apply() call (next OnSettingsUI re-entry) retries from scratch.
                // If we set it early and then a later step throws (locked file, transient I/O,
                // PluginManager not ready yet), "if (lang == _appliedLanguage) return;" above would
                // permanently skip retrying that language for the rest of the session.
                string dir = ModDirectoryPath();
                if (dir == null) return;   // couldn't resolve our own mod dir yet; retry next call

                string path = Path.Combine(Path.Combine(dir, "Locales"), lang + ".txt");
                // File.Exists() also swallows I/O errors as "false", so a transient lock and a
                // genuinely untranslated language look the same here. Treat both as retryable
                // (not applied) rather than sticking on a false negative — Apply() only runs on
                // language-change events, so the extra check on every retry is essentially free.
                if (!File.Exists(path)) return;

                var byName = new Dictionary<string, FieldInfo>();
                foreach (var f in Fields())
                {
                    if (f.FieldType == typeof(string)) byName[f.Name] = f;
                }

                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");

                    FieldInfo f;
                    if (byName.TryGetValue(key, out f)) f.SetValue(null, value);
                }

                // Overlay fully loaded without error: only now is it safe to mark this language
                // as applied and let future calls short-circuit.
                _appliedLanguage = lang;
            }
            catch (Exception e)
            {
                Log.Error("locale load failed", e);
            }
        }

        /// <summary>en.txt を既定値から書き出す。手書きテンプレートは黙って乖離するので使わない。</summary>
        public static void WriteTemplate(string path)
        {
            CaptureDefaults();
            var sb = new StringBuilder();
            sb.AppendLine("# Disaster + - English template (generated).");
            sb.AppendLine("# Copy to <lang>.txt and translate the right-hand side. Use \\n for line breaks.");
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(string)) continue;
                sb.AppendLine(f.Name + " = " + ((string)f.GetValue(null)).Replace("\n", "\\n"));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
