using System.Diagnostics;
using System.Text.Json;
using Nyvora.Core.Storage;
using Nyvora.Core.Timesheet;

namespace Nyvora.App.Services;

/// <summary>
/// Runs the Keka fill script (keka\keka_fill.py, shipped beside the app) with Python and Edge. The script keeps its own
/// Edge profile so you sign in once. It reports back with an exit code (see <see cref="KekaOutcome"/>); it never submits.
/// </summary>
internal static class KekaService
{
    private static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "keka", "keka_fill.py");

    /// <summary>Profile, entry file and log live in Local (machine-specific, never roamed or committed).</summary>
    internal static string WorkDir => Path.Combine(DataFolders.Local, "keka");

    private static string ProfileDir => Path.Combine(WorkDir, "edge-profile");

    public static Task<KekaOutcome> FillAsync(CheckInEntry entry, string url, bool save, Action? onSetup = null)
    {
        try
        {
            Directory.CreateDirectory(WorkDir);
            var entryPath = Path.Combine(WorkDir, "entry.json");
            File.WriteAllText(entryPath, JsonSerializer.Serialize(entry));
            var args = Common(url);
            args.AddRange(new[] { "--entry", entryPath });
            if (save)
            {
                args.Add("--save");
            }

            return RunAsync(args, TimeSpan.FromMinutes(5), onSetup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not prepare the Keka entry", ex);
            return Task.FromResult(KekaOutcome.Failed);
        }
    }

    public static Task<KekaOutcome> SignInAsync(string url, Action? onSetup = null)
    {
        var args = Common(url);
        args.Add("--login");
        return RunAsync(args, TimeSpan.FromMinutes(11), onSetup);
    }

    private static List<string> Common(string url) =>
        new() { "--url", url.Trim(), "--profile", ProfileDir, "--log-dir", WorkDir };

    private static string VenvDir => Path.Combine(WorkDir, "venv");

    private static string VenvPython => Path.Combine(VenvDir, "Scripts", "python.exe");

    private static string ReadyMarker => Path.Combine(VenvDir, ".playwright-ready");

    /// <summary>
    /// Nyvora keeps its own Python environment for this (so nothing has to be installed globally): it is created once from
    /// whatever Python is on the PC and Playwright is installed into it. Null when ready, otherwise why not.
    /// </summary>
    private static async Task<KekaOutcome?> EnsureEnvironmentAsync(Action? onSetup)
    {
        if (File.Exists(VenvPython) && File.Exists(ReadyMarker))
        {
            return null;
        }

        onSetup?.Invoke();
        Directory.CreateDirectory(WorkDir);
        if (!File.Exists(VenvPython))
        {
            // The Python launcher first; plain "python" second (it may be the Microsoft Store stub, which exits with 9009).
            var made = false;
            foreach (var (exe, first) in new[] { ("py", "-3"), ("python", "") })
            {
                var args = first.Length > 0 ? new[] { first, "-m", "venv", VenvDir } : new[] { "-m", "venv", VenvDir };
                var code = await RunPlainAsync(exe, args, TimeSpan.FromMinutes(3));
                if (code == 0 && File.Exists(VenvPython))
                {
                    made = true;
                    break;
                }
            }

            if (!made)
            {
                return KekaOutcome.PythonMissing;
            }
        }

        var pip = await RunPlainAsync(VenvPython, new[] { "-m", "pip", "install", "--disable-pip-version-check", "playwright" }, TimeSpan.FromMinutes(6));
        if (pip != 0)
        {
            return KekaOutcome.PlaywrightMissing;
        }

        File.WriteAllText(ReadyMarker, DateTimeOffset.Now.ToString("o"));
        return null;
    }

    /// <summary>Runs a command to the end and logs what it printed. -1 when it could not start or timed out.</summary>
    private static async Task<int> RunPlainAsync(string exe, string[] args, TimeSpan limit)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return -1;
            }

            var output = proc.StandardOutput.ReadToEndAsync();
            var errors = proc.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(limit);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already gone
                }

                Log.Error($"{exe} {string.Join(' ', args)} timed out");
                return -1;
            }

            var text = ((await output) + "\n" + (await errors)).Trim();
            var tail = string.Join(" | ", text.Split('\n').TakeLast(6).Select(l => l.Trim()).Where(l => l.Length > 0));
            Log.Info($"keka setup: {exe} {string.Join(' ', args)} -> {proc.ExitCode} {tail}");
            return proc.ExitCode is 9009 or 103 ? -1 : proc.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1; // not installed
        }
    }

    private static async Task<KekaOutcome> RunAsync(List<string> args, TimeSpan limit, Action? onSetup)
    {
        if (!File.Exists(ScriptPath))
        {
            Log.Error($"Keka script missing: {ScriptPath}");
            return KekaOutcome.Failed;
        }

        if (await EnsureEnvironmentAsync(onSetup) is { } problem)
        {
            return problem;
        }

        var psi = new ProcessStartInfo(VenvPython)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(ScriptPath);
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            var proc = Process.Start(psi);
            return proc is null ? KekaOutcome.Failed : await WaitAsync(proc, limit);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Could not start the Keka script", ex);
            return KekaOutcome.Failed;
        }
    }

    private static async Task<KekaOutcome> WaitAsync(Process proc, TimeSpan limit)
    {
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            Log.Info("keka: " + e.Data);
            if (e.Data.StartsWith("RESULT ", StringComparison.Ordinal) && int.TryParse(e.Data.AsSpan(7), out var code))
            {
                done.TrySetResult(code); // reported as soon as the script says so; it may stay open for you to review
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Log.Info("keka (error): " + e.Data);
            }
        };
        proc.EnableRaisingEvents = true;
        proc.Exited += (_, _) => done.TrySetResult(SafeExitCode(proc));
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (proc.HasExited)
        {
            done.TrySetResult(SafeExitCode(proc));
        }

        if (await Task.WhenAny(done.Task, Task.Delay(limit)) != done.Task)
        {
            Log.Error("Keka script timed out; stopping it");
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // already gone
            }

            return KekaOutcome.Failed;
        }

        var code = done.Task.Result;
        return KekaResult.FromExitCode(code);
    }

    private static int SafeExitCode(Process p)
    {
        try
        {
            return p.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return (int)KekaOutcome.Failed;
        }
    }
}
