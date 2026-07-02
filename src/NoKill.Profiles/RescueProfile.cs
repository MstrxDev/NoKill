namespace NoKill.Profiles;

/// <summary>
/// One rule describing where recoverable artifacts for an app may live.
/// Paths support environment variables (%APPDATA%, %TEMP%…), the tokens
/// {ProcessName}, {ExeDir} and {Documents}, and wildcard directory segments
/// (e.g. "%APPDATA%\Microsoft\VisualStudio\*").
/// </summary>
public sealed record ArtifactRule
{
    public required string Path { get; init; }

    /// <summary>Vault bucket: autosave, backup, logs, crash-dumps, temp…</summary>
    public required string Category { get; init; }

    /// <summary>File glob within the path (tokens allowed), e.g. "*.blend". Null = all files.</summary>
    public string? FilePattern { get; init; }

    /// <summary>Only files modified within this many days. 0 = no age limit.</summary>
    public int MaxAgeDays { get; init; } = 30;

    /// <summary>Newest-first cap per rule, so a rule can never flood the vault.</summary>
    public int MaxFiles { get; init; } = 100;

    public bool Recursive { get; init; }
}

/// <summary>
/// A rescue playbook for an application — or for ALL applications when
/// <see cref="ProcessNames"/> is empty (the universal heuristic profile).
/// Profiles are pure data: built-ins ship in code, users add or override
/// them with JSON files, and both go through the same engine.
/// </summary>
public sealed record RescueProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>
    /// Process names this profile applies to (case-insensitive, ".exe"
    /// optional). Empty means the profile applies to every process.
    /// </summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = [];

    public IReadOnlyList<ArtifactRule> ArtifactRules { get; init; } = [];

    public bool AppliesTo(string processName)
    {
        if (ProcessNames.Count == 0)
        {
            return true;
        }

        string normalized = Normalize(processName);
        return ProcessNames.Any(name => Normalize(name) == normalized);
    }

    private static string Normalize(string name)
    {
        string trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4].ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }
}
