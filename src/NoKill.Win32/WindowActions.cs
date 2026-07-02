using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NoKill.Win32;

/// <summary>
/// The ONLY place in NoKill that changes another window's state, kept in one
/// file so the safety surface stays auditable. Everything here must be
/// non-destructive: z-order and visibility only — never input, never focus
/// theft, never messages the target could interpret as a command.
/// </summary>
public static class WindowActions
{
    /// <summary>
    /// Raises a window to the top of the z-order WITHOUT activating it or
    /// moving/resizing it. Used to reveal a modal dialog stuck behind its
    /// owner; the user still decides what to click.
    /// </summary>
    public static bool BringToTop(nint hwnd) =>
        PInvoke.SetWindowPos(
            new HWND(hwnd),
            HWND.HWND_TOP,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
}
