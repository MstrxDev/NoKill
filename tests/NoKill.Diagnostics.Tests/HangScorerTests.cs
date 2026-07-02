using NoKill.Core.Models;
using NoKill.Diagnostics;

namespace NoKill.Diagnostics.Tests;

public class HangScorerTests
{
    [Fact]
    public void BothSignalsAgree_ReportsNotResponding()
    {
        var signals = new HangSignals(IsHungAppWindow: true, PingTimedOut: true);
        Assert.Equal(HangStatus.NotResponding, HangScorer.Score(signals));
    }

    [Fact]
    public void OnlyWindowsHungFlag_ReportsLikelyHungNotNotResponding()
    {
        var signals = new HangSignals(IsHungAppWindow: true, PingTimedOut: false);
        Assert.Equal(HangStatus.LikelyHung, HangScorer.Score(signals));
    }

    [Fact]
    public void OnlyPingTimeout_StaysUnknown_BusyAppsAreNotHungApps()
    {
        // A renderer or compiler pegged at 100% CPU can miss a ping without
        // being hung. One weak signal must never produce an accusation.
        var signals = new HangSignals(IsHungAppWindow: false, PingTimedOut: true);
        Assert.Equal(HangStatus.Unknown, HangScorer.Score(signals));
    }

    [Fact]
    public void NoSignals_ReportsResponsive()
    {
        var signals = new HangSignals(IsHungAppWindow: false, PingTimedOut: false);
        Assert.Equal(HangStatus.Responsive, HangScorer.Score(signals));
    }

    [Fact]
    public void ProbeFailure_ReportsUnknown_RegardlessOfOtherSignals()
    {
        var signals = new HangSignals(IsHungAppWindow: true, PingTimedOut: true, ProbeFailed: true);
        Assert.Equal(HangStatus.Unknown, HangScorer.Score(signals));
    }

    [Fact]
    public void NotRespondingRequiresEverySignal_NoSinglePointOfAccusation()
    {
        // Safety property: NotResponding is only reachable when ALL signals
        // fire and the probe succeeded. Enumerate the full input space so a
        // future added signal that breaks this invariant fails the suite.
        foreach (bool hung in new[] { true, false })
        foreach (bool timeout in new[] { true, false })
        foreach (bool failed in new[] { true, false })
        {
            var status = HangScorer.Score(new HangSignals(hung, timeout, failed));
            if (status == HangStatus.NotResponding)
            {
                Assert.True(hung && timeout && !failed);
            }
        }
    }
}
