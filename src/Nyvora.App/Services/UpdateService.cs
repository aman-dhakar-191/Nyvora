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

    // Runs in a hidden Windows PowerShell after Nyvora exits. Parameters arrive via environment variables so no
    // quoting of paths is needed and no script file (execution policy) is involved. Robocopy exit codes >= 8 are failures.
    private const string HelperScript = @"
$log = Join-Path (Split-Path (Split-Path $env:NYVORA_STAGE)) 'update.log'
try {
  Wait-Process -Id ([int]$env:NYVORA_PID) -Timeout 30 -ErrorAction SilentlyContinue
  robocopy $env:NYVORA_STAGE $env:NYVORA_DEST /E /IS /IT /R:10 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Null
  if ($LASTEXITCODE -ge 8) { Add-Content $log ('copy failed, robocopy exit ' + $LASTEXITCODE) } else { Add-Content $log ('updated to ' + $env:NYVORA_TARGET) }
} catch { Add-Content $log ($_ | Out-String) }
Start-Process -FilePath $env:NYVORA_EXE";

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
        try
        {
            Set(UpdateState.Downloading, "Downloading…");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            Directory.CreateDirectory(root);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var expected = Checksum.ParseExpected(await Http.GetStringAsync(update.ChecksumUrl, cts.Token))
                ?? throw new InvalidDataException("Checksum file is malformed.");
            await DownloadAsync(update.PackageUrl, zip, cts.Token);

            Set(UpdateState.Installing, "Verifying…");
            await using (var fs = File.OpenRead(zip))
            {
                var actual = await Checksum.Sha256HexAsync(fs, cts.Token);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Package checksum does not match.");
                }
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(zip, stage), cts.Token);
            if (!File.Exists(Path.Combine(stage, "Nyvora.exe")))
            {
                throw new InvalidDataException("Package does not contain Nyvora.exe.");
            }

            Set(UpdateState.Installing, "Restarting…");
            Launch(stage, update);
            Log.Info($"Installing {update.Tag}; exiting for restart");
            RestartRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or InvalidDataException or UnauthorizedAccessException or Win32Exception)
        {
            Log.Error("Update install failed", ex);
            TryDelete(root);
            Set(UpdateState.Available, "Update failed. Try again");
        }
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
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(HelperScript);
        psi.Environment["NYVORA_PID"] = Environment.ProcessId.ToString();
        psi.Environment["NYVORA_STAGE"] = stage;
        psi.Environment["NYVORA_DEST"] = AppContext.BaseDirectory.TrimEnd('\\', '/');
        psi.Environment["NYVORA_EXE"] = exe;
        psi.Environment["NYVORA_TARGET"] = update.Tag;
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
