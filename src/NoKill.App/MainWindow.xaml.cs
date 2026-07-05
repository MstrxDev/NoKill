using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace NoKill.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>Raised when the window hides to the tray instead of closing.</summary>
    public event Action? HiddenToTray;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => _viewModel.RefreshCommand.Execute(null);

        // Scanning starts immediately, NOT on Loaded: in tray/guardian mode
        // (--minimized) this window is never shown, but the watchdog must run.
        _viewModel.RefreshCommand.Execute(null);
        _viewModel.RefreshHistoryCommand.Execute(null);
        _refreshTimer.Start();

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                HiddenToTray?.Invoke();
            }
        };

        Closed += (_, _) => _refreshTimer.Stop();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // X hides to the tray; only the tray's Exit truly quits.
        if (!App.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }

        base.OnClosing(e);
    }
}
