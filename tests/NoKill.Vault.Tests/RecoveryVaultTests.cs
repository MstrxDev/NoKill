using NoKill.Core.Models;
using NoKill.Vault;

namespace NoKill.Vault.Tests;

public sealed class RecoveryVaultTests : IDisposable
{
    private readonly string _vaultRoot;
    private readonly string _artifactDir;

    public RecoveryVaultTests()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"));
        _vaultRoot = Path.Combine(testRoot, "vault");
        _artifactDir = Path.Combine(testRoot, "artifacts");
        Directory.CreateDirectory(_artifactDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_vaultRoot)!, recursive: true);
        }
        catch
        {
            // best-effort cleanup of temp data
        }
    }

    private static AppWindowInfo Target(string title = "Frozen App", string process = "frozen") => new()
    {
        WindowHandle = 0x42,
        Title = title,
        ProcessId = 1234,
        ProcessName = process,
        Status = HangStatus.NotResponding,
        Signals = new HangSignals(true, true),
        CapturedAt = DateTimeOffset.Now,
    };

    [Fact]
    public void Preserve_WritesReportsAndReturnsEntryDirectory()
    {
        var vault = new RecoveryVault(_vaultRoot);

        var result = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });

        Assert.True(Directory.Exists(result.EntryDirectory));
        Assert.Contains(result.SavedFiles, f => f.EndsWith("rescue-report.json"));
        Assert.Contains(result.SavedFiles, f => f.EndsWith("rescue-report.txt"));
        Assert.Empty(result.Warnings);

        string txt = File.ReadAllText(Path.Combine(result.EntryDirectory, "rescue-report.txt"));
        Assert.Contains("Frozen App", txt);
        Assert.Contains("NotResponding", txt);

        // window handles serialize as numbers (nint needs the custom converter)
        string json = File.ReadAllText(Path.Combine(result.EntryDirectory, "rescue-report.json"));
        Assert.Contains("\"WindowHandle\": 66", json);
    }

    [Fact]
    public void Preserve_CopiesArtifacts_AndNeverTouchesSources()
    {
        string autosave = Path.Combine(_artifactDir, "project.autosave");
        File.WriteAllText(autosave, "unsaved work");
        var originalWriteTime = File.GetLastWriteTimeUtc(autosave);

        var vault = new RecoveryVault(_vaultRoot);
        var result = vault.Preserve(new VaultEntryRequest
        {
            TargetWindow = Target(),
            Artifacts = [new ArtifactSource(autosave, "autosave")],
        });

        string copied = Path.Combine(result.EntryDirectory, "recovered-files", "autosave", "project.autosave");
        Assert.Equal("unsaved work", File.ReadAllText(copied));

        // source untouched: still exists, same content, same timestamp
        Assert.True(File.Exists(autosave));
        Assert.Equal("unsaved work", File.ReadAllText(autosave));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(autosave));
    }

    [Fact]
    public void Preserve_CopiesDirectoryArtifactsRecursively()
    {
        string sub = Path.Combine(_artifactDir, "logs", "nested");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_artifactDir, "logs", "app.log"), "log1");
        File.WriteAllText(Path.Combine(sub, "deep.log"), "log2");

        var vault = new RecoveryVault(_vaultRoot);
        var result = vault.Preserve(new VaultEntryRequest
        {
            TargetWindow = Target(),
            Artifacts = [new ArtifactSource(Path.Combine(_artifactDir, "logs"), "logs")],
        });

        string root = Path.Combine(result.EntryDirectory, "recovered-files", "logs");
        Assert.Equal("log1", File.ReadAllText(Path.Combine(root, "app.log")));
        Assert.Equal("log2", File.ReadAllText(Path.Combine(root, "nested", "deep.log")));
    }

    [Fact]
    public void MissingArtifact_ProducesWarningNotException()
    {
        var vault = new RecoveryVault(_vaultRoot);

        var result = vault.Preserve(new VaultEntryRequest
        {
            TargetWindow = Target(),
            Artifacts = [new ArtifactSource(Path.Combine(_artifactDir, "does-not-exist.tmp"), "autosave")],
        });

        Assert.True(Directory.Exists(result.EntryDirectory)); // preserve still happened
        Assert.Contains(result.Warnings, w => w.Contains("not found"));
    }

    [Fact]
    public void RepeatedPreserve_NeverReusesAnEntryDirectory()
    {
        var vault = new RecoveryVault(_vaultRoot);
        var request = new VaultEntryRequest { TargetWindow = Target() };

        var first = vault.Preserve(request);
        var second = vault.Preserve(request); // same second, same process → suffix

        Assert.NotEqual(first.EntryDirectory, second.EntryDirectory);
        Assert.True(Directory.Exists(first.EntryDirectory));
        Assert.True(Directory.Exists(second.EntryDirectory));
    }

    [Fact]
    public void HostileProcessName_IsSanitizedInFolderName()
    {
        var vault = new RecoveryVault(_vaultRoot);

        var result = vault.Preserve(new VaultEntryRequest
        {
            TargetWindow = Target(process: @"evil\..\name:*?"),
        });

        // entry stays inside the vault root
        string fullEntry = Path.GetFullPath(result.EntryDirectory);
        Assert.StartsWith(Path.GetFullPath(_vaultRoot), fullEntry);
        Assert.True(Directory.Exists(result.EntryDirectory));
    }

    [Fact]
    public void WindowlessProcess_CanStillBePreserved()
    {
        var vault = new RecoveryVault(_vaultRoot);

        var result = vault.Preserve(new VaultEntryRequest
        {
            // no TargetWindow: a service or background process
            ProcessInfo = new ProcessInfoSnapshot
            {
                ProcessId = 999,
                ProcessName = "someservice",
                CapturedAt = DateTimeOffset.Now,
            },
            AppliedProfiles = ["Universal heuristics"],
        });

        Assert.Contains("someservice_999", result.EntryDirectory);
        string txt = File.ReadAllText(Path.Combine(result.EntryDirectory, "rescue-report.txt"));
        Assert.Contains("windowless process/service", txt);
        Assert.Contains("Universal heuristics", txt);
    }

    [Fact]
    public void Screenshot_IsSavedWhenProvided()
    {
        var vault = new RecoveryVault(_vaultRoot);
        byte[] png = [1, 2, 3, 4];

        var result = vault.Preserve(new VaultEntryRequest
        {
            TargetWindow = Target(),
            ScreenshotPng = png,
        });

        Assert.Equal(png, File.ReadAllBytes(Path.Combine(result.EntryDirectory, "screenshot.png")));
    }
}
