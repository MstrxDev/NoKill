namespace NoKill.Win32;

/// <summary>
/// Raw enumeration result for one top-level window, before any process
/// resolution or hang scoring happens in higher layers.
/// </summary>
/// <param name="ZOrder">
/// Position in the enumeration pass: EnumWindows walks top-to-bottom, so a
/// larger value means further behind on the desktop.
/// </param>
/// <param name="OwnerHandle">
/// Owning top-level window (GW_OWNER), or 0. Modal dialogs are owned windows.
/// </param>
/// <param name="IsEnabled">
/// False usually means a modal dialog somewhere is blocking this window.
/// </param>
public readonly record struct TopLevelWindow(
    nint Handle,
    string Title,
    uint ProcessId,
    bool IsVisible,
    bool IsCloaked,
    bool IsEnabled,
    nint OwnerHandle,
    int ZOrder);
