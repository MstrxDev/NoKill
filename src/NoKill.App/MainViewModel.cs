using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;

namespace NoKill.App;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly WindowInventoryService _inventory = new();
    private readonly HiddenDialogDetector _detector = new();

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
                $"{findings.Count} modal blocker(s) ({hiddenBlockers} hidden)";
        }
        finally
        {
            IsRefreshing = false;
        }
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
