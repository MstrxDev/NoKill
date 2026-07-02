using NoKill.Core.Models;

namespace NoKill.Vault;

/// <summary>A file or directory worth preserving, and which bucket it lands in (autosave, logs, temp…).</summary>
public sealed record ArtifactSource(string SourcePath, string Category);

/// <summary>
/// Everything the vault should preserve about one rescue situation. All parts
/// except the target window are optional — the vault saves whatever evidence
/// exists rather than refusing to act on incomplete information.
/// </summary>
public sealed record VaultEntryRequest
{
    public required AppWindowInfo TargetWindow { get; init; }

    public ProcessInfoSnapshot? ProcessInfo { get; init; }

    public IReadOnlyList<AppWindowInfo> ProcessWindows { get; init; } = [];

    public IReadOnlyList<BlockerFinding> Blockers { get; init; } = [];

    public byte[]? ScreenshotPng { get; init; }

    /// <summary>Recovery artifacts to COPY into the vault. Sources are never modified, moved, or deleted.</summary>
    public IReadOnlyList<ArtifactSource> Artifacts { get; init; } = [];

    /// <summary>Why this entry was created, e.g. "manual preserve from dashboard".</summary>
    public string Reason { get; init; } = "manual";
}

/// <summary>What the vault actually managed to save.</summary>
public sealed record VaultEntryResult
{
    public required string EntryDirectory { get; init; }

    public required IReadOnlyList<string> SavedFiles { get; init; }

    /// <summary>Non-fatal problems (missing artifact, oversized file skipped). Empty means a clean preserve.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
