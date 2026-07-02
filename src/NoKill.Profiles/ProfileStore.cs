using System.Text.Json;

namespace NoKill.Profiles;

/// <summary>
/// Assembles the active profile set: built-ins first, then user-supplied
/// JSON files from Documents\NoKill\Profiles — so NoKill is never limited to
/// the apps we thought of. A user profile with the same Id as a built-in
/// replaces it (letting users correct our paths without a new release).
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _userProfileDirectory;

    public ProfileStore(string? userProfileDirectory = null)
    {
        _userProfileDirectory = userProfileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NoKill", "Profiles");
    }

    public string UserProfileDirectory => _userProfileDirectory;

    public (IReadOnlyList<RescueProfile> Profiles, IReadOnlyList<string> Warnings) LoadAll()
    {
        var byId = BuiltInProfiles.All.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        if (Directory.Exists(_userProfileDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_userProfileDirectory, "*.json"))
            {
                try
                {
                    var profile = JsonSerializer.Deserialize<RescueProfile>(
                        File.ReadAllText(file), JsonOptions);

                    if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
                    {
                        warnings.Add($"Ignored profile without an Id: {file}");
                        continue;
                    }

                    byId[profile.Id] = profile; // user profile wins over built-in
                }
                catch (Exception ex)
                {
                    // One broken user file must not take down the whole profile system.
                    warnings.Add($"Failed to load profile {file}: {ex.Message}");
                }
            }
        }

        return (byId.Values.ToList(), warnings);
    }
}
