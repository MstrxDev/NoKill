using System.Diagnostics;
using NoKill.Core.Models;
using NoKill.Diagnostics;
using NoKill.Win32;

namespace NoKill.Automation;

/// <summary>
/// Finds windows that look frozen to the user but are actually waiting on a
/// modal dialog — especially dialogs hiding behind their owner or stranded
/// off-screen. Detection is read-only; the single offered remedy is
/// <see cref="Reveal"/>, which raises the dialog so the user can answer it.
/// </summary>
public sealed class HiddenDialogDetector
{
    public IReadOnlyList<BlockerFinding> Detect() => Detect(WindowEnumerator.Snapshot());

    public IReadOnlyList<BlockerFinding> Detect(IReadOnlyList<TopLevelWindow> windows)
    {
        var now = DateTimeOffset.Now;

        return BlockerClassifier.FindBlockers(windows)
            .Select(candidate => new BlockerFinding
            {
                BlockedWindowHandle = candidate.Blocked.Handle,
                BlockedWindowTitle = candidate.Blocked.Title,
                ProcessId = (int)candidate.Blocked.ProcessId,
                ProcessName = ResolveProcessName(candidate.Blocked.ProcessId),
                BlockerWindowHandle = candidate.Blocker.Handle,
                BlockerTitle = candidate.Blocker.Title,
                BlockerIsBehindBlockedWindow = candidate.BlockerIsBehindBlockedWindow,
                BlockerIsNotOnScreen = WindowProbes.IsOffScreen(candidate.Blocker.Handle),
                BlockerContent = DialogContentReader.TryRead(candidate.Blocker.Handle),
                DetectedAt = now,
            })
            .ToList();
    }

    /// <summary>
    /// The first rung of the rescue ladder that acts at all: raise the hidden
    /// blocker so the user can see and answer it. Never clicks, never types,
    /// never dismisses — the decision stays with the user.
    /// </summary>
    public static bool Reveal(BlockerFinding finding) =>
        WindowActions.BringToTop(finding.BlockerWindowHandle);

    private static string ResolveProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return $"pid {pid}";
        }
    }
}
