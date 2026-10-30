using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace Nyvora.App.Services;

/// <summary>Per-user launch at sign-in (HKCU Run key) and a Start menu shortcut. Neither needs admin rights.</summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Nyvora";

    private static string? ExePath => Environment.ProcessPath;

    public static void SetLaunchAtSignIn(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (enabled && ExePath is { } exe)
            {
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Error("Could not update launch-at-sign-in", ex);
        }
    }

    public static string StartMenuShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nyvora.lnk");

    public static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nyvora.lnk");

    public static bool DesktopShortcutExists => File.Exists(DesktopShortcutPath);

    /// <summary>Creates or repoints the Start menu shortcut. Cheap and idempotent; runs in the background.</summary>
    public static void EnsureStartMenuShortcut() => _ = CreateShortcutAsync(StartMenuShortcutPath);

    /// <summary>Adds or removes the desktop shortcut; true when it ended up in the requested state.</summary>
    public static async Task<bool> SetDesktopShortcutAsync(bool present)
    {
        try
        {
            if (present)
            {
                await CreateShortcutAsync(DesktopShortcutPath);
            }
            else if (File.Exists(DesktopShortcutPath))
            {
                File.Delete(DesktopShortcutPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not change the desktop shortcut", ex);
        }

        return DesktopShortcutExists == present;
    }

    /// <summary>Opens Explorer with the Start menu shortcut selected (right-click it for "Pin to taskbar").</summary>
    public static void RevealStartMenuShortcut()
    {
        try
        {
            var link = StartMenuShortcutPath;
            var psi = File.Exists(link)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{link}\"")
                : new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(link)}\"");
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("Could not open the Start menu folder", ex);
        }
    }

    private static async Task CreateShortcutAsync(string link)
    {
        if (ExePath is not { } exe)
        {
            return;
        }

        var script =
            "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:NYVORA_LNK);" +
            "$s.TargetPath=$env:NYVORA_EXE;$s.WorkingDirectory=Split-Path $env:NYVORA_EXE;" +
            "$s.Description='Nyvora';$s.Save()";
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            psi.Environment["NYVORA_LNK"] = link;
            psi.Environment["NYVORA_EXE"] = exe;
            using var process = Process.Start(psi);
            if (process is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            Log.Error($"Could not create the shortcut {link}", ex);
        }
    }
}
