using System.Diagnostics;
using NoKill.Win32;

namespace NoKill.Diagnostics.Tests;

/// <summary>
/// Live integration test: dumps a real child process and checks the result is
/// a genuine minidump (MDMP signature), and that the child survives untouched.
/// </summary>
public sealed class MiniDumpWriterIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "NoKillTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void TriageDump_OfLiveProcess_ProducesValidMinidump()
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
            string dumpPath = Path.Combine(_dir, "test.dmp");
            var (success, error) = MiniDumpWriter.TryWrite(child.Id, dumpPath, DumpDetail.Triage);

            Assert.True(success, $"dump failed: {error}");

            byte[] header = new byte[4];
            using (var stream = File.OpenRead(dumpPath))
            {
                Assert.Equal(4, stream.Read(header, 0, 4));
            }

            Assert.Equal("MDMP"u8.ToArray(), header); // minidump magic
            Assert.True(new FileInfo(dumpPath).Length > 4096, "dump suspiciously small");

            // read-only doctrine: the target must be alive and untouched
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
    public void DumpOfNonexistentProcess_FailsGracefully()
    {
        var (success, error) = MiniDumpWriter.TryWrite(
            999_999, Path.Combine(_dir, "nope.dmp"), DumpDetail.Triage);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.False(File.Exists(Path.Combine(_dir, "nope.dmp")));
    }
}
