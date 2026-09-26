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
    /// Reads Locales/&lt;lang&gt;.txt and overwrites the public static string fields of Strings,
    /// keyed by field name.
    ///
    /// The English defaults are stashed away exactly once at the start and always restored
    /// before applying another language. Otherwise, moving between partially translated
    /// languages leaves the previous language's translations behind.
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

        /// <summary>
        /// The mod folder, once it has been resolved. **It does not change during a session**
        /// (neither the Workshop folder nor a local folder moves while the game is running),
        /// so it is kept. **null is not kept** — it may only be that <c>PluginManager</c> is
        /// not ready yet, and keeping it would create "one early call and it is gone for good".
        /// </summary>
        private static string _modPath;

        /// <summary>
        /// The folder the mod is installed in. Handles both the Workshop and local versions.
        /// null if it cannot be resolved (the caller may retry at the next opportunity).
        ///
        /// ── ★★ <c>GetInstances&lt;Mod&gt;()</c> **always returns empty** (measured from the IL) ────
        ///
        /// Until 2026-08-22, this took the <c>modPath</c> of whichever plugin
        /// <c>p.GetInstances&lt;Mod&gt;()</c> returned at least one instance from.
        /// **It never returns one.** The body of
        /// <c>PluginManager.PluginInfo.GetInstances&lt;T&gt;()</c> is
        ///
        /// <code>
        /// foreach (Type t in assembly.GetExportedTypes())
        ///     if (!t.IsClass || t.IsAbstract) continue;
        ///     Type[] ifaces = t.GetInterfaces();
        ///     if (!ifaces.Contains(typeof(T))) continue;          ★ here
        ///     if (ifaces.Contains(PluginManager.userModType))
        ///         list.Add(this.userModInstance as T);
        ///     else list.Add(CreateInstance&lt;T&gt;(t.GetConstructor(Type.EmptyTypes)));
        /// </code>
        ///
        /// so <b><c>T</c> is looked for among the <u>interfaces</u> the type implements</b>.
        /// <see cref="Mod"/> is a <c>class</c>, so it is in no type's <c>GetInterfaces()</c>
        /// ⇒ always an empty array ⇒ null was always returned.
        ///
        /// There were two visible consequences:
        ///   - <c>Locales\ja.txt</c> was **never read once** (the English defaults stayed)
        ///   - <c>Audio\erupting-volcano.wav</c> was not found, so eruptions were silent
        ///     (the log from the game: "the mod folder could not be resolved")
        ///
        /// ★ The fix was not <c>GetInstances&lt;IUserMod&gt;()</c> but
        ///   <c>PluginInfo.ContainsAssembly(Assembly)</c>. That does nothing but a
        ///   **reference comparison** over <c>m_Assemblies</c> (measured from the IL); it
        ///   neither sweeps types nor creates instances. **It points straight at the plugin
        ///   this DLL is in**, so it depends on neither which interfaces are implemented nor
        ///   <c>isEnabled</c>.
        ///
        /// ★ <c>Assembly.Location</c> cannot be used. <c>PluginManager.LoadPlugin</c> reads
        ///   from a byte array with <c>Assembly.Load(File.ReadAllBytes(path))</c>
        ///   (measured from the IL), so a mod assembly's <c>Location</c> is **an empty string**.
        ///
        /// ★ <c>isEnabled</c> is not consulted. If this code is running then this mod is
        ///   enabled, and besides, <c>get_isEnabled</c> creates a <c>SavedBool</c> and reads
        ///   the settings file (measured from the IL) — **I/O the check does not need**.
        /// </summary>
        public static string ModDirectoryPath()
        {
            if (_modPath != null) return _modPath;

            try
            {
                if (!PluginManager.exists) return null;

                Assembly self = typeof(LocaleLoader).Assembly;
                foreach (var p in PluginManager.instance.GetPluginsInfo())
                {
                    if (p == null) continue;

                    bool mine;
                    try { mine = p.ContainsAssembly(self); }
                    catch { continue; /* do not fall over enumerating a broken mod */ }
                    if (!mine) continue;

                    string path = p.modPath;
                    if (string.IsNullOrEmpty(path)) continue;

                    _modPath = path;
                    return _modPath;
                }
            }
            catch { /* PluginManager not ready yet, and the like. Retry at the next opportunity */ }

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

        /// <summary>Writes en.txt out from the defaults. A hand-written template drifts silently, so it is not used.</summary>
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
