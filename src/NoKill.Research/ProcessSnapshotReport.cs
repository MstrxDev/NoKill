namespace NoKill.Research;

/// <summary>
/// Result of capturing a diagnostic process snapshot. IMPORTANT: this is a
/// copy-on-write inspection snapshot, NOT a runnable clone — Windows cannot
/// resurrect a live process from one (kernel handles, GPU/driver state, window
/// handles and OS objects are not part of it). The value is a consistent
/// point-in-time view of the process for inspection while the original is
/// disturbed as little as possible.
/// </summary>
public sealed record ProcessSnapshotReport
{
    public required int ProcessId { get; init; }

    public required bool Captured { get; init; }

    public string? Error { get; init; }

    /// <summary>Threads captured in the snapshot.</summary>
    public int ThreadCount { get; init; }

    /// <summary>Handles captured in the snapshot (when handle capture was requested and permitted).</summary>
    public int HandleCount { get; init; }

    /// <summary>Whether a copy-on-write clone of the virtual address space was created.</summary>
    public bool VaCloneCreated { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public static ProcessSnapshotReport Failed(int pid, string error) => new()
    {
        ProcessId = pid,
        Captured = false,
        Error = error,
        CapturedAt = DateTimeOffset.Now,
    };
}
