namespace NoKill.Profiles.Tests;

/// <summary>
/// Guards the built-in profile DATA: profiles are easy to add and easy to get
/// subtly wrong, so structural rules are enforced by tests rather than review.
/// </summary>
public class BuiltInProfileIntegrityTests
{
    [Fact]
    public void AllProfiles_HaveUniqueIds()
    {
        var ids = BuiltInProfiles.All.Select(p => p.Id.ToLowerInvariant()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void OnlyTheUniversalProfile_AppliesToEverything()
    {
        var universal = BuiltInProfiles.All.Where(p => p.ProcessNames.Count == 0).ToList();

        var only = Assert.Single(universal);
        Assert.Equal("universal", only.Id);
    }

    [Fact]
    public void EveryProfile_HasAtLeastOneRule_AndWellFormedRules()
    {
        foreach (var profile in BuiltInProfiles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.Id));
            Assert.False(string.IsNullOrWhiteSpace(profile.DisplayName));
            Assert.NotEmpty(profile.ArtifactRules);

            foreach (var rule in profile.ArtifactRules)
            {
                Assert.False(string.IsNullOrWhiteSpace(rule.Path), $"{profile.Id}: empty rule path");
                Assert.False(string.IsNullOrWhiteSpace(rule.Category), $"{profile.Id}: empty category");
                Assert.True(rule.MaxFiles > 0, $"{profile.Id}: MaxFiles must cap the rule");
                Assert.True(rule.MaxAgeDays >= 0, $"{profile.Id}: negative age filter");
            }
        }
    }

    [Theory]
    [InlineData("Photoshop", "photoshop")]
    [InlineData("Adobe Premiere Pro", "premiere-pro")]
    [InlineData("FL64", "fl-studio")]
    [InlineData("obs64", "obs-studio")]
    [InlineData("Unity", "unity")]
    [InlineData("firefox.exe", "firefox")]
    [InlineData("msedge", "msedge")]
    [InlineData("Notepad", "windows-notepad")]
    [InlineData("Audacity", "audacity")]
    [InlineData("krita", "krita")]
    [InlineData("rider64", "jetbrains-ides")]
    [InlineData("IDEA64.EXE", "jetbrains-ides")]
    [InlineData("sublime_text", "sublime-text")]
    [InlineData("paintdotnet", "paint-dot-net")]
    [InlineData("RobloxPlayerBeta", "roblox-studio")]
    [InlineData("blender", "blender")]
    [InlineData("devenv", "visual-studio")]
    [InlineData("WINWORD", "ms-office")]
    public void KnownProcess_RoutesToItsProfile(string processName, string expectedProfileId)
    {
        var matches = BuiltInProfiles.All
            .Where(p => p.ProcessNames.Count > 0 && p.AppliesTo(processName))
            .ToList();

        var match = Assert.Single(matches); // exactly one specific profile per app
        Assert.Equal(expectedProfileId, match.Id);
    }

    [Fact]
    public void NoProcessName_AppearsInTwoProfiles()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in BuiltInProfiles.All)
        {
            foreach (string name in profile.ProcessNames)
            {
                bool added = owners.TryAdd(name, profile.Id);
                Assert.True(added,
                    $"process name '{name}' claimed by both '{owners[name]}' and '{profile.Id}'");
            }
        }
    }
}
