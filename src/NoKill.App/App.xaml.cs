using System.Windows;

namespace NoKill.App;

/// <summary>
/// Startup arguments:
///   --minimized          start hidden in the tray with the watchdog armed (login mode)
///   --install-startup    register run-at-login and exit (scriptable)
///   --uninstall-startup  remove run-at-login registration and exit
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>True only when the user chose Exit from the tray; lets MainWindow tell hide from quit.</summary>
    public static bool IsShuttingDown { get; private set; }

    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--install-startup"))
        {
            StartupManager.Enable();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--uninstall-startup"))
        {
            StartupManager.Disable();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;

        _tray = new TrayIconService(window, window.ViewModel);
        _tray.ExitRequested += () =>
        {
            IsShuttingDown = true;
            _tray.Dispose();
            window.Close();
            Shutdown();
        };
        window.HiddenToTray += () => _tray.NotifyHiddenToTray();

        if (e.Args.Contains("--minimized"))
        {
            // Login/guardian mode: no window, watchdog armed from the first scan.
            window.ViewModel.WatchdogEnabled = true;
        }
        else
        {
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
