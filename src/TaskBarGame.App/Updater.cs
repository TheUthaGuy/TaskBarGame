using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskBarGame.Core;

namespace TaskBarGame.App;

public enum UpdateState
{
    /// <summary>Hasn't checked yet.</summary>
    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version is out (see <see cref="Updater.Release"/>).</summary>
    Available,
    Downloading,

    /// <summary>The new version replaced the game file; it runs from the next start.</summary>
    Installed,
    Failed,
}

/// <summary>
/// Finds, downloads and installs new versions of the Harvest Edition from GitHub Releases.
///
/// Installing: the new exe is downloaded next to the game as "TaskBarGame.exe.download" and its
/// SHA-256 is checked against the one GitHub publishes. Then the running exe is renamed to
/// "TaskBarGame.exe.old" (Windows allows renaming a running program, just not overwriting it), the
/// new one takes its name, and the game restarts. The .old file is deleted on the next start.
/// </summary>
public sealed class Updater
{
    /// <summary>
    /// Set this environment variable to a web address serving a GitHub-style list of releases to
    /// test updates without GitHub. Downloads from it are allowed from anywhere (still checksum-checked).
    /// </summary>
    public const string TestFeedVariable = "TBG_UPDATE_FEED";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>The version of the game that's running, e.g. 0.2.0.</summary>
    public static Version CurrentVersion { get; } =
        Updates.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // After CurrentVersion: static fields are set up in the order they're written, and this uses it.
    private static readonly HttpClient Http = CreateClient();

    private readonly GameSettings _settings;
    private readonly string? _testFeed = Environment.GetEnvironmentVariable(TestFeedVariable) is { Length: > 0 } feed ? feed : null;
    private DispatcherTimer? _timer;
    private Version? _announced;

    public Updater(GameSettings settings) => _settings = settings;

    public UpdateState State { get; private set; }

    /// <summary>The newer release, once one has been found.</summary>
    public ReleaseInfo? Release { get; private set; }

    /// <summary>0..1 while downloading.</summary>
    public double Progress { get; private set; }

    /// <summary>What went wrong, when <see cref="State"/> is Failed.</summary>
    public string Error { get; private set; } = "";

    public DateTime? LastChecked { get; private set; }

    /// <summary>
    /// True when this copy of the game is a single exe (as released) that can be swapped for a new one.
    /// A copy built as a folder of files has to be updated by hand.
    /// </summary>
    public static bool CanInstall =>
        Environment.ProcessPath is { } exe
        && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, "TaskBarGame.dll"));

    /// <summary>Something changed (state, progress): update anything showing it.</summary>
    public event Action? Changed;

    /// <summary>A newer version was found (raised once per version).</summary>
    public event Action<ReleaseInfo>? Found;

    /// <summary>The update was installed in the background (automatic mode).</summary>
    public event Action<ReleaseInfo>? InstalledInBackground;

    /// <summary>Installing failed after you asked for it (argument = what went wrong).</summary>
    public event Action<string>? InstallFailed;

    /// <summary>Starts checking now and then (first check shortly after the game starts).</summary>
    public void Start()
    {
        CleanUpOldVersion();
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (_settings.CheckForUpdates) await CheckAsync(manual: false);
        };
        _timer.Start();
    }

    /// <summary>Looks for a newer version. <paramref name="manual"/> = you pressed "Check now".</summary>
    public async Task CheckAsync(bool manual)
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installed) return;
        SetState(UpdateState.Checking);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, _testFeed ?? Updates.ReleasesApiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await Http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token);

            LastChecked = DateTime.Now;
            var newest = Updates.Newest(json);
            if (newest is null || !Updates.IsNewer(newest, CurrentVersion))
            {
                Release = null;
                SetState(UpdateState.UpToDate);
                return;
            }

            Release = newest;
            SetState(UpdateState.Available);
            if (_announced != newest.Version)
            {
                _announced = newest.Version;
                if (_settings.InstallUpdatesAutomatically && CanInstall)
                {
                    if (await InstallAsync(announceFailure: false)) InstalledInBackground?.Invoke(newest);
                }
                else
                {
                    Found?.Invoke(newest);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Fail(manual ? $"Couldn't reach GitHub to check for updates ({ex.Message})." : "Couldn't check for updates (no connection?).");
        }
    }

    /// <summary>
    /// Downloads the new version, checks it, and swaps it in for the running exe. Returns true when
    /// it's installed: restart the game to run it.
    /// </summary>
    public async Task<bool> InstallAsync(bool announceFailure = true)
    {
        if (State == UpdateState.Installed) return true;
        if (Release is not { } release || State is UpdateState.Downloading or UpdateState.Checking) return false;

        string? exe = Environment.ProcessPath;
        if (!CanInstall || exe is null)
            return Fail("This copy of the game can't update itself. Download the new version from the release page.", announceFailure);
        if (release.ExeUrl is null)
            return Fail("That release doesn't have the game file yet. Try again in a few minutes.", announceFailure);
        if (_testFeed is null && !Updates.IsTrustedDownload(release.ExeUrl))
            return Fail("The download wasn't on GitHub, so it wasn't installed.", announceFailure);

        string download = exe + ".download";
        string old = exe + ".old";
        try
        {
            string? expected = release.Sha256;
            if (expected is null && release.ChecksumUrl is { } checksumUrl && (_testFeed is not null || Updates.IsTrustedDownload(checksumUrl)))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                expected = Updates.ParseSha256(await Http.GetStringAsync(checksumUrl, timeout.Token));
            }
            if (expected is null)
                return Fail("The new version's checksum is missing, so it can't be checked and wasn't installed.", announceFailure);

            Progress = 0;
            SetState(UpdateState.Downloading);
            string actual = await DownloadAsync(release, download);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(download);
                return Fail("The download didn't match its checksum, so it wasn't installed. Try again later.", announceFailure);
            }

            // Swap the files: rename the running exe out of the way, then put the new one in its place.
            TryDelete(old);
            File.Move(exe, old);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                File.Move(old, exe); // put the old one back so the game still starts
                throw;
            }

            SetState(UpdateState.Installed);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            TryDelete(download);
            return Fail($"Couldn't install the update ({ex.Message}). You can download it from the release page instead.", announceFailure);
        }
    }

    /// <summary>Deletes what's left of the previous version after an update.</summary>
    public static void CleanUpOldVersion()
    {
        if (Environment.ProcessPath is not { } exe) return;
        _ = Task.Run(async () =>
        {
            // The old version may still be closing for a moment after starting this one.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (TryDelete(exe + ".old") && TryDelete(exe + ".download")) return;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<string> DownloadAsync(ReleaseInfo release, string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var response = await Http.GetAsync(release.ExeUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.ExeSize;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
        await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long done = 0;
            DateTime lastReport = DateTime.MinValue;
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                hash.AppendData(buffer, 0, read);
                done += read;
                if (total > 0 && DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(200))
                {
                    lastReport = DateTime.UtcNow;
                    Progress = Math.Min(1, (double)done / total);
                    Changed?.Invoke();
                }
            }
        }
        Progress = 1;
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private bool Fail(string message, bool announce = false)
    {
        Error = message;
        SetState(UpdateState.Failed);
        if (announce) InstallFailed?.Invoke(message);
        return false;
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskBarGame-HarvestEdition", CurrentVersion.ToString()));
        return client;
    }
}
