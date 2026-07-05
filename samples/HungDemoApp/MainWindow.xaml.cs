using System.Windows;
using System.Windows.Threading;

namespace HungDemoApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Supports automated testing:
    ///   --auto-freeze [delayMs] [durationMs]  freeze the UI thread (defaults 1000, 60000)
    ///   --auto-hidden-modal                   open the behind-the-owner modal on startup
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();

        if (args.Contains("--auto-hidden-modal"))
        {
            Dispatcher.BeginInvoke(
                () => HiddenModal_Click(this, new RoutedEventArgs()),
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        if (args.Contains("--auto-deadlock"))
        {
            Dispatcher.BeginInvoke(
                () => Deadlock_Click(this, new RoutedEventArgs()),
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        int index = Array.IndexOf(args, "--auto-freeze");
        if (index < 0)
        {
            return;
        }

        int delayMs = index + 1 < args.Length && int.TryParse(args[index + 1], out int d) ? d : 1000;
        int durationMs = index + 2 < args.Length && int.TryParse(args[index + 2], out int f) ? f : 60_000;

        StatusText.Text = $"Status: auto-freezing in {delayMs} ms for {durationMs} ms";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Thread.Sleep(durationMs); // blocks the dispatcher: the freeze under test
        };
        timer.Start();
    }

    private void FreezeThirtySeconds_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Status: UI thread frozen for 30 s";
        // Let the status text render first, then block the dispatcher.
        Dispatcher.BeginInvoke(
            () => Thread.Sleep(30_000),
            DispatcherPriority.Background);
    }

    private void Deadlock_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Status: deadlocking on two kernel mutexes (kill me via Task Manager when done)";

        // Raw kernel mutexes + raw waits (see NativeMethods): the kernel
        // tracks mutant ownership, so Wait Chain Traversal sees the full
        // cycle, and the raw wait genuinely blocks the UI thread (WPF's
        // dispatcher can't pump through a native WaitForSingleObject).
        nint mutexA = NativeMethods.CreateMutexW(0, false, null);
        nint mutexB = NativeMethods.CreateMutexW(0, false, null);

        // Two-way sequencing: each thread must HOLD its first mutex before the
        // other proceeds to the blocking wait, or one thread can win both
        // mutexes and no deadlock forms.
        var uiHoldsA = new ManualResetEventSlim();
        var workerHoldsB = new ManualResetEventSlim();

        var worker = new Thread(() =>
        {
            NativeMethods.WaitForSingleObject(mutexB, NativeMethods.Infinite);
            workerHoldsB.Set();
            uiHoldsA.Wait();
            NativeMethods.WaitForSingleObject(mutexA, NativeMethods.Infinite); // holds B, wants A
        })
        { IsBackground = true };
        worker.Start();

        Dispatcher.BeginInvoke(() =>
        {
            NativeMethods.WaitForSingleObject(mutexA, NativeMethods.Infinite);
            uiHoldsA.Set();
            workerHoldsB.Wait();
            NativeMethods.WaitForSingleObject(mutexB, NativeMethods.Infinite); // holds A, wants B — cycle
        }, DispatcherPriority.Background);
    }

    private void HiddenModal_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Status: modal dialog opened BEHIND this window";
        var dialog = new Window
        {
            Title = "Hidden blocker",
            Width = 300,
            Height = 140,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Left + 40,
            Top = Top + 40,
            ShowActivated = false,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "I am the modal dialog blocking the main window.",
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
            },
        };

        // Push the blocker below its owner AFTER it is shown (Loaded), because
        // WPF's show pass would undo any earlier z-order placement. Simulates
        // the classic "app looks frozen but a dialog is hiding behind it" case.
        dialog.Loaded += (_, _) =>
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
            NativeMethods.PlaceBehindOwner(helper.Handle, new System.Windows.Interop.WindowInteropHelper(this).Handle);
        };

        dialog.ShowDialog();
        StatusText.Text = "Status: idle";
    }
}
