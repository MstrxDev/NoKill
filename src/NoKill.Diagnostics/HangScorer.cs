using NoKill.Core.Models;

namespace NoKill.Diagnostics;

/// <summary>
/// Pure signal-combination logic, deliberately free of any Win32 dependency
/// so it can be unit-tested. The scoring is conservative by design: a busy
/// app (renderer, compiler) and a deadlocked app can look identical on a
/// single signal, and NoKill must not cry wolf.
/// </summary>
public static class HangScorer
{
    public static HangStatus Score(HangSignals signals)
    {
        if (signals.ProbeFailed)
        {
            return HangStatus.Unknown;
        }

        return (signals.IsHungAppWindow, signals.PingTimedOut) switch
        {
            (true, true) => HangStatus.NotResponding,
            // Windows says hung but the window just answered a ping:
            // probably mid-recovery. Strong signal, so still flag it.
            (true, false) => HangStatus.LikelyHung,
            // Ping timeout alone can be a window that is merely slow or busy;
            // IsHungAppWindow has not confirmed. Make no accusation yet.
            (false, true) => HangStatus.Unknown,
            (false, false) => HangStatus.Responsive,
        };
    }
}
