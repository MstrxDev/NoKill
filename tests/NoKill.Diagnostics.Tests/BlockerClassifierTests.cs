using NoKill.Win32;

namespace NoKill.Diagnostics.Tests;

public class BlockerClassifierTests
{
    private static TopLevelWindow Win(
        nint handle,
        int zOrder,
        string title = "",
        bool visible = true,
        bool enabled = true,
        bool cloaked = false,
        nint owner = 0)
        => new(handle, title, ProcessId: 100, visible, cloaked, enabled, owner, zOrder);

    [Fact]
    public void ModalDialogBehindOwner_IsReportedAsHiddenBlocker()
    {
        // z-order: main window (disabled) in FRONT, its modal dialog BEHIND it
        var windows = new[]
        {
            Win(1, zOrder: 0, title: "Main App", enabled: false),
            Win(2, zOrder: 1, title: "Save changes?", owner: 1),
        };

        var findings = BlockerClassifier.FindBlockers(windows);

        var f = Assert.Single(findings);
        Assert.Equal(1, f.Blocked.Handle);
        Assert.Equal(2, f.Blocker.Handle);
        Assert.True(f.BlockerIsBehindBlockedWindow);
    }

    [Fact]
    public void ModalDialogInFrontOfOwner_IsBlockerButNotHidden()
    {
        var windows = new[]
        {
            Win(2, zOrder: 0, title: "Save changes?", owner: 1),
            Win(1, zOrder: 1, title: "Main App", enabled: false),
        };

        var f = Assert.Single(BlockerClassifier.FindBlockers(windows));
        Assert.False(f.BlockerIsBehindBlockedWindow);
    }

    [Fact]
    public void HealthyDesktop_NoDisabledWindows_ProducesNoFindings()
    {
        var windows = new[]
        {
            Win(1, 0, "App A"),
            Win(2, 1, "App B"),
            Win(3, 2, "App C", owner: 1), // owned but nothing is disabled
        };

        Assert.Empty(BlockerClassifier.FindBlockers(windows));
    }

    [Fact]
    public void DisabledWindowWithNoOwnedEnabledWindow_ProducesNoFinding()
    {
        // Disabled for some other reason; there is no dialog to reveal,
        // so accusing anything would be wrong.
        var windows = new[]
        {
            Win(1, 0, "Main App", enabled: false),
            Win(2, 1, "Unrelated"),
        };

        Assert.Empty(BlockerClassifier.FindBlockers(windows));
    }

    [Fact]
    public void NestedModalStack_OnlyTopmostEnabledDialogIsTheBlocker()
    {
        // main (disabled) ← dialog1 (disabled) ← dialog2 (enabled)
        var windows = new[]
        {
            Win(1, zOrder: 0, title: "Main App", enabled: false),
            Win(2, zOrder: 1, title: "Options", enabled: false, owner: 1),
            Win(3, zOrder: 2, title: "Confirm", enabled: true, owner: 2),
        };

        var findings = BlockerClassifier.FindBlockers(windows);

        // dialog2 blocks both the main window and dialog1; it is the only blocker
        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(3, f.Blocker.Handle));
    }

    [Fact]
    public void InvisibleOrUntitledDisabledWindows_AreNotReportedAsBlocked()
    {
        var windows = new[]
        {
            Win(1, 0, title: "", enabled: false),                  // untitled
            Win(2, 1, title: "Ghost", enabled: false, visible: false), // invisible
            Win(3, 2, title: "Dialog", owner: 1),
        };

        Assert.Empty(BlockerClassifier.FindBlockers(windows));
    }

    [Fact]
    public void OwnerCycle_DoesNotHangTheClassifier()
    {
        // Corrupt/hostile owner data must not send the chain walk into a loop.
        var windows = new[]
        {
            Win(1, 0, title: "Main App", enabled: false),
            Win(2, 1, title: "A", owner: 3),
            Win(3, 2, title: "B", owner: 2),
        };

        Assert.Empty(BlockerClassifier.FindBlockers(windows));
    }
}
