namespace NoKill.Sdk;

/// <summary>
/// Shared conventions for where cooperative recovery checkpoints live, used by
/// both the writer (embedded in apps) and NoKill's reader. One directory per
/// app under the local profile; checkpoints are plain files NoKill can copy.
/// </summary>
public static class CooperativeRecovery
{
    /// <summary>Root of all cooperative checkpoints: %LOCALAPPDATA%\NoKill\Cooperative.</summary>
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NoKill", "Cooperative");

    /// <summary>The checkpoint directory for one app id (sanitized).</summary>
    public static string DirectoryForApp(string appId) =>
        Path.Combine(RootDirectory, Sanitize(appId));

    internal static string Sanitize(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = appId.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        return new string(chars);
    }
}
