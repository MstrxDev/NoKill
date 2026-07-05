namespace NoKill.Vault.Tests;

public sealed class FreezeHistoryTests : IDisposable
{
    private readonly string _dbPath;

    public FreezeHistoryTests()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"), "history.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path.GetDirectoryName(_dbPath)!, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void RecordIncident_CreatesSchemaAndReturnsId()
    {
        var history = new FreezeHistory(_dbPath);

        long id = history.RecordIncident(
            "blender", 1234, @"C:\apps\blender.exe", "watchdog",
            @"C:\vault\entry1", "DEADLOCK: wait cycle detected");

        Assert.True(id > 0);
        Assert.True(File.Exists(_dbPath));

        var record = Assert.Single(history.GetRecent());
        Assert.Equal("blender", record.ProcessName);
        Assert.Equal(1234, record.ProcessId);
        Assert.Equal("watchdog", record.Trigger);
        Assert.Equal(@"C:\vault\entry1", record.VaultEntryPath);
        Assert.StartsWith("DEADLOCK", record.Insight);
        Assert.Null(record.EndedAt);
    }

    [Fact]
    public void MarkEnded_SetsEndedAt_Once()
    {
        var history = new FreezeHistory(_dbPath);
        long id = history.RecordIncident("app", 1, null, "watchdog", null, null,
            startedAt: DateTimeOffset.Now.AddMinutes(-2));

        var firstEnd = DateTimeOffset.Now;
        history.MarkEnded(id, firstEnd);
        history.MarkEnded(id, firstEnd.AddHours(1)); // second call must not overwrite

        var record = Assert.Single(history.GetRecent());
        Assert.NotNull(record.EndedAt);
        Assert.Equal(firstEnd, record.EndedAt!.Value);
    }

    [Fact]
    public void GetRecent_ReturnsNewestFirst_AndHonorsLimit()
    {
        var history = new FreezeHistory(_dbPath);
        var t0 = DateTimeOffset.Now.AddHours(-3);
        for (int i = 0; i < 5; i++)
        {
            history.RecordIncident($"app{i}", i, null, "manual", null, null, t0.AddMinutes(i));
        }

        var recent = history.GetRecent(3);

        Assert.Equal(3, recent.Count);
        Assert.Equal("app4", recent[0].ProcessName); // newest first
        Assert.Equal("app2", recent[2].ProcessName);
    }

    [Fact]
    public void GetForProcess_FiltersCaseInsensitively()
    {
        var history = new FreezeHistory(_dbPath);
        history.RecordIncident("Blender", 1, null, "manual", null, null);
        history.RecordIncident("chrome", 2, null, "manual", null, null);
        history.RecordIncident("blender", 3, null, "watchdog", null, null);

        var records = history.GetForProcess("BLENDER");

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal("blender", r.ProcessName, ignoreCase: true));
    }

    [Fact]
    public void GetTopOffenders_AggregatesByProcess_MostFrequentFirst()
    {
        var history = new FreezeHistory(_dbPath);
        var t0 = DateTimeOffset.Now.AddHours(-1);
        history.RecordIncident("chrome", 1, null, "watchdog", null, null, t0);
        history.RecordIncident("chrome", 2, null, "watchdog", null, null, t0.AddMinutes(10));
        history.RecordIncident("chrome", 3, null, "manual", null, null, t0.AddMinutes(20));
        history.RecordIncident("blender", 4, null, "watchdog", null, null, t0.AddMinutes(30));

        var offenders = history.GetTopOffenders();

        Assert.Equal(2, offenders.Count);
        Assert.Equal("chrome", offenders[0].ProcessName);
        Assert.Equal(3, offenders[0].IncidentCount);
        Assert.Equal(1, offenders[1].IncidentCount);
    }

    [Fact]
    public void ReopeningDatabase_KeepsExistingHistory()
    {
        long id = new FreezeHistory(_dbPath).RecordIncident("app", 1, null, "manual", null, null);

        var reopened = new FreezeHistory(_dbPath);
        var record = Assert.Single(reopened.GetRecent());
        Assert.Equal(id, record.Id);
    }
}
