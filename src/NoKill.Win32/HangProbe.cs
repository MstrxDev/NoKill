using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NoKill.Win32;

/// <summary>
/// Non-destructive responsiveness probes. WM_NULL is the canonical no-op
/// message: a healthy window answers it immediately and no app treats it as
/// input, so probing cannot change target state.
/// </summary>
public static class HangProbe
{
    /// <summary>
    /// Windows' own hang determination: true when the window has not processed
    /// messages for more than ~5 seconds (the same signal that makes the OS
    /// show "(Not Responding)" and a ghost window).
    /// </summary>
    public static bool IsHungAppWindow(nint hwnd) =>
        PInvoke.IsHungAppWindow(new HWND(hwnd));

    /// <summary>
    /// Sends WM_NULL with SMTO_ABORTIFHUNG and reports whether the window
    /// answered within <paramref name="timeoutMs"/>. SMTO_ABORTIFHUNG makes
    /// the call return immediately for already-hung windows instead of
    /// blocking the probe thread.
    /// </summary>
    /// <returns>True when the ping timed out (a hang signal); false when the window answered.</returns>
    public static unsafe bool PingTimedOut(nint hwnd, uint timeoutMs = 1000)
    {
        nuint result;
        var outcome = PInvoke.SendMessageTimeout(
            new HWND(hwnd),
            PInvoke.WM_NULL,
            default,
            default,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK,
            timeoutMs,
            &result);

        return outcome == 0;
    }
}
