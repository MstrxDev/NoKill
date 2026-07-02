using System.Diagnostics;
using NoKill.Core.Models;
using NoKill.Win32;

namespace NoKill.Diagnostics;

/// <summary>
/// Produces read-only snapshots of the desktop's application windows with a
/// conservative hang classification per window. Performs no action against
/// any window or process beyond a WM_NULL responsiveness ping.
/// </summary>
public sealed class WindowInventoryService
{
    private readonly uint _pingTimeoutMs;

    public WindowInventoryService(uint pingTimeoutMs = 1000)
    {
        _pingTimeoutMs = pingTimeoutMs;
    }

    /// <summary>
    /// Snapshots all visible, titled, non-cloaked top-level windows.
    /// Hidden windows are excluded here for the dashboard view; the future
    /// blocker investigator will query them separately on purpose.
    /// </summary>
    public IReadOnlyList<AppWindowInfo> Snapshot()
    {
        var now = DateTimeOffset.Now;
        var windows = WindowEnumerator.Snapshot()
            .Where(w => w.IsVisible && !w.IsCloaked && w.Title.Length > 0)
            .ToList();

        var results = new List<AppWindowInfo>(windows.Count);
        var processCache = new Dictionary<uint, (string Name, string? Path)>();

        foreach (var window in windows)
        {
            var signals = Probe(window.Handle);
            var (processName, executablePath) = ResolveProcess(window.ProcessId, processCache);

            results.Add(new AppWindowInfo
            {
                WindowHandle = window.Handle,
                Title = window.Title,
                ProcessId = (int)window.ProcessId,
                ProcessName = processName,
                ExecutablePath = executablePath,
                Status = HangScorer.Score(signals),
                Signals = signals,
                CapturedAt = now,
            });
        }

        return results;
    }

    private HangSignals Probe(nint hwnd)
    {
        try
        {
            return new HangSignals(
                IsHungAppWindow: HangProbe.IsHungAppWindow(hwnd),
                PingTimedOut: HangProbe.PingTimedOut(hwnd, _pingTimeoutMs));
        }
        catch
        {
            // Window destroyed mid-probe or access denied; report honestly.
            return HangSignals.Failed;
        }
    }

    private static (string Name, string? Path) ResolveProcess(
        uint pid, Dictionary<uint, (string, string?)> cache)
    {
        if (cache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        (string, string?) resolved;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                // Access denied for elevated/protected processes; name still useful.
            }

            resolved = (process.ProcessName, path);
        }
        catch
        {
            resolved = ($"pid {pid}", null);
        }

        cache[pid] = resolved;
        return resolved;
    }
}
