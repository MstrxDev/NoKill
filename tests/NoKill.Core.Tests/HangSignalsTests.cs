using NoKill.Core.Models;

namespace NoKill.Core.Tests;

public class HangSignalsTests
{
    [Fact]
    public void FailedProbe_CarriesNoOtherClaims()
    {
        var failed = HangSignals.Failed;

        Assert.True(failed.ProbeFailed);
        Assert.False(failed.IsHungAppWindow);
        Assert.False(failed.PingTimedOut);
    }

    [Fact]
    public void AppWindowInfo_IsImmutableSnapshot()
    {
        var info = new AppWindowInfo
        {
            WindowHandle = 0x1234,
            Title = "Test",
            ProcessId = 42,
            ProcessName = "test",
            Status = HangStatus.Responsive,
            Signals = new HangSignals(false, false),
            CapturedAt = DateTimeOffset.UnixEpoch,
        };

        // record equality: two snapshots with identical data are the same snapshot
        var copy = info with { };
        Assert.Equal(info, copy);
        Assert.Null(info.ExecutablePath);
    }
}
