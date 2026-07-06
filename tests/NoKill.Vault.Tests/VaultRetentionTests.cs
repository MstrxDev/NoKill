using NoKill.Core.Models;

namespace NoKill.Vault.Tests;

public sealed class VaultRetentionTests : IDisposable
{
    private readonly string _vaultRoot;

    public VaultRetentionTests()
    {
        _vaultRoot = Path.Combine(
            Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"), "vault");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_vaultRoot)!, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private static AppWindowInfo Target(string process = "app") => new()
    {
        WindowHandle = 1,
        Title = "T",
        ProcessId = 1,
        ProcessName = process,
        Status = HangStatus.NotResponding,
        Signals = new HangSignals(true, true),
        CapturedAt = DateTimeOffset.Now,
    };

    /// <summary>Backdates an entry so the oldest-first ordering is deterministic.</summary>
    private static void Backdate(string entryDir, int days) =>
        Directory.SetCreationTimeUtc(entryDir, DateTime.UtcNow.AddDays(-days));

    [Fact]
    public void EntryCountCap_PrunesOldestFirst_NeverTheCurrentEntry()
    {
        var vault = new RecoveryVault(_vaultRoot, new VaultRetentionOptions
        {
            MaxEntries = 2, MaxTotalBytes = 0, MaxAgeDays = 0,
        });

        var first = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });
        Backdate(first.EntryDirectory, 3);
        var second = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });
        Backdate(second.EntryDirectory, 2);

        var third = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });

        var prunedEntry = Assert.Single(third.PrunedEntries);
        Assert.Equal(first.EntryDirectory, prunedEntry);       // oldest went
        Assert.False(Directory.Exists(first.EntryDirectory));
        Assert.True(Directory.Exists(second.EntryDirectory));  // newer survivor
        Assert.True(Directory.Exists(third.EntryDirectory));   // current never pruned
    }

    [Fact]
    public void SizeCap_PrunesUntilUnderBudget()
    {
        var vault = new RecoveryVault(_vaultRoot, new VaultRetentionOptions
        {
            MaxTotalBytes = 250_000, MaxEntries = 0, MaxAgeDays = 0,
        });

        // three entries of ~100 KB each via a staged "minidump"
        var results = new List<VaultEntryResult>();
        for (int i = 0; i < 3; i++)
        {
            string dump = vault.CreateTempFilePath(".dmp");
            File.WriteAllBytes(dump, new byte[100_000]);
            var result = vault.Preserve(new VaultEntryRequest
            {
                TargetWindow = Target(),
                MinidumpTempPath = dump,
            });
            Backdate(result.EntryDirectory, 10 - i);
            results.Add(result);
        }

        // first two fit (200 KB); the third pushes past 250 KB → oldest pruned
        Assert.Empty(results[0].PrunedEntries);
        Assert.Empty(results[1].PrunedEntries);
        Assert.Equal(results[0].EntryDirectory, Assert.Single(results[2].PrunedEntries));
        Assert.True(Directory.Exists(results[2].EntryDirectory));
    }

    [Fact]
    public void AgeCap_PrunesExpiredEntries_WhenEnabled()
    {
        var vault = new RecoveryVault(_vaultRoot, new VaultRetentionOptions
        {
            MaxAgeDays = 30, MaxEntries = 0, MaxTotalBytes = 0,
        });

        var old = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });
        Backdate(old.EntryDirectory, 45);

        // the very next preserve notices the expired entry and prunes it
        var recent = vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });

        Assert.Equal(old.EntryDirectory, Assert.Single(recent.PrunedEntries));
        Assert.False(Directory.Exists(old.EntryDirectory));
        Assert.True(Directory.Exists(recent.EntryDirectory));
    }

    [Fact]
    public void AllCapsDisabled_NothingIsEverPruned()
    {
        var vault = new RecoveryVault(_vaultRoot, new VaultRetentionOptions
        {
            MaxEntries = 0, MaxTotalBytes = 0, MaxAgeDays = 0,
        });

        var results = Enumerable.Range(0, 5)
            .Select(_ => vault.Preserve(new VaultEntryRequest { TargetWindow = Target() }))
            .ToList();

        Assert.All(results, r => Assert.Empty(r.PrunedEntries));
        Assert.Equal(5, Directory.GetDirectories(_vaultRoot).Count(d => !Path.GetFileName(d).StartsWith('.')));
    }

    [Fact]
    public void StagingDirectory_IsNeverCountedOrPruned()
    {
        var vault = new RecoveryVault(_vaultRoot, new VaultRetentionOptions
        {
            MaxEntries = 1, MaxTotalBytes = 0, MaxAgeDays = 0,
        });

        // materialize .tmp with a file in it
        string staged = vault.CreateTempFilePath(".dmp");
        File.WriteAllBytes(staged, [1, 2, 3]);

        vault.Preserve(new VaultEntryRequest { TargetWindow = Target() });

        Assert.True(File.Exists(staged)); // .tmp untouched by retention
        var stats = vault.GetStats();
        Assert.Equal(1, stats.EntryCount);
    }

    [Fact]
    public void GetStats_ReportsEntryCountAndBytes()
    {
        var vault = new RecoveryVault(_vaultRoot);
        Assert.Equal((0, 0L), vault.GetStats());

        string dump = vault.CreateTempFilePath(".dmp");
        File.WriteAllBytes(dump, new byte[50_000]);
        vault.Preserve(new VaultEntryRequest { TargetWindow = Target(), MinidumpTempPath = dump });

        var stats = vault.GetStats();
        Assert.Equal(1, stats.EntryCount);
        Assert.True(stats.TotalBytes >= 50_000);
    }
}
