namespace NoKill.Core.Models;

/// <summary>Point-in-time facts about a process, gathered read-only for the rescue report.</summary>
public sealed record ProcessInfoSnapshot
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public string? ExecutablePath { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public long WorkingSetBytes { get; init; }

    public long PrivateMemoryBytes { get; init; }

    public int ThreadCount { get; init; }

    /// <summary>Process.Responding — false when the main window fails a quick message check.</summary>
    public bool? Responding { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }
}
