namespace NoKill.Core.Models;

/// <summary>
/// Raw per-window hang evidence collected by the probe layer.
/// Each signal is independent; scoring into a <see cref="HangStatus"/> is
/// deliberately kept separate so it can be unit-tested without Win32.
/// </summary>
/// <param name="IsHungAppWindow">
/// Windows' own ghost-window determination (no message processing for ~5 s).
/// </param>
/// <param name="PingTimedOut">
/// A WM_NULL ping via SendMessageTimeout failed to return within the probe timeout.
/// </param>
/// <param name="ProbeFailed">
/// The probe itself could not run (access denied, window destroyed mid-probe).
/// When true the other signals are meaningless.
/// </param>
public readonly record struct HangSignals(
    bool IsHungAppWindow,
    bool PingTimedOut,
    bool ProbeFailed = false)
{
    public static HangSignals Failed => new(false, false, ProbeFailed: true);
}
