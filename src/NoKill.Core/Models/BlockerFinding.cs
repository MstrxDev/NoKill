namespace NoKill.Core.Models;

/// <summary>
/// One detected "window is blocked by a modal dialog" situation. The blocked
/// window is typically what the user perceives as frozen; the blocker is the
/// dialog actually waiting for input.
/// </summary>
public sealed record BlockerFinding
{
    public required nint BlockedWindowHandle { get; init; }

    public required string BlockedWindowTitle { get; init; }

    public required int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public required nint BlockerWindowHandle { get; init; }

    /// <summary>Modal dialogs frequently have empty titles; empty is preserved, not invented.</summary>
    public required string BlockerTitle { get; init; }

    /// <summary>True when the blocker sits BEHIND the window it blocks — the pathological case NoKill exists for.</summary>
    public required bool BlockerIsBehindBlockedWindow { get; init; }

    /// <summary>True when the blocker is invisible or on no monitor at all.</summary>
    public required bool BlockerIsNotOnScreen { get; init; }

    /// <summary>Dialog body text and button labels read via UI Automation, when accessible.</summary>
    public string? BlockerContent { get; init; }

    public required DateTimeOffset DetectedAt { get; init; }

    /// <summary>A finding is only actionable when the user likely cannot see the blocker.</summary>
    public bool IsHiddenBlocker => BlockerIsBehindBlockedWindow || BlockerIsNotOnScreen;
}
