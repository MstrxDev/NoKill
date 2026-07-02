using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NoKill.Core.Models;
using NoKill.Diagnostics;

namespace NoKill.App;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly WindowInventoryService _inventory = new();

    [ObservableProperty]
    private IReadOnlyList<AppWindowInfo> _windows = [];

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
            var snapshot = await Task.Run(() => _inventory.Snapshot());

            Windows = snapshot
                .OrderByDescending(w => w.Status)
                .ThenBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int notResponding = snapshot.Count(w => w.Status == HangStatus.NotResponding);
            int likelyHung = snapshot.Count(w => w.Status == HangStatus.LikelyHung);
            StatusText =
                $"Last scan {DateTime.Now:HH:mm:ss} — {snapshot.Count} windows, " +
                $"{notResponding} not responding, {likelyHung} likely hung";
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
