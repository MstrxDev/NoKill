using System.Diagnostics;
using NoKill.Core.Models;

namespace NoKill.Diagnostics;

/// <summary>Read-only process fact gathering for rescue reports. Fields that need denied access stay null.</summary>
public static class ProcessInspector
{
    public static ProcessInfoSnapshot? TryInspect(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            return new ProcessInfoSnapshot
            {
                ProcessId = pid,
                ProcessName = process.ProcessName,
                ExecutablePath = TryGet(() => process.MainModule?.FileName),
                StartTime = TryGet<DateTimeOffset?>(() => process.StartTime),
                WorkingSetBytes = process.WorkingSet64,
                PrivateMemoryBytes = process.PrivateMemorySize64,
                ThreadCount = process.Threads.Count,
                Responding = TryGet<bool?>(() => process.Responding),
                CapturedAt = DateTimeOffset.Now,
            };
        }
        catch
        {
            return null; // process exited or fully inaccessible
        }
    }

    private static T? TryGet<T>(Func<T> accessor)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return default;
        }
    }
}
