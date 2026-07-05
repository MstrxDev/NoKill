using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;
using NoKill.Profiles;
using NoKill.Vault;
using NoKill.Win32;

namespace NoKill.App;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly WindowInventoryService _inventory = new();
    private readonly HiddenDialogDetector _detector = new();
    private readonly RecoveryVault _vault = new();
    private readonly ArtifactPlanner _planner = new();
    private readonly FreezeHistory _history = new();
    private readonly Dictionary<int, long> _activeIncidents = [];

    // Watchdog rides the dashboard's own 3 s scan: 9 s confirm = 3 consecutive
    // NotResponding scans before an incident is declared.
    private readonly FreezeIncidentTracker _freezeTracker = new(new FreezeTrackerOptions
    {
        ConfirmAfter = TimeSpan.FromSeconds(9),
        Cooldown = TimeSpan.FromMinutes(2),
    });

    [ObservableProperty]
    private bool _watchdogEnabled;

    partial void OnWatchdogEnabledChanged(bool value)
    {
        if (!value)
        {
            _freezeTracker.Reset(); // stale hung-since timestamps must not survive a toggle
        }

        StatusText = value
            ? "Watchdog armed: evidence will be preserved automatically when an app freezes."
            : "Watchdog disarmed.";
    }

    [ObservableProperty]
    private IReadOnlyList<AppWindowInfo> _windows = [];

    [ObservableProperty]
    private IReadOnlyList<BlockerFinding> _blockers = [];

    [ObservableProperty]
    private string _statusText = "Scanning…";

    [ObservableProperty]
    private bool _isRefreshing;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return; // a scan is already in flight; the timer can be faster than a slow probe pass
        }

        IsRefreshing = true;
        try
        {
            // Probing must not run on our own UI thread: a slow probe pass
            // would make NoKill itself look hung, which would be embarrassing.
            var (snapshot, findings) = await Task.Run(() =>
                (_inventory.Snapshot(), _detector.Detect()));

            Windows = snapshot
                .OrderByDescending(w => w.Status)
                .ThenBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Blockers = findings;

            int notResponding = snapshot.Count(w => w.Status == HangStatus.NotResponding);
            int likelyHung = snapshot.Count(w => w.Status == HangStatus.LikelyHung);
            int hiddenBlockers = findings.Count(f => f.IsHiddenBlocker);
            StatusText =
                $"Last scan {DateTime.Now:HH:mm:ss} — {snapshot.Count} windows, " +
                $"{notResponding} not responding, {likelyHung} likely hung, " +
                $"{findings.Count} modal blocker(s) ({hiddenBlockers} hidden)" +
                (WatchdogEnabled ? " — watchdog armed" : string.Empty);

            if (WatchdogEnabled)
            {
                await RunWatchdogTickAsync(snapshot);
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task RunWatchdogTickAsync(IReadOnlyList<AppWindowInfo> snapshot)
    {
        int ownPid = Environment.ProcessId;
        var observations = snapshot
            .Where(w => w.ProcessId != ownPid)
            .GroupBy(w => w.ProcessId)
            .Select(g => new ProcessObservation(
                g.Key, g.First().ProcessName, g.Any(w => w.Status == HangStatus.NotResponding)))
            .ToList();

        foreach (var freezeEvent in _freezeTracker.Observe(DateTimeOffset.Now, observations))
        {
            if (freezeEvent.Kind == FreezeEventKind.Started)
            {
                var target = snapshot
                    .Where(w => w.ProcessId == freezeEvent.ProcessId)
                    .OrderByDescending(w => w.Status)
                    .FirstOrDefault();

                if (target is not null)
                {
                    StatusText = $"Watchdog: {freezeEvent.ProcessName} froze — preserving evidence…";
                    long incidentId = await PreserveCoreAsync(target, "watchdog");
                    if (incidentId > 0)
                    {
                        _activeIncidents[freezeEvent.ProcessId] = incidentId;
                    }
                }
            }
            else
            {
                StatusText = $"Watchdog: {freezeEvent.ProcessName} recovered or exited.";
                if (_activeIncidents.Remove(freezeEvent.ProcessId, out long incidentId))
                {
                    try
                    {
                        _history.MarkEnded(incidentId);
                    }
                    catch
                    {
                        // bookkeeping only
                    }
                }
            }
        }
    }

    [RelayCommand]
    private Task PreserveAsync(AppWindowInfo target) => PreserveCoreAsync(target, "manual");

    private async Task<long> PreserveCoreAsync(AppWindowInfo target, string trigger)
    {
        StatusText = $"Preserving evidence for {target.ProcessName}…";

        var (result, incidentId) = await Task.Run(() =>
        {
            var processWindows = Windows.Where(w => w.ProcessId == target.ProcessId).ToList();
            var blockers = Blockers.Where(f => f.ProcessId == target.ProcessId).ToList();
            var plan = _planner.PlanFor(target.ProcessName, target.ExecutablePath);
            var waitChains = new WaitChainAnalyzer().Analyze(target.ProcessId);
            IReadOnlyList<string> insights =
                waitChains is not null ? WaitChainInterpreter.Interpret(waitChains) : [];

            string dumpStage = _vault.CreateTempFilePath(".dmp");
            var (dumpOk, _) = MiniDumpWriter.TryWrite(target.ProcessId, dumpStage, DumpDetail.Triage);

            var preserveResult = _vault.Preserve(new VaultEntryRequest
            {
                TargetWindow = target,
                ProcessInfo = ProcessInspector.TryInspect(target.ProcessId),
                ProcessWindows = processWindows,
                Blockers = blockers,
                ScreenshotPng = WindowCapture.TryCapturePng(target.WindowHandle),
                Artifacts = plan.Artifacts,
                AppliedProfiles = plan.AppliedProfiles,
                WaitChains = waitChains,
                WaitChainInsights = insights,
                MinidumpTempPath = dumpOk ? dumpStage : null,
                MinidumpDetail = dumpOk ? "triage" : null,
                Reason = $"{trigger} preserve from dashboard",
            });

            long id = 0;
            try
            {
                id = _history.RecordIncident(
                    target.ProcessName, target.ProcessId, target.ExecutablePath,
                    trigger, preserveResult.EntryDirectory, insights.FirstOrDefault());
            }
            catch
            {
                // history is bookkeeping; a failed insert must not fail a preserve
            }

            return (preserveResult, id);
        });

        StatusText = result.Warnings.Count == 0
            ? $"Preserved {result.SavedFiles.Count} file(s) → {result.EntryDirectory}"
            : $"Preserved with {result.Warnings.Count} warning(s) → {result.EntryDirectory}";

        return incidentId;
    }

    [RelayCommand]
    private async Task DiagnoseAsync(AppWindowInfo target)
    {
        StatusText = $"Analyzing wait chains of {target.ProcessName}…";

        var report = await Task.Run(() => new WaitChainAnalyzer().Analyze(target.ProcessId));

        StatusText = report is null
            ? "Wait Chain Traversal is unavailable on this system."
            : $"{target.ProcessName}: {WaitChainInterpreter.Interpret(report)[0]}";
    }

    [RelayCommand]
    private async Task RevealBlockerAsync(BlockerFinding finding)
    {
        bool ok = HiddenDialogDetector.Reveal(finding);
        StatusText = ok
            ? $"Revealed blocker of \"{finding.BlockedWindowTitle}\" — dialog raised to front"
            : $"Could not raise blocker of \"{finding.BlockedWindowTitle}\"";

        await RefreshAsync();
    }
}
