using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class DiagnosticReportTests
    {
        private static DiagnosticReport Build(params AssumptionResult[] assumptions)
        {
            return new DiagnosticReport(
                new List<DiagnosticLine>(),
                new List<AssumptionResult>(assumptions),
                new List<DiagnosticSection>());
        }

        [Fact]
        public void Counts_AreDerivedFromAssumptions()
        {
            var r = Build(
                new AssumptionResult("a", true, ""),
                new AssumptionResult("b", false, "x breaks"),
                new AssumptionResult("c", true, ""));

            Assert.Equal(2, r.PassedCount);
            Assert.Equal(1, r.FailedCount);
            Assert.True(r.HasFailures);
        }

        [Fact]
        public void NoFailures_HasFailuresIsFalse()
        {
            Assert.False(Build(new AssumptionResult("a", true, "")).HasFailures);
        }

        [Fact]
        public void EmptyReport_IsSafe()
        {
            var r = Build();
            Assert.Equal(0, r.PassedCount);
            Assert.Equal(0, r.FailedCount);
            Assert.False(r.HasFailures);
        }

        [Fact]
        public void NullCollections_AreTreatedAsEmpty()
        {
            // The overlay must not fall over even if the Game layer carelessly passes null.
            var r = new DiagnosticReport(null, null, null);
            Assert.NotNull(r.Header);
            Assert.NotNull(r.Assumptions);
            Assert.NotNull(r.Sections);
            Assert.False(r.HasFailures);
        }

        [Fact]
        public void SectionOrder_FollowsInputOrder()
        {
            var sections = new List<DiagnosticSection>
            {
                new DiagnosticSection("B", FeatureHealth.Healthy, null, null),
                new DiagnosticSection("A", FeatureHealth.Healthy, null, null),
            };
            var r = new DiagnosticReport(null, null, sections);
            Assert.Equal("B", r.Sections[0].Name);
            Assert.Equal("A", r.Sections[1].Name);
        }

        [Fact]
        public void Section_NullLines_AreTreatedAsEmpty()
        {
            var s = new DiagnosticSection("X", FeatureHealth.Disabled, null, null);
            Assert.NotNull(s.Lines);
            Assert.Empty(s.Lines);
        }

        [Fact]
        public void Report_DefensivesCopy_ProtectsAgainstMutation()
        {
            // Even if the caller modifies the list, the report's counts must stay unchanged.
            // This is because a report built on the sim thread has to keep returning the
            // correct values across a level unload that clears the original list.
            var assumptions = new List<AssumptionResult>
            {
                new AssumptionResult("a", true, ""),
                new AssumptionResult("b", false, "fail"),
            };

            var r = new DiagnosticReport(null, assumptions, null);
            Assert.Equal(1, r.PassedCount);
            Assert.Equal(1, r.FailedCount);
            Assert.Equal(2, r.Assumptions.Count);

            // Clear the original list.
            assumptions.Clear();

            // The report's counts are unchanged.
            Assert.Equal(1, r.PassedCount);
            Assert.Equal(1, r.FailedCount);
            // The report's copy does not reflect changes to the original list either.
            Assert.Equal(2, r.Assumptions.Count);
        }

        [Fact]
        public void DiagnosticLine_NegativeIndent_ClampsToZero()
        {
            var line1 = new DiagnosticLine(-5, "label", "value");
            Assert.Equal(0, line1.Indent);

            var line2 = new DiagnosticLine(0, "label", "value");
            Assert.Equal(0, line2.Indent);

            var line3 = new DiagnosticLine(3, "label", "value");
            Assert.Equal(3, line3.Indent);
        }

        [Fact]
        public void NullStrings_BecomeEmpty()
        {
            // DiagnosticLine
            var line = new DiagnosticLine(0, null, null);
            Assert.Equal("", line.Label);
            Assert.Equal("", line.Value);

            // AssumptionResult
            var result = new AssumptionResult(null, true, null);
            Assert.Equal("", result.Name);
            Assert.Equal("", result.Impact);

            // DiagnosticSection
            var section = new DiagnosticSection(null, FeatureHealth.Healthy, null, null);
            Assert.Equal("", section.Name);
            Assert.Equal("", section.Note);
        }
    }
}
