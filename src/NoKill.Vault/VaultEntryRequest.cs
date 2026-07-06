using NoKill.Core.Models;

namespace NoKill.Vault;

/// <summary>A file or directory worth preserving, and which bucket it lands in (autosave, logs, temp…).</summary>
public sealed record ArtifactSource(string SourcePath, string Category);

/// <summary>
/// Everything the vault should preserve about one rescue situation. Every
/// part is optional — windowless services and background processes have no
/// target window, and the vault saves whatever evidence exists rather than
/// refusing to act on incomplete information. At least one of
/// <see cref="TargetWindow"/> or <see cref="ProcessInfo"/> should identify
/// the process.
/// </summary>
public sealed record VaultEntryRequest
{
    public AppWindowInfo? TargetWindow { get; init; }

    public ProcessInfoSnapshot? ProcessInfo { get; init; }

    /// <summary>Display names of the rescue profiles that contributed to this preserve.</summary>
    public IReadOnlyList<string> AppliedProfiles { get; init; } = [];

    /// <summary>Wait-chain analysis of the target process, when available.</summary>
    public WaitChainReport? WaitChains { get; init; }

    /// <summary>Plain-English interpretation of the wait chains (computed by the caller).</summary>
    public IReadOnlyList<string> WaitChainInsights { get; init; } = [];

    public IReadOnlyList<AppWindowInfo> ProcessWindows { get; init; } = [];

    public IReadOnlyList<BlockerFinding> Blockers { get; init; } = [];

    public byte[]? ScreenshotPng { get; init; }

    /// <summary>Recovery artifacts to COPY into the vault. Sources are never modified, moved, or deleted.</summary>
    public IReadOnlyList<ArtifactSource> Artifacts { get; init; } = [];

    /// <summary>
    /// Path to a minidump NoKill itself just captured into vault temp space
    /// (see <see cref="RecoveryVault.CreateTempFilePath"/>). Unlike artifacts
    /// — which are user data and only ever copied — this file is NoKill's own
    /// and is MOVED into the entry, so a large dump is never written twice.
    /// </summary>
    public string? MinidumpTempPath { get; init; }

    /// <summary>Dump detail label for the report ("triage", "full").</summary>
    public string? MinidumpDetail { get; init; }

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

    /// <summary>Old entries removed by the retention policy to stay within the vault's caps.</summary>
    public IReadOnlyList<string> PrunedEntries { get; init; } = [];
}
