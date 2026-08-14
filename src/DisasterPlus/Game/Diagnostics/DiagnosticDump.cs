using System;
using System.IO;
using System.Text;
using DisasterPlus.Core.Diagnostics;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 状態を 1 ファイルに書き出す。オーバーレイと同じ DiagnosticFormatter を使うので、
    /// 画面に見えているものとファイルの内容が食い違わない。
    /// </summary>
    public static class DiagnosticDump
    {
        public const string FileName = "DisasterPlus-diagnostics.txt";

        public static void Write()
        {
            try
            {
                // 表示していなくてもダンプできるよう、その場で 1 回収集する。
                var report = FeatureHost.BuildReport();
                DiagnosticsHub.Publish(report);

                var sb = new StringBuilder();
                sb.AppendLine("Disaster + diagnostics");
                sb.AppendLine("generated at frame " + SimulationManager.instance.m_currentFrameIndex);
                sb.AppendLine();

                foreach (string line in DiagnosticFormatter.Format(report))
                {
                    sb.AppendLine(line);
                }

                string dir = LocaleLoader.ModDirectoryPath();
                if (string.IsNullOrEmpty(dir))
                {
                    Log.Warn("diagnostics dump skipped: mod directory not resolved");
                    return;
                }

                string path = Path.Combine(dir, FileName);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Log.Info("diagnostics written to " + path);
            }
            catch (Exception e)
            {
                Log.Error("diagnostics dump failed", e);
            }
        }
    }
}
