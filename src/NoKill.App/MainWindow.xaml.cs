using System.Windows;
using System.Windows.Threading;

namespace NoKill.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => _viewModel.RefreshCommand.Execute(null);

        Loaded += (_, _) =>
        {
            _viewModel.RefreshCommand.Execute(null);
            _viewModel.RefreshHistoryCommand.Execute(null);
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }
}
