using Microsoft.Win32;

namespace NoKill.App;

/// <summary>
/// The off switch for automatic update checks — local-first doctrine says the
/// product's one outbound call must be user-disableable. Stored per-user.
/// </summary>
internal static class UpdateSettings
{
    private const string KeyPath = @"Software\NoKill";
    private const string ValueName = "CheckForUpdates";

    public static bool AutoCheckEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is not int value || value != 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, value ? 1 : 0);
        }
    }
}
