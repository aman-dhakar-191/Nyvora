using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Nyvora.App.Services;

namespace Nyvora.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\Nyvora.SingleInstance";
    private const string ShowSignalName = @"Local\Nyvora.ShowPill";

    // Held for the life of the process so a second launch can tell this one is running.
    private static Mutex? _instance;
    private static EventWaitHandle? _showSignal;

    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Error("Unhandled exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            // Another copy owns the pill (a shortcut, sign-in start and a manual launch can all happen):
            // ask it to open, then leave, so there is never a second pill or a second writer of tasks.json.
            Log.Info("Already running; asking the running copy to open");
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
            {
                Log.Error("Could not signal the running copy", ex);
            }

            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Show();
        ListenForSecondLaunch(DispatcherQueue.GetForCurrentThread());
    }

    private void ListenForSecondLaunch(DispatcherQueue queue)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showSignal = signal;
        var thread = new Thread(() =>
        {
            while (signal.WaitOne())
            {
                queue.TryEnqueue(() => _window?.ShowFromSecondLaunch());
            }
        })
        {
            IsBackground = true,
            Name = "Nyvora second-launch listener",
        };
        thread.Start();
    }
}
