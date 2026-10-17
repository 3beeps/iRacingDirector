using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IRacingDirector.Core;

namespace IRacingDirector.Services;

/// <param name="Sha256">Lower-case hex SHA-256 GitHub publishes for the setup asset, or null if it gave none.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string SetupName, string SetupUrl, string PageUrl, string? Sha256 = null);

/// <summary>
/// Looks for a newer release on GitHub at startup and every 24 hours, and installs it on request.
/// Releases are made by tools/release.ps1.
/// </summary>
public sealed class UpdateChecker : ObservableObject, IDisposable
{
    public const string Repository = "3beeps/iRacingDirector";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly Regex SetupAssetName = new(@"^iRacingDirector-Setup-[0-9A-Za-z.\-]+\.exe$", RegexOptions.IgnoreCase);
    private static readonly HttpClient Http = CreateClient();

    private readonly DirectorSettings _settings;
    private readonly Logger _log;
    private CancellationTokenSource? _loop;
    private ReleaseInfo? _available;
    private bool _isInstalling;
    private string _bannerText = "";

    public UpdateChecker(DirectorSettings settings, Logger log)
    {
        _settings = settings;
        _log = log;
    }

    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>The newer release, or null when Director is up to date.</summary>
    public ReleaseInfo? Available
    {
        get => _available;
        private set
        {
            if (!SetField(ref _available, value)) return;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            UpdateBanner();
        }
    }

    public bool IsUpdateAvailable => _available is not null;

    public bool IsInstalling
    {
        get => _isInstalling;
        private set { if (SetField(ref _isInstalling, value)) UpdateBanner(); }
    }

    public string BannerText { get => _bannerText; private set => SetField(ref _bannerText, value); }

    /// <summary>Raised once for each newer version found.</summary>
    public event Action<ReleaseInfo>? UpdateFound;

    public void Start()
    {
        _settings.PropertyChanged += OnSettingChanged;
        if (_settings.CheckForUpdates) StartLoop();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DirectorSettings.CheckForUpdates)) return;
        StopLoop();
        if (_settings.CheckForUpdates) StartLoop();
    }

    private void StartLoop()
    {
        _loop = new CancellationTokenSource();
        _ = RunAsync(_loop.Token);
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(FirstCheckDelay, ct);
            while (true)
            {
                await CheckAsync(ct);
                await Task.Delay(CheckInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await Http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _log.Info($"Couldn't check for updates (GitHub said {(int)response.StatusCode}).");
                return;
            }

            var release = ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token));
            if (release is null || release.Version <= CurrentVersion)
            {
                Available = null;
                return;
            }
            if (Available?.Version == release.Version) return;

            Available = release;
            _log.Info($"iRacing Director {release.Version} is available (you have {CurrentVersion}).");
            UpdateFound?.Invoke(release);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Info($"Couldn't check for updates: {ex.Message}");
        }
    }

    /// <summary>Downloads the setup to %TEMP% and runs it silently. The installer closes and restarts Director.</summary>
    public async Task InstallAsync()
    {
        if (Available is null || IsInstalling) return;
        IsInstalling = true;
        try
        {
            // The banner may be hours old; install whatever is newest now, not the version it first saw.
            await CheckAsync();
            if (Available is not { } release)
            {
                IsInstalling = false;
                return;
            }

            // A fresh folder per download, so nothing else can have planted or swapped the file beforehand.
            var folder = Path.Combine(Path.GetTempPath(), "iRacingDirector-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, release.SetupName);
            _log.Info($"Downloading iRacing Director {release.Version}...");
            using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(file);
            }

            if (release.Sha256 is { } expected)
            {
                string actual;
                await using (var file = File.OpenRead(path))
                    actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file)).ToLowerInvariant();
                if (actual != expected)
                {
                    File.Delete(path);
                    throw new InvalidDataException("the download didn't match the checksum GitHub published, so it wasn't installed");
                }
            }

            _log.Info("Installing the update. Director will close and start again when it's done.");
            Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true })?.Dispose();

            // The installer closes Director within seconds. Still here after two minutes means it didn't run.
            await Task.Delay(TimeSpan.FromMinutes(2));
            _log.Warn("The update didn't finish. Click the banner to try again.");
            IsInstalling = false;
        }
        catch (Exception ex)
        {
            _log.Error($"Update failed: {ex.Message}");
            IsInstalling = false;
        }
    }

    private void UpdateBanner()
    {
        BannerText = _available is not { } r ? ""
            : IsInstalling ? $"Downloading and installing iRacing Director {r.Version}..."
            : $"Update available: iRacing Director {r.Version} (you have {CurrentVersion}). Click to install.";
    }

    /// <summary>Reads a GitHub "latest release" response. Null if it has no usable version or setup exe.</summary>
    internal static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagProp) || tagProp.GetString() is not { } tag) return null;
        if (!TryParseTag(tag, out var version)) return null;
        var page = root.TryGetProperty("html_url", out var pageProp) ? pageProp.GetString() ?? "" : "";

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (name is null || url is null || !SetupAssetName.IsMatch(name)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest[7..].ToLowerInvariant()
                : null;
            return new ReleaseInfo(version, tag, name, url, page, sha256);
        }
        return null;
    }

    /// <summary>Parses "v1.2.3" (or "1.2.3"). Pre-release tags like "v1.2.3-beta" are rejected.</summary>
    internal static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        if (text.Split('.').Length is < 2 or > 3) return false;
        if (!Version.TryParse(text, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>True when the tag names a newer version than <paramref name="current"/>.</summary>
    internal static bool IsNewer(string? tag, Version current) =>
        TryParseTag(tag, out var version) && version > Normalize(current);

    /// <summary>Major.Minor.Build with missing parts as 0, so 1.1 == 1.1.0 == 1.1.0.0.</summary>
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("iRacingDirector", CurrentVersionText()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string CurrentVersionText() => typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingChanged;
        StopLoop();
    }
}
