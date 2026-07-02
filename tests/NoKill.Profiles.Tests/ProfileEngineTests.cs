using NoKill.Profiles;

namespace NoKill.Profiles.Tests;

public class RescueProfileTests
{
    [Fact]
    public void EmptyProcessNames_AppliesToEveryProcess()
    {
        Assert.True(BuiltInProfiles.Universal.AppliesTo("anything"));
        Assert.True(BuiltInProfiles.Universal.AppliesTo("some-obscure-service"));
    }

    [Theory]
    [InlineData("blender")]
    [InlineData("Blender")]
    [InlineData("BLENDER.EXE")]
    [InlineData("blender.exe")]
    public void ProcessNameMatch_IsCaseAndExtensionInsensitive(string name)
    {
        var blender = BuiltInProfiles.All.Single(p => p.Id == "blender");
        Assert.True(blender.AppliesTo(name));
    }

    [Fact]
    public void NonMatchingProcess_OnlyGetsUniversalProfile()
    {
        var applied = BuiltInProfiles.All.Where(p => p.AppliesTo("weird-custom-tool")).ToList();

        var only = Assert.Single(applied);
        Assert.Equal("universal", only.Id);
    }
}

public sealed class ArtifactCollectorTests : IDisposable
{
    private readonly string _root;

    public ArtifactCollectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private static RescueProfile ProfileWith(params ArtifactRule[] rules) => new()
    {
        Id = "test",
        DisplayName = "Test",
        ArtifactRules = rules,
    };

    private static ProcessContext Context(string name = "myapp", string? exe = null) => new(name, exe);

    [Fact]
    public void TokenExpansion_ProcessNameAndPattern()
    {
        string appDir = Path.Combine(_root, "myapp");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "myapp-session.log"), "x");
        File.WriteAllText(Path.Combine(appDir, "other.txt"), "x");

        var profile = ProfileWith(new ArtifactRule
        {
            Path = Path.Combine(_root, "{ProcessName}"),
            FilePattern = "{ProcessName}*",
            Category = "logs",
        });

        var sources = ArtifactCollector.Collect([profile], Context());

        var source = Assert.Single(sources);
        Assert.EndsWith("myapp-session.log", source.SourcePath);
    }

    [Fact]
    public void AgeFilter_ExcludesOldFiles()
    {
        string fresh = Path.Combine(_root, "fresh.log");
        string stale = Path.Combine(_root, "stale.log");
        File.WriteAllText(fresh, "x");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-90));

        var profile = ProfileWith(new ArtifactRule
        {
            Path = _root,
            FilePattern = "*.log",
            Category = "logs",
            MaxAgeDays = 7,
        });

        var sources = ArtifactCollector.Collect([profile], Context());

        var source = Assert.Single(sources);
        Assert.EndsWith("fresh.log", source.SourcePath);
    }

    [Fact]
    public void MaxFiles_KeepsNewestFirst()
    {
        for (int i = 0; i < 5; i++)
        {
            string file = Path.Combine(_root, $"file{i}.log");
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-i));
        }

        var profile = ProfileWith(new ArtifactRule
        {
            Path = _root,
            FilePattern = "*.log",
            Category = "logs",
            MaxFiles = 2,
        });

        var sources = ArtifactCollector.Collect([profile], Context());

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, s => s.SourcePath.EndsWith("file0.log")); // newest
        Assert.Contains(sources, s => s.SourcePath.EndsWith("file1.log"));
    }

    [Fact]
    public void WildcardDirectorySegments_Expand()
    {
        string a = Path.Combine(_root, "Visual Studio 2022", "Backup Files");
        string b = Path.Combine(_root, "Visual Studio 2026", "Backup Files");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        File.WriteAllText(Path.Combine(a, "one.cs"), "x");
        File.WriteAllText(Path.Combine(b, "two.cs"), "x");

        var profile = ProfileWith(new ArtifactRule
        {
            Path = Path.Combine(_root, "Visual Studio*", "Backup Files"),
            Category = "backup",
        });

        var sources = ArtifactCollector.Collect([profile], Context());

        Assert.Equal(2, sources.Count);
    }

    [Fact]
    public void ExeDirToken_WithoutExecutablePath_SkipsRuleQuietly()
    {
        var profile = ProfileWith(new ArtifactRule
        {
            Path = @"{ExeDir}\logs",
            Category = "logs",
        });

        var sources = ArtifactCollector.Collect([profile], Context(exe: null));

        Assert.Empty(sources); // no crash, no match
    }

    [Fact]
    public void MissingDirectories_AreNotAnError()
    {
        var profile = ProfileWith(new ArtifactRule
        {
            Path = Path.Combine(_root, "does", "not", "exist"),
            Category = "logs",
        });

        Assert.Empty(ArtifactCollector.Collect([profile], Context()));
    }

    [Fact]
    public void DuplicateMatchesAcrossRules_AreDeduplicated()
    {
        File.WriteAllText(Path.Combine(_root, "app.log"), "x");

        var profile = ProfileWith(
            new ArtifactRule { Path = _root, FilePattern = "*.log", Category = "logs" },
            new ArtifactRule { Path = _root, FilePattern = "app.*", Category = "backup" });

        Assert.Single(ArtifactCollector.Collect([profile], Context()));
    }
}

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _userDir;

    public ProfileStoreTests()
    {
        _userDir = Path.Combine(Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_userDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_userDir, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void NoUserDirectory_ReturnsBuiltInsOnly()
    {
        var store = new ProfileStore(Path.Combine(_userDir, "missing"));

        var (profiles, warnings) = store.LoadAll();

        Assert.Equal(BuiltInProfiles.All.Count, profiles.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void UserProfile_IsAdded()
    {
        File.WriteAllText(Path.Combine(_userDir, "myapp.json"), """
            {
              "Id": "my-custom-app",
              "DisplayName": "My Custom App",
              "ProcessNames": ["mycustomapp"],
              "ArtifactRules": [
                { "Path": "%APPDATA%\\MyCustomApp", "Category": "autosave", "FilePattern": "*.sav" }
              ]
            }
            """);

        var (profiles, warnings) = new ProfileStore(_userDir).LoadAll();

        Assert.Empty(warnings);
        var custom = profiles.Single(p => p.Id == "my-custom-app");
        Assert.True(custom.AppliesTo("MyCustomApp.exe"));
        Assert.Single(custom.ArtifactRules);
    }

    [Fact]
    public void UserProfile_OverridesBuiltInWithSameId()
    {
        File.WriteAllText(Path.Combine(_userDir, "blender-fix.json"), """
            { "Id": "blender", "DisplayName": "Blender (corrected)", "ProcessNames": ["blender"] }
            """);

        var (profiles, _) = new ProfileStore(_userDir).LoadAll();

        var blender = profiles.Single(p => p.Id == "blender");
        Assert.Equal("Blender (corrected)", blender.DisplayName);
        Assert.Empty(blender.ArtifactRules); // fully replaced, not merged
    }

    [Fact]
    public void BrokenJsonFile_ProducesWarningAndKeepsEverythingElse()
    {
        File.WriteAllText(Path.Combine(_userDir, "broken.json"), "{ not json at all");
        File.WriteAllText(Path.Combine(_userDir, "good.json"), """
            { "Id": "good-app", "DisplayName": "Good", "ProcessNames": ["good"] }
            """);

        var (profiles, warnings) = new ProfileStore(_userDir).LoadAll();

        Assert.Single(warnings);
        Assert.Contains(profiles, p => p.Id == "good-app");
        Assert.Contains(profiles, p => p.Id == "universal");
    }
}
