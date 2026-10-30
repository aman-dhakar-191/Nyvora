namespace Nyvora.Core.Storage;

/// <summary>
/// Where Nyvora keeps things. Your data (tasks.json, settings.json) lives in Roaming (%APPDATA%\Nyvora) so it follows
/// your profile; machine-specific, bulky or disposable files (log, downloaded updates, update backup) stay in Local.
/// The program files themselves are wherever the installer put them (normally %LOCALAPPDATA%\Programs\Nyvora).
/// </summary>
public static class DataFolders
{
    public static readonly string[] DataFiles = { "tasks.json", "settings.json" };

    public static string Roaming =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nyvora");

    public static string Local =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvora");

    /// <summary>
    /// Earlier versions kept data in Local. Copies each data file to Roaming when Roaming does not have it yet; the old
    /// copies are left in place as a backup. Never overwrites, and a file that cannot be copied is skipped.
    /// Returns the names that were copied.
    /// </summary>
    public static IReadOnlyList<string> MigrateFromLocal(string roaming, string local)
    {
        var copied = new List<string>();
        foreach (var name in DataFiles)
        {
            var from = Path.Combine(local, name);
            var to = Path.Combine(roaming, name);
            try
            {
                if (!File.Exists(from) || File.Exists(to))
                {
                    continue;
                }

                Directory.CreateDirectory(roaming);
                File.Copy(from, to, overwrite: false);
                copied.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave it; the app starts with defaults rather than failing.
            }
        }

        return copied;
    }

    public static IReadOnlyList<string> MigrateFromLocal() => MigrateFromLocal(Roaming, Local);

    /// <summary>
    /// The folder to actually use. Normally Roaming; but if a data file still exists only in Local (the copy to Roaming
    /// failed, e.g. Roaming is not writable), keep using Local so the user's tasks never appear to vanish.
    /// Call after <see cref="MigrateFromLocal()"/>.
    /// </summary>
    public static string Active(string roaming, string local)
    {
        foreach (var name in DataFiles)
        {
            if (File.Exists(Path.Combine(local, name)) && !File.Exists(Path.Combine(roaming, name)))
            {
                return local;
            }
        }

        return roaming;
    }

    public static string Active() => Active(Roaming, Local);
}
