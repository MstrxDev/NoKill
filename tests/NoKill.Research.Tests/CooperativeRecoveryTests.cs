using NoKill.Sdk;

namespace NoKill.Research.Tests;

public sealed class CooperativeRecoveryTests : IDisposable
{
    private readonly string _root;

    public CooperativeRecoveryTests()
    {
        // Redirect the SDK root by using a unique app id under the real root,
        // then clean it up. Keeps the test off shared state.
        _appId = "NoKillTest_" + Guid.NewGuid().ToString("N");
        _root = CooperativeRecovery.DirectoryForApp(_appId);
    }

    private readonly string _appId;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Save_ThenReader_FindsCheckpointNewestFirst()
    {
        var checkpoint = new RecoveryCheckpoint(_appId);
        checkpoint.Save("first draft", "document");
        Thread.Sleep(5);
        checkpoint.Save("second draft", "document");

        var reader = new CooperativeCheckpointReader();
        var found = reader.GetCheckpoints(_appId);

        Assert.NotEmpty(found);
        // newest first: the most recent write leads
        Assert.Equal("second draft", File.ReadAllText(found[0].Path));
    }

    [Fact]
    public void Save_KeepsOnlyRecentCheckpoints()
    {
        var checkpoint = new RecoveryCheckpoint(_appId, keepRecent: 2);
        for (int i = 0; i < 5; i++)
        {
            checkpoint.Save($"edit {i}", "document");
            Thread.Sleep(3);
        }

        var kept = new CooperativeCheckpointReader().GetCheckpoints(_appId, max: 100);
        Assert.Equal(2, kept.Count); // ring buffer pruned the rest
    }

    [Fact]
    public void Reader_UnknownApp_ReturnsEmpty()
    {
        var found = new CooperativeCheckpointReader().GetCheckpoints("no-such-app-" + Guid.NewGuid());
        Assert.Empty(found);
    }

    [Fact]
    public void Save_IsAtomic_NoTempFilesLeftBehind()
    {
        var checkpoint = new RecoveryCheckpoint(_appId);
        checkpoint.Save("content", "document");

        var temps = Directory.GetFiles(_root, "*.tmp");
        Assert.Empty(temps);
    }

    [Fact]
    public void EmptyAppId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RecoveryCheckpoint("  "));
    }
}
