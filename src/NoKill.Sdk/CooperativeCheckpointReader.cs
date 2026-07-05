namespace NoKill.Sdk;

/// <summary>A cooperative checkpoint file discovered on disk.</summary>
public sealed class CheckpointFile
{
    public CheckpointFile(string path, DateTimeOffset lastWriteUtc, long sizeBytes)
    {
        Path = path;
        LastWriteUtc = lastWriteUtc;
        SizeBytes = sizeBytes;
    }

    public string Path { get; }

    public DateTimeOffset LastWriteUtc { get; }

    public long SizeBytes { get; }
}

/// <summary>
/// NoKill's side of cooperative recovery: discovers the checkpoints an app
/// wrote via <see cref="RecoveryCheckpoint"/>, so a rescue can preserve the
/// app's own emergency state alongside the rest of the evidence. Read-only.
/// </summary>
public sealed class CooperativeCheckpointReader
{
    private readonly string _root;

    public CooperativeCheckpointReader(string? rootDirectory = null)
    {
        _root = rootDirectory ?? CooperativeRecovery.RootDirectory;
    }

    /// <summary>True when any app has registered cooperative checkpoints at all.</summary>
    public bool HasAnyCheckpoints() => Directory.Exists(_root) && Directory.EnumerateDirectories(_root).Any();

    /// <summary>
    /// Latest checkpoints for an app id, newest first. Matching is
    /// case-insensitive and tolerant of the process-name/app-id mismatch by
    /// also trying the app id as-is.
    /// </summary>
    public IReadOnlyList<CheckpointFile> GetCheckpoints(string appId, int max = 10)
    {
        string dir = CooperativeRecovery.DirectoryForApp(appId);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new CheckpointFile(f, info.LastWriteTimeUtc, info.Length);
            })
            .OrderByDescending(c => c.LastWriteUtc)
            .Take(max)
            .ToList();
    }
}
