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
    /// Supports automated testing: "HungDemoApp.exe --auto-freeze [delayMs] [durationMs]"
    /// freezes the UI thread without any clicking (defaults: 1000 ms delay, 60 s freeze).
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        string[] args = Environment.GetCommandLineArgs();
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
        StatusText.Text = "Status: deadlocking (kill me via Task Manager when done)";
        var lockA = new object();
        var lockB = new object();
        var firstLockHeld = new ManualResetEventSlim();

        var worker = new Thread(() =>
        {
            lock (lockB)
            {
                firstLockHeld.Set();
                Thread.Sleep(200);
                lock (lockA) { }
            }
        })
        { IsBackground = true };
        worker.Start();

        Dispatcher.BeginInvoke(() =>
        {
            lock (lockA)
            {
                firstLockHeld.Wait();
                Thread.Sleep(200);
                lock (lockB) { } // UI thread wants B, worker holds B and wants A
            }
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

        // Push the blocker below its owner before it blocks: simulates the
        // classic "app looks frozen but a dialog is hiding behind it" case.
        dialog.SourceInitialized += (_, _) =>
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
            NativeMethods.PlaceBehindOwner(helper.Handle, new System.Windows.Interop.WindowInteropHelper(this).Handle);
        };

        dialog.ShowDialog();
        StatusText.Text = "Status: idle";
    }
}
