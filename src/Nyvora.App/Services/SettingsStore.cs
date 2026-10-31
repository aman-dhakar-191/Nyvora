using Nyvora.Core.Settings;
using Nyvora.Core.Storage;

namespace Nyvora.App.Services;

/// <summary>Single shared <see cref="AppSettings"/> instance so independent features cannot overwrite each other's fields.</summary>
internal sealed class SettingsStore
{
    internal static readonly string FilePath = System.IO.Path.Combine(DataFolders.Active(), "settings.json");

    public AppSettings Current { get; } = AppSettings.Load(FilePath);

    public void Save()
    {
        try
        {
            Current.Save(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
    }
}
