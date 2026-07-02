using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NoKill.Win32;

/// <summary>
/// Read-only enumeration of top-level windows. Never sends input, never
/// modifies window state — the only message traffic is DWM attribute reads.
/// </summary>
public static class WindowEnumerator
{
    /// <summary>
    /// Snapshots all top-level windows in z-order (front to back). Includes
    /// invisible/cloaked ones; callers decide how to filter (the rescue
    /// engine cares about hidden windows precisely because they can be blockers).
    /// </summary>
    public static unsafe IReadOnlyList<TopLevelWindow> Snapshot()
    {
        var results = new List<TopLevelWindow>(128);

        PInvoke.EnumWindows(
            (hwnd, _) =>
            {
                results.Add(Describe(hwnd, results.Count));
                return true; // continue enumeration
            },
            default);

        return results;
    }

    private static unsafe TopLevelWindow Describe(HWND hwnd, int zOrder)
    {
        string title = GetTitle(hwnd);

        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId(hwnd, &pid);

        HWND owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);

        return new TopLevelWindow(
            (nint)hwnd.Value,
            title,
            pid,
            PInvoke.IsWindowVisible(hwnd),
            IsCloaked(hwnd),
            PInvoke.IsWindowEnabled(hwnd),
            (nint)owner.Value,
            zOrder);
    }

    private static string GetTitle(HWND hwnd)
    {
        int length = PInvoke.GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        Span<char> buffer = length <= 512 ? stackalloc char[length + 1] : new char[length + 1];
        int copied = PInvoke.GetWindowText(hwnd, buffer);
        return copied > 0 ? new string(buffer[..copied]) : string.Empty;
    }

    /// <summary>
    /// UWP/store app windows can be "cloaked": present and technically visible,
    /// but not actually shown on any desktop. Treated as not-shown by callers.
    /// </summary>
    private static unsafe bool IsCloaked(HWND hwnd)
    {
        uint cloaked = 0;
        var hr = PInvoke.DwmGetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked,
            sizeof(uint));

        return hr.Succeeded && cloaked != 0;
    }
}
