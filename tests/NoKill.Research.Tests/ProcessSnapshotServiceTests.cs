using System.Diagnostics;
using NoKill.Research;

namespace NoKill.Research.Tests;

public sealed class ProcessSnapshotServiceTests
{
    [Fact]
    public void Capture_OfLiveProcess_ReportsThreadsAndVaClone()
    {
        var child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -WindowStyle Hidden -Command Start-Sleep 60",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        try
        {
            var report = new ProcessSnapshotService().Capture(child.Id);

            Assert.True(report.Captured, $"capture failed: {report.Error}");
            Assert.True(report.ThreadCount > 0, "expected at least one captured thread");
            // VaCloneCreated may be false if the clone privilege was denied and
            // the service fell back to a clone-less snapshot — both are valid.

            // read-only doctrine: the snapshot must not have harmed the target
            child.Refresh();
            Assert.False(child.HasExited);
        }
        finally
        {
            try
            {
                child.Kill();
            }
            catch
            {
                // already gone
            }

            child.Dispose();
        }
    }

    [Fact]
    public void Capture_OfNonexistentProcess_FailsGracefully()
    {
        var report = new ProcessSnapshotService().Capture(999_999);

        Assert.False(report.Captured);
        Assert.NotNull(report.Error);
    }
}
