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

    public static Task<KekaOutcome> FillAsync(CheckInEntry entry, string url, bool save)
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

            return RunAsync(args, TimeSpan.FromMinutes(5));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not prepare the Keka entry", ex);
            return Task.FromResult(KekaOutcome.Failed);
        }
    }

    public static Task<KekaOutcome> SignInAsync(string url)
    {
        var args = Common(url);
        args.Add("--login");
        return RunAsync(args, TimeSpan.FromMinutes(11));
    }

    private static List<string> Common(string url) =>
        new() { "--url", url.Trim(), "--profile", ProfileDir, "--log-dir", WorkDir };

    private static async Task<KekaOutcome> RunAsync(List<string> args, TimeSpan limit)
    {
        if (!File.Exists(ScriptPath))
        {
            Log.Error($"Keka script missing: {ScriptPath}");
            return KekaOutcome.Failed;
        }

        // The Python launcher first; plain "python" second (it may be the Microsoft Store stub, which exits with 9009).
        foreach (var (exe, first) in new[] { ("py", "-3"), ("python", "") })
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (first.Length > 0)
            {
                psi.ArgumentList.Add(first);
            }

            psi.ArgumentList.Add(ScriptPath);
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                continue; // that launcher is not installed
            }

            if (proc is null)
            {
                continue;
            }

            var outcome = await WaitAsync(proc, limit);
            if (outcome == KekaOutcome.PythonMissing)
            {
                continue;
            }

            return outcome;
        }

        return KekaOutcome.PythonMissing;
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
        return code is 9009 or 103 ? KekaOutcome.PythonMissing : KekaResult.FromExitCode(code);
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
