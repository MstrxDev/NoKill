using NoKill.Win32;

namespace NoKill.Diagnostics;

/// <summary>
/// Pure logic that decides, from raw window facts alone, which windows are
/// modally blocked and which owned window is doing the blocking. No Win32
/// calls — fully unit-testable with hand-built window lists.
///
/// The mechanics being exploited: a modal dialog disables its owner window
/// (IsEnabled = false) and stays enabled itself. So a visible, titled,
/// DISABLED window is "blocked", and the enabled window(s) in its owned
/// subtree are the active blocker(s).
/// </summary>
public static class BlockerClassifier
{
    public readonly record struct Candidate(
        TopLevelWindow Blocked,
        TopLevelWindow Blocker,
        bool BlockerIsBehindBlockedWindow);

    private const int MaxOwnerChainDepth = 16;

    public static IReadOnlyList<Candidate> FindBlockers(IReadOnlyList<TopLevelWindow> windows)
    {
        var ownerOf = windows.ToDictionary(w => w.Handle, w => w.OwnerHandle);

        var blocked = windows
            .Where(w => w.IsVisible && !w.IsCloaked && !w.IsEnabled && w.Title.Length > 0)
            .ToList();

        if (blocked.Count == 0)
        {
            return [];
        }

        var results = new List<Candidate>();

        foreach (var blockedWindow in blocked)
        {
            // The active blocker: an ENABLED window whose owner chain reaches
            // the blocked window. In a nested modal stack only the topmost
            // dialog is enabled, so this naturally finds the one that matters.
            foreach (var window in windows)
            {
                if (!window.IsEnabled || !window.IsVisible || window.Handle == blockedWindow.Handle)
                {
                    continue;
                }

                if (OwnerChainContains(window, blockedWindow.Handle, ownerOf))
                {
                    results.Add(new Candidate(
                        blockedWindow,
                        window,
                        // Higher ZOrder = further back in the enumeration pass.
                        BlockerIsBehindBlockedWindow: window.ZOrder > blockedWindow.ZOrder));
                }
            }
        }

        return results;
    }

    private static bool OwnerChainContains(
        TopLevelWindow window, nint target, IReadOnlyDictionary<nint, nint> ownerOf)
    {
        nint current = window.OwnerHandle;
        for (int depth = 0; depth < MaxOwnerChainDepth && current != 0; depth++)
        {
            if (current == target)
            {
                return true;
            }

            if (!ownerOf.TryGetValue(current, out current))
            {
                return false;
            }
        }

        return false;
    }
}
