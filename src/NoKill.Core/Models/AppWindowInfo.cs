namespace NoKill.Core.Models;

/// <summary>
/// A read-only snapshot of one top-level window and its owning process,
/// taken at a single point in time. NoKill never mutates the window or
/// process it describes.
/// </summary>
public sealed record AppWindowInfo
{
    /// <summary>Native window handle (HWND) at snapshot time.</summary>
    public required nint WindowHandle { get; init; }

    public required string Title { get; init; }

    public required int ProcessId { get; init; }

    /// <summary>Process image name without extension, e.g. "blender".</summary>
    public required string ProcessName { get; init; }

    /// <summary>Full executable path, or null when access is denied (elevated/protected process).</summary>
    public string? ExecutablePath { get; init; }

    public required HangStatus Status { get; init; }

    public required HangSignals Signals { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }
}
