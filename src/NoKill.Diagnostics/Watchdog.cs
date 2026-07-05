using NoKill.Core.Models;

namespace NoKill.Diagnostics;

public sealed record WatchdogOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan ConfirmAfter { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>NoKill must never turn its diagnostics on itself in a feedback loop.</summary>
    public IReadOnlyList<string> IgnoreProcessNames { get; init; } = ["NoKill.App", "NoKill.Cli"];
}

/// <summary>A confirmed freeze incident, with the worst-off window for evidence targeting.</summary>
public sealed record FreezeIncident(
    int ProcessId, string ProcessName, TimeSpan HungFor, AppWindowInfo? TargetWindow);

/// <summary>
/// Background freeze monitor: scans the desktop on an interval, feeds the
/// incident tracker, and raises events when a process is confirmed frozen or
/// recovers. Detection only — what happens on an incident (typically an
/// automatic vault preserve) is the subscriber's decision, keeping the
/// watchdog itself strictly read-only.
/// </summary>
public sealed class Watchdog
{
    private readonly WatchdogOptions _options;
    private readonly WindowInventoryService _inventory;
    private readonly FreezeIncidentTracker _tracker;

    public event Action<FreezeIncident>? FreezeDetected;

    public event Action<FreezeIncident>? FreezeEnded;

    public Watchdog(WatchdogOptions? options = null, WindowInventoryService? inventory = null)
    {
        _options = options ?? new WatchdogOptions();
        _inventory = inventory ?? new WindowInventoryService();
        _tracker = new FreezeIncidentTracker(new FreezeTrackerOptions
        {
            ConfirmAfter = _options.ConfirmAfter,
            Cooldown = _options.Cooldown,
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch
            {
                // One failed scan (racing process exits, transient denials)
                // must not kill the watchdog; the next tick starts fresh.
            }

            try
            {
                await Task.Delay(_options.PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Tick()
    {
        int ownPid = Environment.ProcessId;
        var snapshot = _inventory.Snapshot()
            .Where(w => w.ProcessId != ownPid
                && !_options.IgnoreProcessNames.Contains(w.ProcessName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var observations = snapshot
            .GroupBy(w => w.ProcessId)
            .Select(g => new ProcessObservation(
                g.Key,
                g.First().ProcessName,
                // Only the strongest verdict triggers the watchdog: a process
                // is hung when any of its windows is fully NotResponding.
                g.Any(w => w.Status == HangStatus.NotResponding)))
            .ToList();

        foreach (var freezeEvent in _tracker.Observe(DateTimeOffset.Now, observations))
        {
            var target = snapshot
                .Where(w => w.ProcessId == freezeEvent.ProcessId)
                .OrderByDescending(w => w.Status)
                .FirstOrDefault();

            var incident = new FreezeIncident(
                freezeEvent.ProcessId, freezeEvent.ProcessName, freezeEvent.HungFor, target);

            (freezeEvent.Kind == FreezeEventKind.Started ? FreezeDetected : FreezeEnded)?.Invoke(incident);
        }
    }
}
