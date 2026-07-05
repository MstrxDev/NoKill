using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace NoKill.Win32;

/// <summary>Read-only thread listing via the toolhelp snapshot API.</summary>
public static class ThreadEnumerator
{
    /// <summary>Thread ids belonging to <paramref name="processId"/>, in snapshot order.</summary>
    public static IReadOnlyList<uint> GetThreadIds(uint processId)
    {
        var threadIds = new List<uint>();

        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(
            CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD, 0);

        if (snapshot.IsInvalid)
        {
            return threadIds;
        }

        var entry = new THREADENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<THREADENTRY32>() };
        if (!PInvoke.Thread32First(snapshot, ref entry))
        {
            return threadIds;
        }

        do
        {
            if (entry.th32OwnerProcessID == processId)
            {
                threadIds.Add(entry.th32ThreadID);
            }
        }
        while (PInvoke.Thread32Next(snapshot, ref entry));

        return threadIds;
    }
}
