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
            // Game 層がうっかり null を渡してもオーバーレイが落ちないこと。
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
    }
}
