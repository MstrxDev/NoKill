using System.Runtime.InteropServices;

namespace HungDemoApp;

internal static partial class NativeMethods
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Z-orders <paramref name="window"/> directly behind <paramref name="owner"/>.</summary>
    internal static void PlaceBehindOwner(nint window, nint owner) =>
        SetWindowPos(window, owner, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

    // Raw kernel mutex + wait. Deliberately NOT System.Threading.Mutex:
    // .NET's wait machinery is invisible to Wait Chain Traversal, and WPF's
    // synchronization context pumps messages during managed waits. Raw waits
    // give NoKill a real, diagnosable, WCT-visible deadlock.
    internal const uint Infinite = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateMutexW(nint lpMutexAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInitialOwner, string? lpName);

    [LibraryImport("kernel32.dll")]
    internal static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
}
