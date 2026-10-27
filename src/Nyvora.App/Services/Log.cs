using System.Diagnostics;

namespace Nyvora.App.Services;

/// <summary>Minimal local-only log (%LOCALAPPDATA%\Nyvora\nyvora.log). Records events and exceptions, never user content.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvora", "nyvora.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:O} {level} {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch (IOException) { /* logging must never take the app down */ }
        catch (UnauthorizedAccessException) { }
    }
}
