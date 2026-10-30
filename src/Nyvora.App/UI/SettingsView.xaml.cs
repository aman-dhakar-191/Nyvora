using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nyvora.App.Services;
using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Windows.System;

namespace Nyvora.App.UI;

/// <summary>Everything configurable: window behavior, updates, reminders and task statuses.</summary>
public sealed partial class SettingsView : UserControl
{
    private UpdateService? _updates;

    public SettingsView() => InitializeComponent();

    public event Action? CloseRequested;

    public event Action<bool>? AlwaysOnTopChanged;

    public event Action<bool>? CompactChanged;

    public event Action<bool>? HotkeyEnabledChanged;

    /// <summary>The user pressed a new shortcut; the owner tries to register it and calls <see cref="SetHotkey"/> with the outcome.</summary>
    public event Action<HotkeySpec>? HotkeyKeysChanged;

    public event Action<bool>? StatusReminderChanged;

    private HotkeySpec _hotkey = HotkeySpec.Default;
    private bool _capturing;

    internal void SetLayoutOptions(bool compact, bool hotkeyOn, HotkeySpec hotkey)
    {
        CompactToggle.IsChecked = compact;
        HotkeyToggle.IsChecked = hotkeyOn;
        SetHotkey(hotkey, null);
    }

    /// <summary>Shows the shortcut in use; <paramref name="note"/> explains a problem (for example, it is taken).</summary>
    internal void SetHotkey(HotkeySpec hotkey, string? note)
    {
        _hotkey = hotkey;
        _capturing = false;
        HotkeyButton.Content = hotkey + "  (click to change)";
        HotkeyNote.Text = note ?? "";
        HotkeyNote.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        HotkeyButton.Content = "Press the new shortcut… (Esc cancels)";
        HotkeyNote.Text = "Use Ctrl, Alt or Win with a letter, digit or F-key.";
        HotkeyNote.Visibility = Visibility.Visible;
        HotkeyButton.Focus(FocusState.Programmatic);
    }

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturing)
        {
            SetHotkey(_hotkey, null); // clicked away: keep what we had
        }
    }

    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key;
        if (key == VirtualKey.Escape)
        {
            SetHotkey(_hotkey, null);
            return;
        }

        if (key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            return; // modifiers alone: wait for the real key
        }

        var mods = HotkeyModifiers.None;
        if (Down(VirtualKey.Control))
        {
            mods |= HotkeyModifiers.Control;
        }

        if (Down(VirtualKey.Menu))
        {
            mods |= HotkeyModifiers.Alt;
        }

        if (Down(VirtualKey.Shift))
        {
            mods |= HotkeyModifiers.Shift;
        }

        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
        {
            mods |= HotkeyModifiers.Win;
        }

        var spec = new HotkeySpec(mods, (int)key);
        if (!spec.IsValid)
        {
            HotkeyNote.Text = $"{spec} won't work: use Ctrl, Alt or Win with a letter, digit or F-key.";
            return;
        }

        HotkeyKeysChanged?.Invoke(spec);
    }

    private static bool Down(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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

    private string McpConfigText()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        var exe = Path.Combine(dir, "nyvora-mcp.exe").Replace("\\", "\\\\");
        var args = new List<string>();
        if (McpWrite.IsChecked == true)
        {
            args.Add("\"--allow-write\"");
        }

        if (McpDelete.IsChecked == true)
        {
            args.Add("\"--allow-delete\"");
        }

        return "{\n  \"mcpServers\": {\n    \"nyvora\": {\n      \"command\": \"" + exe + "\",\n      \"args\": [" + string.Join(", ", args) + "]\n    }\n  }\n}";
    }

    private void OnMcpOptionClick(object sender, RoutedEventArgs e)
    {
        // Deleting only makes sense (and only works) together with write access.
        McpDelete.IsEnabled = McpWrite.IsChecked == true;
        if (McpWrite.IsChecked != true)
        {
            McpDelete.IsChecked = false;
        }

        McpConfig.Text = McpConfigText();
        McpCopy.Content = "Copy config";
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

    private void OnNav(object sender, RoutedEventArgs e)
    {
        FrameworkElement target = ((sender as FrameworkElement)?.Tag as string) switch
        {
            "general" => SecGeneral,
            "tasks" => SecTasks,
            "agents" => SecAgents,
            _ => SecUpdates,
        };
        target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.0 });
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnTopClick(object sender, RoutedEventArgs e) => AlwaysOnTopChanged?.Invoke(TopToggle.IsChecked == true);

    private void OnCompactClick(object sender, RoutedEventArgs e) => CompactChanged?.Invoke(CompactToggle.IsChecked == true);

    private void OnHotkeyClick(object sender, RoutedEventArgs e) => HotkeyEnabledChanged?.Invoke(HotkeyToggle.IsChecked == true);

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

        // A real bar while downloading (we know the size); a moving one for the steps after it.
        UpdateProgress.Visibility = u.State is UpdateState.Downloading or UpdateState.Installing ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = u.Progress is null;
        if (u.Progress is { } p)
        {
            UpdateProgress.Value = p * 100;
        }

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
