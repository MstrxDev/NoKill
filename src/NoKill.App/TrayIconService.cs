using System.ComponentModel;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace NoKill.App;

/// <summary>
/// The resident-guardian face of NoKill: a notification-area icon that stays
/// alive while the dashboard window is hidden. Green when the desktop is
/// calm, red while any app is Not Responding; balloon tips announce watchdog
/// preserves. Icons are drawn at runtime — no binary assets in the repo.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly (Drawing.Icon Icon, nint Handle) _calm = AppIcons.Draw(alert: false);
    private readonly (Drawing.Icon Icon, nint Handle) _alert = AppIcons.Draw(alert: true);
    private readonly WinForms.ToolStripMenuItem _watchdogItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private bool _hideHintShown;
    private bool _disposed;

    public event Action? ExitRequested;

    public TrayIconService(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;

        var menu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Open dashboard", null, (_, _) => RestoreWindow());
        _watchdogItem = new WinForms.ToolStripMenuItem("Watchdog: auto-preserve on freeze")
        {
            CheckOnClick = true,
        };
        _watchdogItem.CheckedChanged += WatchdogItemChanged;
        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows (minimized)")
        {
            CheckOnClick = true,
        };
        _startupItem.CheckedChanged += StartupItemChanged;
        var exitItem = new WinForms.ToolStripMenuItem("Exit NoKill", null, (_, _) => ExitRequested?.Invoke());

        menu.Items.AddRange(
        [
            openItem,
            new WinForms.ToolStripSeparator(),
            _watchdogItem,
            _startupItem,
            new WinForms.ToolStripSeparator(),
            exitItem,
        ]);
        menu.Opening += (_, _) => SyncMenuState();

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _calm.Icon,
            Text = "NoKill — watching for freezes",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.WatchdogNotification += message =>
            _notifyIcon.ShowBalloonTip(4000, "NoKill", message, WinForms.ToolTipIcon.Info);
    }

    /// <summary>First-time hint so a hidden window doesn't look like an exit.</summary>
    public void NotifyHiddenToTray()
    {
        if (!_hideHintShown)
        {
            _hideHintShown = true;
            _notifyIcon.ShowBalloonTip(
                3000, "NoKill is still running",
                "The dashboard is in the notification area. Double-click the icon to reopen it.",
                WinForms.ToolTipIcon.Info);
        }
    }

    private void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    private void SyncMenuState()
    {
        // assign Checked directly (not CheckOnClick semantics) without re-toggling
        _watchdogItem.CheckedChanged -= WatchdogItemChanged;
        _watchdogItem.Checked = _viewModel.WatchdogEnabled;
        _watchdogItem.CheckedChanged += WatchdogItemChanged;

        _startupItem.CheckedChanged -= StartupItemChanged;
        _startupItem.Checked = StartupManager.IsEnabled();
        _startupItem.CheckedChanged += StartupItemChanged;
    }

    // named handlers so SyncMenuState can detach/reattach them
    private void WatchdogItemChanged(object? sender, EventArgs e) =>
        _viewModel.WatchdogEnabled = _watchdogItem.Checked;

    private void StartupItemChanged(object? sender, EventArgs e) =>
        ToggleStartup(_startupItem.Checked);

    private void ToggleStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                StartupManager.Enable();
            }
            else
            {
                StartupManager.Disable();
            }
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(
                4000, "NoKill", $"Could not update startup setting: {ex.Message}",
                WinForms.ToolTipIcon.Warning);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.NotRespondingCount))
        {
            return;
        }

        int count = _viewModel.NotRespondingCount;
        _notifyIcon.Icon = count > 0 ? _alert.Icon : _calm.Icon;
        _notifyIcon.Text = count > 0
            ? $"NoKill — {count} app(s) not responding"
            : "NoKill — watching for freezes";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        AppIcons.DestroyIcon(_calm.Handle);
        AppIcons.DestroyIcon(_alert.Handle);
    }
}
