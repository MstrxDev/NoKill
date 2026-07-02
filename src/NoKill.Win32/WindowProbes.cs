using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace NoKill.Win32;

/// <summary>Additional read-only window facts that are cheap but not worth collecting for every window.</summary>
public static class WindowProbes
{
    /// <summary>
    /// True when no part of the window intersects any monitor — a dialog
    /// stranded off-screen (disconnected monitor, bad saved position).
    /// </summary>
    public static bool IsOffScreen(nint hwnd) =>
        PInvoke.MonitorFromWindow(new HWND(hwnd), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL) == HMONITOR.Null;
}
