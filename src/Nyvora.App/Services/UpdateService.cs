using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using Nyvora.Core.Updates;

namespace Nyvora.App.Services;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// Checks GitHub Releases for a newer build and installs it on request. Raises events on the UI thread
/// (async continuations resume on it). Nothing is ever installed without an explicit <see cref="InstallAsync"/> call.
/// </summary>
internal sealed class UpdateService
{
    private const long MaxPackageBytes = 400L * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static readonly string NyvoraDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvora");

    private static readonly string UpdatesDir = Path.Combine(NyvoraDir, "updates");

    private readonly SettingsStore _store;

    public UpdateService(SettingsStore store) => _store = store;

    public Version Current { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public string CurrentText => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public string Message { get; private set; } = "";

    public UpdateInfo? Available { get; private set; }

    public event Action? Changed;

    /// <summary>Raised right after the installer helper is launched; the host must exit so files can be replaced.</summary>
    public event Action? RestartRequested;

    /// <summary>Not offered when running from a build output folder (dotnet run / Visual Studio), where it would overwrite build output.</summary>
    public bool CanInstall { get; } = !IsBuildOutput(AppContext.BaseDirectory);

    public static void CleanupOldUpdates()
    {
        try
        {
            if (Directory.Exists(UpdatesDir))
            {
                foreach (var dir in Directory.GetDirectories(UpdatesDir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not clean old update files", ex);
        }
    }

    public async Task AutoCheckAsync()
    {
        if (_store.Current.IsUpdateCheckDue(DateTimeOffset.Now))
        {
            await CheckAsync();
        }
    }

    public async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing)
        {
            return;
        }

        Set(UpdateState.Checking, "Checking…");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var json = await Http.GetStringAsync(ReleaseFeed.ReleasesApiUrl, cts.Token);
            var info = ReleaseFeed.FindUpdate(json, Current);

            _store.Current.LastUpdateCheck = DateTimeOffset.Now;
            _store.Save();

            Available = info;
            if (info is null)
            {
                Set(UpdateState.UpToDate, "Up to date");
            }
            else
            {
                Log.Info($"Update available: {info.Tag}");
                Set(UpdateState.Available, $"{info.Tag} available");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("Update check failed", ex);
            Set(UpdateState.Failed, "Couldn't check for updates");
        }
    }

    public async Task InstallAsync()
    {
        if (State != UpdateState.Available || Available is not { } update || !CanInstall)
        {
            return;
        }

        var root = Path.Combine(UpdatesDir, update.Tag);
        var zip = Path.Combine(root, "package.zip");
        var stage = Path.Combine(root, "stage");
        var step = "starting";
        try
        {
            step = "preparing";
            Set(UpdateState.Downloading, "Downloading…");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            step = "downloading the checksum";
            Log.Info($"Update {update.Tag}: {step} from {update.ChecksumUrl}");
            var expected = Checksum.ParseExpected(await Http.GetStringAsync(update.ChecksumUrl, cts.Token))
                ?? throw new InvalidDataException("Checksum file is malformed.");
            step = "downloading the package";
            Log.Info($"Update {update.Tag}: {step} from {update.PackageUrl}");
            await DownloadAsync(update.PackageUrl, zip, cts.Token);

            step = "verifying the download";
            Log.Info($"Update {update.Tag}: downloaded {new FileInfo(zip).Length} bytes; {step}");
            Set(UpdateState.Installing, "Verifying…");
            await using (var fs = File.OpenRead(zip))
            {
                var actual = await Checksum.Sha256HexAsync(fs, cts.Token);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Package checksum does not match.");
                }
            }

            step = "unpacking";
            Log.Info($"Update {update.Tag}: {step}");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, stage), cts.Token);
            if (!File.Exists(Path.Combine(stage, "Nyvora.exe")))
            {
                throw new InvalidDataException("Package does not contain Nyvora.exe.");
            }

            step = "starting the installer";
            Set(UpdateState.Installing, "Restarting…");
            Launch(stage, update);
            Log.Info($"Installing {update.Tag}; exiting for restart");
            RestartRequested?.Invoke();
        }
        catch (Exception ex)
        {
            // Any failure, expected or not, must leave the app running and say what went wrong.
            Log.Error($"Update install failed while {step}", ex);
            TryDelete(root);
            Set(UpdateState.Available, $"Update failed while {step}: {Explain(ex)}");
        }
    }

    /// <summary>One short line for the Settings panel; the full exception is in the log.</summary>
    private static string Explain(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null)
        {
            inner = inner.InnerException;
        }

        var text = $"{inner.GetType().Name}: {inner.Message}".Replace('\r', ' ').Replace('\n', ' ');
        return text.Length > 140 ? text[..140] + "…" : text;
    }

    private static string LoadHelperScript()
    {
        // UpdateHelper.ps1 is embedded so the shipped script is exactly the one the CI tests run.
        var assembly = typeof(UpdateService).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("UpdateHelper.ps1", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The update helper script is missing from this build.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Launch(string stage, UpdateInfo update)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-EncodedCommand"); // base64 UTF-16: no quoting problems, and no script file for execution policy to block
        psi.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(LoadHelperScript())));
        psi.Environment["NYVORA_PID"] = Environment.ProcessId.ToString();
        psi.Environment["NYVORA_STAGE"] = stage;
        psi.Environment["NYVORA_DEST"] = AppContext.BaseDirectory.TrimEnd('\\', '/');
        psi.Environment["NYVORA_EXE"] = exe;
        psi.Environment["NYVORA_TARGET"] = update.Tag;
        // Outside updates\<tag>: the new version clears that folder at startup, and the backup must outlive that.
        psi.Environment["NYVORA_BACKUP"] = Path.Combine(NyvoraDir, "rollback");
        psi.Environment["NYVORA_LOG"] = Path.Combine(UpdatesDir, "update.log");
        Process.Start(psi)?.Dispose();
    }

    private static async Task DownloadAsync(Uri url, string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPackageBytes)
        {
            throw new InvalidDataException("Package is larger than expected.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxPackageBytes)
            {
                throw new InvalidDataException("Package is larger than expected.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private void Set(UpdateState state, string message)
    {
        State = state;
        Message = message;
        Changed?.Invoke();
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not remove update files", ex);
        }
    }

    private static bool IsBuildOutput(string dir) =>
        dir.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
        dir.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) ||
        dir.Contains(@"\bin\x64\", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // per-call CancellationTokenSource instead
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nyvora-updater", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
