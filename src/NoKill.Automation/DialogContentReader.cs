using System.Windows.Automation;
using NoKill.Win32;

namespace NoKill.Automation;

/// <summary>
/// Reads the visible content of a dialog (body text + button labels) via
/// UI Automation so rescue reports can say WHAT is asking, not just that
/// something is. Read-only: no patterns are invoked, nothing is clicked.
/// </summary>
public static class DialogContentReader
{
    private const int MaxTextFragments = 6;

    /// <summary>
    /// Returns a one-line summary like
    /// "Save changes before closing? [buttons: Save, Don't Save, Cancel]",
    /// or null when the dialog can't be read safely.
    /// </summary>
    public static string? TryRead(nint dialogHwnd)
    {
        // UIA calls into a hung UI thread can block the caller indefinitely.
        // A dialog that doesn't answer a cheap ping doesn't get UIA treatment.
        if (HangProbe.PingTimedOut(dialogHwnd, timeoutMs: 500))
        {
            return null;
        }

        try
        {
            var root = AutomationElement.FromHandle(dialogHwnd);

            var texts = CollectNames(root, ControlType.Text);
            var buttons = CollectNames(root, ControlType.Button);

            string body = string.Join(" — ", texts);
            string suffix = buttons.Count > 0 ? $" [buttons: {string.Join(", ", buttons)}]" : string.Empty;

            string summary = (body + suffix).Trim();
            return summary.Length > 0 ? summary : null;
        }
        catch
        {
            return null; // dialog closed mid-read, or access denied
        }
    }

    // Titlebar chrome exposed by the non-client-area UIA provider; these are
    // window furniture, not dialog content.
    private static readonly string[] ChromeAutomationIds = ["Minimize", "Maximize", "Restore", "Close", "TitleBar"];

    private static List<string> CollectNames(AutomationElement root, ControlType type)
    {
        var found = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));

        return found
            .Cast<AutomationElement>()
            .Where(e => !ChromeAutomationIds.Contains(e.Current.AutomationId))
            .Select(e => e.Current.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(MaxTextFragments)
            .ToList();
    }
}
