namespace NoKill.Core.Models;

/// <summary>
/// Conservative responsiveness classification for a top-level window.
/// A window is only classified as <see cref="NotResponding"/> when multiple
/// independent signals agree; single-signal results stay in the softer buckets.
/// </summary>
public enum HangStatus
{
    /// <summary>The window is processing messages normally.</summary>
    Responsive,

    /// <summary>Signals disagree or could not be collected; make no claim.</summary>
    Unknown,

    /// <summary>At least one strong hang signal fired, but not all of them.</summary>
    LikelyHung,

    /// <summary>All hang signals agree the window has stopped processing messages.</summary>
    NotResponding,
}
