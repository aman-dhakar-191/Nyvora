using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nyvora.App.Services;
using Nyvora.Core.Tasks;

namespace Nyvora.App.UI;

/// <summary>Everything configurable: window behavior, updates, reminders and task statuses.</summary>
public sealed partial class SettingsView : UserControl
{
    private UpdateService? _updates;

    public SettingsView() => InitializeComponent();

    public event Action? CloseRequested;

    public event Action<bool>? AlwaysOnTopChanged;

    public event Action<bool>? StatusReminderChanged;

    public event Action<bool>? LaunchAtSignInChanged;

    internal void Attach(StatusSet statuses, TagSet tags, UpdateService updates, bool alwaysOnTop, bool statusReminder, bool launchAtSignIn)
    {
        TagsEditor.Attach(tags);
        McpConfig.Text = McpConfigText();
        RefreshShortcutButton();
        TopToggle.IsChecked = alwaysOnTop;
        ReminderToggle.IsChecked = statusReminder;
        StartupToggle.IsChecked = launchAtSignIn;
        Statuses.Attach(statuses);

        if (_updates is not null)
        {
            _updates.Changed -= RefreshUpdate;
        }

        _updates = updates;
        _updates.Changed += RefreshUpdate;
        RefreshUpdate();
    }

    private static string McpConfigText()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        var exe = Path.Combine(dir, "nyvora-mcp.exe").Replace("\\", "\\\\");
        return "{\n  \"mcpServers\": {\n    \"nyvora\": {\n      \"command\": \"" + exe + "\",\n      \"args\": []\n    }\n  }\n}";
    }

    private void OnCopyMcp(object sender, RoutedEventArgs e)
    {
        var ok = ClipboardText.TrySet(McpConfig.Text);
        McpCopy.Content = ok ? "Copied" : "Could not copy; select the text instead";
    }

    private void RefreshShortcutButton() =>
        DesktopShortcutButton.Content = StartupService.DesktopShortcutExists ? "Remove desktop shortcut" : "Add desktop shortcut";

    private async void OnDesktopShortcut(object sender, RoutedEventArgs e)
    {
        try
        {
            DesktopShortcutButton.IsEnabled = false;
            var want = !StartupService.DesktopShortcutExists;
            var ok = await StartupService.SetDesktopShortcutAsync(want);
            RefreshShortcutButton();
            if (!ok)
            {
                DesktopShortcutButton.Content = "Could not change it; see the log";
            }
        }
        catch (Exception ex)
        {
            Log.Error("Desktop shortcut action failed", ex);
        }
        finally
        {
            DesktopShortcutButton.IsEnabled = true;
        }
    }

    private void OnRevealStartMenu(object sender, RoutedEventArgs e) => StartupService.RevealStartMenuShortcut();

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = Log.FilePath;
            var psi = File.Exists(file)
                ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"")
                : new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(file)}\"");
            System.Diagnostics.Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Could not open the log folder", ex);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnTopClick(object sender, RoutedEventArgs e) => AlwaysOnTopChanged?.Invoke(TopToggle.IsChecked == true);

    private void OnStartupClick(object sender, RoutedEventArgs e) =>
        LaunchAtSignInChanged?.Invoke(StartupToggle.IsChecked == true);

    private void OnReminderClick(object sender, RoutedEventArgs e) =>
        StatusReminderChanged?.Invoke(ReminderToggle.IsChecked == true);

    private void RefreshUpdate()
    {
        if (_updates is not { } u)
        {
            return;
        }

        UpdateText.Text = u.State == UpdateState.Idle ? u.CurrentText : $"{u.CurrentText} · {u.Message}";
        var busy = u.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
        var installable = u.State == UpdateState.Available && u.Available is not null;

        UpdateButton.IsEnabled = !busy && (!installable || u.CanInstall);
        UpdateButton.Content = installable
            ? (u.CanInstall ? "Install and restart" : "Update available")
            : "Check for updates";
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updates is not { } u)
        {
            return;
        }

        try
        {
            if (u.State == UpdateState.Available)
            {
                await u.InstallAsync();
            }
            else
            {
                await u.CheckAsync();
            }
        }
        catch (Exception ex)
        {
            // Last resort so an unexpected failure cannot crash the app from an async void handler.
            Log.Error("Update action failed", ex);
        }
    }
}
