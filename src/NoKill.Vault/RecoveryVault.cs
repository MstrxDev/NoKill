using System.Text;
using System.Text.Json;
using NoKill.Core.Models;

namespace NoKill.Vault;

/// <summary>
/// Caps that keep the vault from growing unbounded (the watchdog auto-preserves
/// evidence including minidumps). Each cap can be disabled with 0. Pruning is
/// oldest-first and only ever touches the vault's own entry folders — never
/// user files, and never the entry currently being written.
/// </summary>
public sealed record VaultRetentionOptions
{
    /// <summary>Total vault size cap. Default 2 GB; 0 disables.</summary>
    public long MaxTotalBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Maximum number of entries. Default 200; 0 disables.</summary>
    public int MaxEntries { get; init; } = 200;

    /// <summary>Entries older than this are pruned. Default 0 (disabled) — age alone never deletes evidence.</summary>
    public int MaxAgeDays { get; init; }
}

/// <summary>
/// Preserves rescue evidence into a per-incident folder BEFORE any
/// intervention is attempted. Vault rules, enforced here and not by caller
/// discipline: sources are only ever read, entries are never overwritten,
/// and a partial preserve with warnings always beats refusing to preserve.
/// </summary>
public sealed class RecoveryVault
{
    /// <summary>Individual artifact files larger than this are skipped (with a warning), not copied.</summary>
    public const long MaxArtifactFileBytes = 256 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new NintJsonConverter() },
    };

    private readonly string _rootDirectory;
    private readonly VaultRetentionOptions _retention;

    public RecoveryVault(string? rootDirectory = null, VaultRetentionOptions? retention = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NoKill", "Vault");
        _retention = retention ?? new VaultRetentionOptions();
    }

    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// A temp path on the SAME volume as the vault, so files staged here
    /// (e.g. a minidump being captured) move into an entry instantly.
    /// </summary>
    public string CreateTempFilePath(string extension)
    {
        string tempDir = Path.Combine(_rootDirectory, ".tmp");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, $"{Guid.NewGuid():N}{extension}");
    }

    public VaultEntryResult Preserve(VaultEntryRequest request)
    {
        string entryDir = CreateEntryDirectory(request);
        var saved = new List<string>();
        var warnings = new List<string>();

        WriteJson(entryDir, "rescue-report.json", request, saved, warnings);
        WriteText(entryDir, "rescue-report.txt", BuildTextReport(request), saved, warnings);

        if (request.ProcessInfo is not null)
        {
            WriteJson(entryDir, "process-info.json", request.ProcessInfo, saved, warnings);
        }

        if (request.ProcessWindows.Count > 0)
        {
            WriteJson(entryDir, "windows.json", request.ProcessWindows, saved, warnings);
        }

        if (request.WaitChains is not null)
        {
            WriteJson(entryDir, "wait-chains.json", request.WaitChains, saved, warnings);
        }

        if (request.ScreenshotPng is { Length: > 0 })
        {
            string path = Path.Combine(entryDir, "screenshot.png");
            File.WriteAllBytes(path, request.ScreenshotPng);
            saved.Add(path);
        }

        if (request.MinidumpTempPath is { } dumpTemp)
        {
            MoveMinidumpIn(entryDir, dumpTemp, saved, warnings);
        }

        foreach (var artifact in request.Artifacts)
        {
            CopyArtifact(entryDir, artifact, saved, warnings);
        }

        var pruned = EnforceRetention(entryDir, warnings);

        return new VaultEntryResult
        {
            EntryDirectory = entryDir,
            SavedFiles = saved,
            Warnings = warnings,
            PrunedEntries = pruned,
        };
    }

    /// <summary>Current vault footprint: entry count and total bytes (excluding staging).</summary>
    public (int EntryCount, long TotalBytes) GetStats()
    {
        try
        {
            var entries = EnumerateEntryDirectories().ToList();
            return (entries.Count, entries.Sum(DirectorySize));
        }
        catch
        {
            return (0, 0);
        }
    }

    private IEnumerable<string> EnumerateEntryDirectories()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        // dot-dirs (.tmp staging) are not entries
        return Directory.EnumerateDirectories(_rootDirectory)
            .Where(d => !Path.GetFileName(d).StartsWith('.'));
    }

    private IReadOnlyList<string> EnforceRetention(string currentEntryDir, List<string> warnings)
    {
        var pruned = new List<string>();
        try
        {
            string currentFull = Path.GetFullPath(currentEntryDir);

            // Oldest first; the entry just written is never a pruning candidate.
            var candidates = EnumerateEntryDirectories()
                .Where(d => !string.Equals(Path.GetFullPath(d), currentFull, StringComparison.OrdinalIgnoreCase))
                .Select(d => (Dir: d, Created: Directory.GetCreationTimeUtc(d), Size: DirectorySize(d)))
                .OrderBy(e => e.Created)
                .ToList();

            long totalBytes = candidates.Sum(e => e.Size) + DirectorySize(currentEntryDir);
            int entryCount = candidates.Count + 1;

            foreach (var (dir, created, size) in candidates)
            {
                bool tooOld = _retention.MaxAgeDays > 0
                    && DateTime.UtcNow - created > TimeSpan.FromDays(_retention.MaxAgeDays);
                bool overCount = _retention.MaxEntries > 0 && entryCount > _retention.MaxEntries;
                bool overSize = _retention.MaxTotalBytes > 0 && totalBytes > _retention.MaxTotalBytes;

                if (!tooOld && !overCount && !overSize)
                {
                    break; // sorted oldest-first: nothing newer can violate either
                }

                try
                {
                    Directory.Delete(dir, recursive: true);
                    pruned.Add(dir);
                    totalBytes -= size;
                    entryCount--;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Retention: could not prune {Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Retention is housekeeping; it must never fail a preserve.
            warnings.Add($"Retention enforcement failed: {ex.Message}");
        }

        return pruned;
    }

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try
                    {
                        return new FileInfo(f).Length;
                    }
                    catch
                    {
                        return 0L;
                    }
                });
        }
        catch
        {
            return 0L;
        }
    }

    private string CreateEntryDirectory(VaultEntryRequest request)
    {
        string processName =
            request.TargetWindow?.ProcessName ?? request.ProcessInfo?.ProcessName ?? "process";
        int pid = request.TargetWindow?.ProcessId ?? request.ProcessInfo?.ProcessId ?? 0;

        string baseName = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{Sanitize(processName)}_{pid}";

        // Never reuse an entry folder: a second preserve in the same second
        // gets a numeric suffix instead of writing into existing evidence.
        string candidate = Path.Combine(_rootDirectory, baseName);
        for (int suffix = 2; Directory.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(_rootDirectory, $"{baseName}_{suffix}");
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    private static void MoveMinidumpIn(
        string entryDir, string dumpTemp, List<string> saved, List<string> warnings)
    {
        try
        {
            if (!File.Exists(dumpTemp))
            {
                warnings.Add($"Minidump temp file not found: {dumpTemp}");
                return;
            }

            string target = Path.Combine(entryDir, "minidump.dmp");
            File.Move(dumpTemp, target); // same volume when staged via CreateTempFilePath
            saved.Add(target);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to move minidump into vault: {ex.Message}");
        }
    }

    private static void CopyArtifact(
        string entryDir, ArtifactSource artifact, List<string> saved, List<string> warnings)
    {
        string targetRoot = Path.Combine(entryDir, "recovered-files", Sanitize(artifact.Category));

        try
        {
            if (File.Exists(artifact.SourcePath))
            {
                CopyFile(artifact.SourcePath, targetRoot, saved, warnings);
            }
            else if (Directory.Exists(artifact.SourcePath))
            {
                foreach (string file in Directory.EnumerateFiles(
                    artifact.SourcePath, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(artifact.SourcePath, file);
                    CopyFile(file, Path.Combine(targetRoot, Path.GetDirectoryName(relative) ?? ""),
                        saved, warnings);
                }
            }
            else
            {
                warnings.Add($"Artifact not found: {artifact.SourcePath}");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to copy {artifact.SourcePath}: {ex.Message}");
        }
    }

    private static void CopyFile(string source, string targetDir, List<string> saved, List<string> warnings)
    {
        try
        {
            var info = new FileInfo(source);
            if (info.Length > MaxArtifactFileBytes)
            {
                warnings.Add($"Skipped oversized file ({info.Length / (1024 * 1024)} MB): {source}");
                return;
            }

            Directory.CreateDirectory(targetDir);
            string target = Path.Combine(targetDir, info.Name);
            File.Copy(source, target, overwrite: false);
            saved.Add(target);
        }
        catch (Exception ex)
        {
            // A locked autosave file (the frozen app may hold it open) must
            // not abort the rest of the preserve.
            warnings.Add($"Failed to copy {source}: {ex.Message}");
        }
    }

    private static void WriteJson<T>(
        string entryDir, string fileName, T payload, List<string> saved, List<string> warnings)
    {
        try
        {
            string path = Path.Combine(entryDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
            saved.Add(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to write {fileName}: {ex.Message}");
        }
    }

    // BOM so legacy Windows tools (older PowerShell, some editors) detect UTF-8
    // instead of mangling non-ASCII characters in window titles.
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static void WriteText(
        string entryDir, string fileName, string content, List<string> saved, List<string> warnings)
    {
        try
        {
            string path = Path.Combine(entryDir, fileName);
            File.WriteAllText(path, content, Utf8WithBom);
            saved.Add(path);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to write {fileName}: {ex.Message}");
        }
    }

    private static string BuildTextReport(VaultEntryRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("NoKill Rescue Report");
        sb.AppendLine("====================");
        sb.AppendLine($"Created:   {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Reason:    {request.Reason}");
        sb.AppendLine();

        if (request.TargetWindow is { } target)
        {
            sb.AppendLine($"Target:    \"{target.Title}\"");
            sb.AppendLine($"Process:   {target.ProcessName} (pid {target.ProcessId})");
            sb.AppendLine($"Path:      {target.ExecutablePath ?? "(inaccessible)"}");
            sb.AppendLine($"Status:    {target.Status}");
            sb.AppendLine(
                $"Signals:   IsHungAppWindow={target.Signals.IsHungAppWindow}, " +
                $"PingTimedOut={target.Signals.PingTimedOut}, ProbeFailed={target.Signals.ProbeFailed}");
        }
        else if (request.ProcessInfo is { } proc)
        {
            sb.AppendLine($"Target:    windowless process/service");
            sb.AppendLine($"Process:   {proc.ProcessName} (pid {proc.ProcessId})");
            sb.AppendLine($"Path:      {proc.ExecutablePath ?? "(inaccessible)"}");
        }
        else
        {
            sb.AppendLine("Target:    unknown (no window or process info supplied)");
        }

        if (request.AppliedProfiles.Count > 0)
        {
            sb.AppendLine($"Profiles:  {string.Join(", ", request.AppliedProfiles)}");
        }

        if (request.ProcessInfo is { } info)
        {
            sb.AppendLine();
            sb.AppendLine("Process details:");
            sb.AppendLine($"  Started:      {info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown"}");
            sb.AppendLine($"  Working set:  {info.WorkingSetBytes / (1024 * 1024)} MB");
            sb.AppendLine($"  Private mem:  {info.PrivateMemoryBytes / (1024 * 1024)} MB");
            sb.AppendLine($"  Threads:      {info.ThreadCount}");
            sb.AppendLine($"  Responding:   {info.Responding?.ToString() ?? "unknown"}");
        }

        if (request.Blockers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Suspected modal blockers:");
            foreach (var blocker in request.Blockers)
            {
                string visibility = blocker.BlockerIsNotOnScreen
                    ? "off-screen"
                    : blocker.BlockerIsBehindBlockedWindow ? "hidden behind owner" : "visible";
                sb.AppendLine($"  \"{blocker.BlockedWindowTitle}\" blocked by \"{blocker.BlockerTitle}\" [{visibility}]");
                if (blocker.BlockerContent is not null)
                {
                    sb.AppendLine($"    dialog says: {blocker.BlockerContent}");
                }
            }
        }

        if (request.ProcessWindows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Windows of this process:");
            foreach (var window in request.ProcessWindows)
            {
                sb.AppendLine($"  [{window.Status}] \"{window.Title}\"");
            }
        }

        if (request.MinidumpTempPath is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Minidump:  captured ({request.MinidumpDetail ?? "triage"} level) → minidump.dmp");
        }

        if (request.WaitChainInsights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Wait-chain analysis:");
            foreach (string insight in request.WaitChainInsights)
            {
                sb.AppendLine($"  - {insight}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(request.Artifacts.Count > 0
            ? $"Artifacts preserved into recovered-files/ ({request.Artifacts.Count} source(s))."
            : "No recovery artifacts found by the applied profiles.");

        return sb.ToString();
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length > 0 ? cleaned : "unknown";
    }
}
