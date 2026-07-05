using System.Text;

namespace NoKill.Sdk;

/// <summary>
/// The app-side of cooperative recovery. An application constructs one of
/// these and calls <see cref="Save"/> periodically — ideally from a background
/// thread or timer, so that even if the UI thread freezes, the most recent
/// checkpoint already sits on disk for NoKill to preserve.
///
/// This is the honest form of the "responsive clone" idea: NoKill can't
/// resurrect arbitrary frozen processes, but an app that cooperates by
/// journaling its unsaved state turns a freeze into a recoverable event.
///
/// Writes are atomic (temp file + move) so a crash mid-write never corrupts
/// the last good checkpoint, and a bounded ring of recent checkpoints is kept.
/// </summary>
public sealed class RecoveryCheckpoint
{
    private readonly string _directory;
    private readonly int _keep;
    private readonly object _gate = new();

    /// <param name="appId">Stable identifier for the app, e.g. "AcmeEditor".</param>
    /// <param name="keepRecent">How many recent checkpoints to retain (ring buffer).</param>
    public RecoveryCheckpoint(string appId, int keepRecent = 3)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException("An app id is required.", nameof(appId));
        }

        _keep = Math.Max(1, keepRecent);
        _directory = CooperativeRecovery.DirectoryForApp(appId);
        Directory.CreateDirectory(_directory);
    }

    public string Directory_ => _directory;

    /// <summary>Saves a text checkpoint (e.g. serialized unsaved document state).</summary>
    public void Save(string content, string label = "state") =>
        Save(Encoding.UTF8.GetBytes(content), label, "txt");

    /// <summary>Saves a binary checkpoint. Atomic: written to a temp file then moved into place.</summary>
    public void Save(byte[] content, string label, string extension)
    {
        lock (_gate)
        {
            string safeLabel = CooperativeRecovery.Sanitize(label);
            string fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeLabel}.{extension}";
            string finalPath = Path.Combine(_directory, fileName);
            string tempPath = finalPath + ".tmp";

            File.WriteAllBytes(tempPath, content);
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);
            PruneOldCheckpoints(safeLabel, extension);
        }
    }

    private void PruneOldCheckpoints(string label, string extension)
    {
        var matching = System.IO.Directory
            .GetFiles(_directory, $"*_{label}.{extension}")
            .OrderByDescending(f => f)
            .Skip(_keep);

        foreach (string old in matching)
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                // a stale checkpoint we couldn't delete is harmless
            }
        }
    }
}
