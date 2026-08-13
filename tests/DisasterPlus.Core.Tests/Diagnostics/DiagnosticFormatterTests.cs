using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class DiagnosticFormatterTests
    {
        private static string Joined(DiagnosticReport r)
        {
            return string.Join("\n", DiagnosticFormatter.Format(r).ToArray());
        }

        [Fact]
        public void EmptyReport_ProducesNoCrashAndNoSections()
        {
            var lines = DiagnosticFormatter.Format(new DiagnosticReport(null, null, null));
            Assert.NotNull(lines);
        }

        [Fact]
        public void NullReport_ProducesEmptyList()
        {
            // オーバーレイは publish 前に一度描画されうる。
            var lines = DiagnosticFormatter.Format(null);
            Assert.NotNull(lines);
            Assert.Empty(lines);
        }

        [Fact]
        public void HeaderLines_AppearBeforeSections()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "DLC", "ND ok") },
                null,
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("Fire whirl", FeatureHealth.Healthy, null, null)
                });

            string text = Joined(r);
            Assert.True(text.IndexOf("DLC") < text.IndexOf("Fire whirl"));
        }

        [Fact]
        public void LabelAndValue_AppearOnTheSameLine()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "scan", "burning 34") },
                null, null);
            Assert.Contains("scan", Joined(r));
            Assert.Contains("burning 34", Joined(r));
        }

        [Fact]
        public void Indent_ProducesLeadingSpaces()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine>
                {
                    new DiagnosticLine(0, "a", "1"),
                    new DiagnosticLine(2, "b", "2"),
                },
                null, null);

            var lines = DiagnosticFormatter.Format(r);
            string deep = lines.Find(l => l.Contains("b"));
            string shallow = lines.Find(l => l.Contains("a"));
            Assert.NotNull(deep);
            Assert.NotNull(shallow);
            int deepSpaces = deep.Length - deep.TrimStart().Length;
            int shallowSpaces = shallow.Length - shallow.TrimStart().Length;
            Assert.True(deepSpaces > shallowSpaces, "indent did not increase");
        }

        [Fact]
        public void EmptyValue_StillRendersLabel()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "cooldown", "") }, null, null);
            Assert.Contains("cooldown", Joined(r));
        }

        [Fact]
        public void LongValue_IsTruncated()
        {
            string huge = new string('x', DiagnosticFormatter.MaxValueLength + 200);
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "k", huge) }, null, null);

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                Assert.True(line.Length < DiagnosticFormatter.MaxValueLength + 60,
                    "line not truncated: " + line.Length);
            }
        }

        [Fact]
        public void NewlinesInValue_DoNotBreakLineStructure()
        {
            // 例外メッセージには改行が入る。1 行 = 1 要素を崩さないこと。
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "err", "line1\nline2\r\nline3") },
                null, null);

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                Assert.DoesNotContain("\n", line);
                Assert.DoesNotContain("\r", line);
            }
        }

        [Fact]
        public void AssumptionSummary_ShowsCounts()
        {
            var r = new DiagnosticReport(null, new List<AssumptionResult>
            {
                new AssumptionResult("a", true, ""),
                new AssumptionResult("b", false, "thing breaks"),
            }, null);

            string text = Joined(r);
            Assert.Contains("1", text);
            Assert.Contains("FAIL", text);
        }

        [Fact]
        public void PassedAssumptions_AreNotListedIndividually_ButFailuresAre()
        {
            // 通った前提を全部出すとオーバーレイが埋まる。FAIL だけ列挙する。
            var r = new DiagnosticReport(null, new List<AssumptionResult>
            {
                new AssumptionResult("QUIETPASS", true, ""),
                new AssumptionResult("LOUDFAIL", false, "thing breaks"),
            }, null);

            string text = Joined(r);
            Assert.DoesNotContain("QUIETPASS", text);
            Assert.Contains("LOUDFAIL", text);
            Assert.Contains("thing breaks", text);
        }

        [Fact]
        public void SectionHealth_IsVisible()
        {
            var r = new DiagnosticReport(null, null, new List<DiagnosticSection>
            {
                new DiagnosticSection("Volcano", FeatureHealth.Degraded, "3 errors", null),
            });

            string text = Joined(r);
            Assert.Contains("Volcano", text);
            Assert.Contains("Degraded", text);
            Assert.Contains("3 errors", text);
        }

        [Fact]
        public void DisabledSection_IsShownAsDisabled()
        {
            var r = new DiagnosticReport(null, null, new List<DiagnosticSection>
            {
                new DiagnosticSection("Typhoon", FeatureHealth.Disabled, null, null),
            });
            Assert.Contains("Disabled", Joined(r));
        }

        [Fact]
        public void Output_IsAsciiOnly()
        {
            // IMGUI の既定フォントは日本語グリフを持たない可能性が高く、豆腐になる。
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "state", "ready") },
                new List<AssumptionResult> { new AssumptionResult("x", false, "breaks") },
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("Fire whirl", FeatureHealth.Healthy, null,
                        new List<DiagnosticLine> { new DiagnosticLine(1, "active", "2") })
                });

            foreach (string line in DiagnosticFormatter.Format(r))
            {
                foreach (char c in line)
                {
                    Assert.True(c < 128, "non-ASCII character in output: " + c);
                }
            }
        }

        [Fact]
        public void IsDeterministic()
        {
            var r = new DiagnosticReport(
                new List<DiagnosticLine> { new DiagnosticLine(0, "a", "1") },
                new List<AssumptionResult> { new AssumptionResult("b", false, "c") },
                new List<DiagnosticSection>
                {
                    new DiagnosticSection("S", FeatureHealth.Healthy, null, null)
                });

            Assert.Equal(Joined(r), Joined(r));
        }
    }
}
