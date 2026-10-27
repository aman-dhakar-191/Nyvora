using Microsoft.UI.Xaml;
using Nyvora.App.Services;

namespace Nyvora.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log.Error("Unhandled exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Show();
    }
}
