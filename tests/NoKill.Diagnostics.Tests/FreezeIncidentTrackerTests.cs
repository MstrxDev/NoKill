namespace NoKill.Diagnostics.Tests;

public class FreezeIncidentTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static FreezeIncidentTracker Tracker(int confirmSeconds = 10, int cooldownMinutes = 2) =>
        new(new FreezeTrackerOptions
        {
            ConfirmAfter = TimeSpan.FromSeconds(confirmSeconds),
            Cooldown = TimeSpan.FromMinutes(cooldownMinutes),
        });

    private static ProcessObservation Hung(int pid = 100, string name = "app") => new(pid, name, IsHung: true);

    private static ProcessObservation Fine(int pid = 100, string name = "app") => new(pid, name, IsHung: false);

    [Fact]
    public void TransientHang_ShorterThanConfirmWindow_NeverBecomesIncident()
    {
        var tracker = Tracker(confirmSeconds: 10);

        Assert.Empty(tracker.Observe(T0, [Hung()]));
        Assert.Empty(tracker.Observe(T0.AddSeconds(5), [Hung()]));
        Assert.Empty(tracker.Observe(T0.AddSeconds(8), [Fine()])); // recovered before confirm
        Assert.Empty(tracker.Observe(T0.AddSeconds(11), [Fine()]));
    }

    [Fact]
    public void PersistentHang_BecomesIncident_ExactlyOnce()
    {
        var tracker = Tracker(confirmSeconds: 10);

        Assert.Empty(tracker.Observe(T0, [Hung()]));
        Assert.Empty(tracker.Observe(T0.AddSeconds(5), [Hung()]));

        var confirmed = tracker.Observe(T0.AddSeconds(12), [Hung()]);
        var started = Assert.Single(confirmed);
        Assert.Equal(FreezeEventKind.Started, started.Kind);
        Assert.Equal(100, started.ProcessId);
        Assert.True(started.HungFor >= TimeSpan.FromSeconds(10));

        // still hung: no re-fire, ever
        Assert.Empty(tracker.Observe(T0.AddSeconds(15), [Hung()]));
        Assert.Empty(tracker.Observe(T0.AddSeconds(60), [Hung()]));
    }

    [Fact]
    public void Recovery_EmitsEnded_AndDisappearanceCountsAsRecovery()
    {
        var tracker = Tracker(confirmSeconds: 5);

        tracker.Observe(T0, [Hung()]);
        tracker.Observe(T0.AddSeconds(6), [Hung()]); // incident starts

        var recovered = tracker.Observe(T0.AddSeconds(12), [Fine()]);
        Assert.Equal(FreezeEventKind.Ended, Assert.Single(recovered).Kind);

        // process gone entirely (killed/exited) also ends an incident
        tracker.Observe(T0.AddSeconds(20), [Hung(pid: 200, name: "other")]);
        tracker.Observe(T0.AddSeconds(26), [Hung(pid: 200, name: "other")]);
        var gone = tracker.Observe(T0.AddSeconds(30), []);
        var ended = Assert.Single(gone);
        Assert.Equal(FreezeEventKind.Ended, ended.Kind);
        Assert.Equal(200, ended.ProcessId);
    }

    [Fact]
    public void RefreezeAfterRecovery_WithinCooldown_IsSuppressed()
    {
        var tracker = Tracker(confirmSeconds: 5, cooldownMinutes: 2);

        tracker.Observe(T0, [Hung()]);
        Assert.Single(tracker.Observe(T0.AddSeconds(6), [Hung()])); // first incident
        tracker.Observe(T0.AddSeconds(10), [Fine()]);               // recovers

        // freezes again 20 s later: inside the 2 min cooldown → silent
        tracker.Observe(T0.AddSeconds(30), [Hung()]);
        Assert.Empty(tracker.Observe(T0.AddSeconds(36), [Hung()]));
    }

    [Fact]
    public void RefreezeAfterCooldown_FiresAgain()
    {
        var tracker = Tracker(confirmSeconds: 5, cooldownMinutes: 2);

        tracker.Observe(T0, [Hung()]);
        Assert.Single(tracker.Observe(T0.AddSeconds(6), [Hung()]));
        tracker.Observe(T0.AddSeconds(10), [Fine()]);

        var later = T0.AddMinutes(5);
        tracker.Observe(later, [Hung()]);
        var again = tracker.Observe(later.AddSeconds(6), [Hung()]);
        Assert.Equal(FreezeEventKind.Started, Assert.Single(again).Kind);
    }

    [Fact]
    public void MultipleProcesses_AreTrackedIndependently()
    {
        var tracker = Tracker(confirmSeconds: 5);

        tracker.Observe(T0, [Hung(1, "a"), Hung(2, "b"), Fine(3, "c")]);
        var events = tracker.Observe(T0.AddSeconds(6), [Hung(1, "a"), Hung(2, "b"), Fine(3, "c")]);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.ProcessId == 1 && e.Kind == FreezeEventKind.Started);
        Assert.Contains(events, e => e.ProcessId == 2 && e.Kind == FreezeEventKind.Started);
    }

    [Fact]
    public void HealthyProcesses_NeverProduceEvents()
    {
        var tracker = Tracker();

        for (int i = 0; i < 20; i++)
        {
            Assert.Empty(tracker.Observe(T0.AddSeconds(i * 3), [Fine(1), Fine(2), Fine(3)]));
        }
    }

    [Fact]
    public void Reset_ForgetsInFlightState_ButCooldownSurvives()
    {
        var tracker = Tracker(confirmSeconds: 5, cooldownMinutes: 10);

        tracker.Observe(T0, [Hung()]);
        Assert.Single(tracker.Observe(T0.AddSeconds(6), [Hung()]));

        tracker.Reset();

        // fresh hang after reset: confirm window restarts...
        Assert.Empty(tracker.Observe(T0.AddSeconds(20), [Hung()]));
        // ...and the cooldown from the pre-reset incident still suppresses
        Assert.Empty(tracker.Observe(T0.AddSeconds(26), [Hung()]));
    }
}
