using DisasterPlus.Core.Diagnostics;
using Xunit;

namespace DisasterPlus.Core.Tests.Diagnostics
{
    public class LogChannelTests
    {
        [Fact]
        public void IsEnabled_ChannelInMask_ReturnsTrue()
        {
            Assert.True(LogChannel.IsEnabled(LogChannel.FireWhirl,
                LogChannel.General | LogChannel.FireWhirl));
        }

        [Fact]
        public void IsEnabled_ChannelNotInMask_ReturnsFalse()
        {
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano,
                LogChannel.General | LogChannel.FireWhirl));
        }

        [Fact]
        public void IsEnabled_ZeroMask_EverythingOff()
        {
            Assert.False(LogChannel.IsEnabled(LogChannel.General, 0));
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano, 0));
        }

        [Fact]
        public void DefaultMask_EnablesGeneralOnly()
        {
            // Existing Log.Diag(key, msg) calls are treated as General.
            // Setting this to 0 would silently make the diagnostic logs that currently
            // appear vanish, and break the procedure in docs/playtest-checklist.md.
            Assert.True(LogChannel.IsEnabled(LogChannel.General, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.FireWhirl, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Forecast, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Earthquake, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Typhoon, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Volcano, LogChannel.DefaultMask));
            Assert.False(LogChannel.IsEnabled(LogChannel.Diagnostics, LogChannel.DefaultMask));
        }

        [Fact]
        public void BitPositions_AreFrozen()
        {
            // Saved values are a public contract. Changing a bit position makes the value
            // held in an existing player's .cgs mean something else. Do not close up the
            // gaps when retiring one either.
            Assert.Equal(1, LogChannel.General);
            Assert.Equal(2, LogChannel.FireWhirl);
            Assert.Equal(4, LogChannel.Forecast);
            Assert.Equal(8, LogChannel.Earthquake);
            Assert.Equal(16, LogChannel.Typhoon);
            Assert.Equal(32, LogChannel.Volcano);
            Assert.Equal(64, LogChannel.Diagnostics);
        }

        [Fact]
        public void IsEnabled_UndefinedBit_ReturnsFalseAgainstDefault()
        {
            Assert.False(LogChannel.IsEnabled(1 << 20, LogChannel.DefaultMask));
        }

        [Fact]
        public void IsEnabled_MultipleChannelsAtOnce_RequiresAll()
        {
            int mask = LogChannel.General | LogChannel.Volcano;
            Assert.True(LogChannel.IsEnabled(LogChannel.General | LogChannel.Volcano, mask));
            Assert.False(LogChannel.IsEnabled(LogChannel.General | LogChannel.Typhoon, mask));
        }

        [Fact]
        public void IsEnabled_ZeroChannel_ReturnsFalse()
        {
            Assert.False(LogChannel.IsEnabled(0, LogChannel.DefaultMask));
        }
    }
}
