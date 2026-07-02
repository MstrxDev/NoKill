namespace NoKill.Win32;

/// <summary>
/// Raw enumeration result for one top-level window, before any process
/// resolution or hang scoring happens in higher layers.
/// </summary>
public readonly record struct TopLevelWindow(
    nint Handle,
    string Title,
    uint ProcessId,
    bool IsVisible,
    bool IsCloaked);
