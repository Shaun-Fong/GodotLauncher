using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Styling;
using GodotLauncher.App.Downloads;
using GodotLauncher.App.Localization;
using GodotLauncher.Core.Assets;
using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Install;
using GodotLauncher.Core.Launch;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Net;
using GodotLauncher.Core.Projects;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;
using GodotLauncher.Core.Updates;

namespace GodotLauncher.App.Services;

/// <summary>
/// Composition root and observable application state. Views bind to <see cref="Loc"/> and
/// <see cref="Settings"/>, so language and theme changes propagate without re-creating the
/// window.
/// </summary>
public sealed class AppServices : INotifyPropertyChanged
{
    private const string VersionsYmlUrl =
        "https://raw.githubusercontent.com/godotengine/godot-website/master/_data/versions.yml";

    private readonly LocalizationService _localization;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private CancellationTokenSource? _saveCts;
    private string? _networkIssue;

    public AppServices(AppPaths paths, FileLog log)
    {
        Paths = paths;
        Log = log;

        SettingsStore = new SettingsStore(paths.ConfigFile);
        var loaded = SettingsStore.Load().Normalize();
        Settings = loaded;

        // The install root lives in the settings file, so it has to be applied
        // before anything reads AppPaths.InstallRoot.
        if (!string.IsNullOrWhiteSpace(loaded.InstallRoot))
        {
            var configured = paths.WithInstallRoot(loaded.InstallRoot);
            try
            {
                configured.EnsureDirectories();
                Paths = configured;
            }
            catch (Exception ex) when (IsPathFailure(ex))
            {
                log.Error($"Could not use the install location '{configured.InstallRoot}'. Falling back to the default.", ex);
                Settings = loaded with { InstallRoot = string.Empty };
                InstallRootError = ex.Message;
            }
        }

        _localization = LocalizationService.Load((message, error) => log.Error(message, error));
        Loc = _localization.LoadStrings(loaded.Language);

        Http = ProxySupport.CreateClient(Settings, decompress: true, TimeSpan.FromSeconds(90));
        DownloadHttp = ProxySupport.CreateClient(Settings, decompress: false, Timeout.InfiniteTimeSpan);

        CatalogCache = new CatalogCache(paths.CatalogCacheFile);
        CatalogService = new CatalogService(
            Http,
            [
                new VersionsYmlSource("versions-yml", new Uri(VersionsYmlUrl)),
                new HtmlArchiveSource()
            ],
            CatalogCache);

        Resolver = new AssetResolver();
        Downloader = new HttpDownloader(DownloadHttp);
        Installer = new Installer(Paths);
        Library = new InstallationLibrary(new InstallRegistry(paths.InstalledFile));
        Projects = new ProjectStore(paths.ProjectsFile);
        Launcher = new EditorLauncher();
        Updates = new UpdateChecker(Http, "Shaun-Fong", "GodotLauncher");
        Downloads = new DownloadManager(this);

        Library.Changed += (_, _) => RaiseOnUiThread(() => InstallationChanged?.Invoke(this, EventArgs.Empty));
        Projects.Changed += (_, _) => RaiseOnUiThread(() => ProjectChanged?.Invoke(this, EventArgs.Empty));
        ApplyTheme(Settings.Theme);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed; views refresh localized computed strings.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Raised after any setting changed and was persisted.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when the installation list changed on disk.</summary>
    public event EventHandler? InstallationChanged;

    /// <summary>Raised when the tracked project list changed.</summary>
    public event EventHandler? ProjectChanged;

    /// <summary>Raised after an editor was started, whichever page started it.</summary>
    public event EventHandler<InstalledVersion>? EditorLaunched;

    /// <summary>Raised when the catalog was loaded from cache or refreshed.</summary>
    public event EventHandler? CatalogChanged;

    /// <summary>Resolved directories. <see cref="AppPaths.InstallRoot"/> follows the settings.</summary>
    public AppPaths Paths { get; private set; }

    /// <summary>Set when a configured install location could not be used.</summary>
    public string? InstallRootError { get; private set; }

    public FileLog Log { get; }

    public SettingsStore SettingsStore { get; }

    public LauncherSettings Settings { get; private set; }

    public Strings Loc { get; private set; }

    /// <summary>Languages discovered from the localization files, independent of the active catalog.</summary>
    public IReadOnlyList<LanguageOption> Languages => _localization.Languages;

    public HttpClient Http { get; private set; }

    public HttpClient DownloadHttp { get; private set; }

    public CatalogCache CatalogCache { get; }

    public CatalogService CatalogService { get; private set; }

    public AssetResolver Resolver { get; private set; }

    public HttpDownloader Downloader { get; private set; }

    public Installer Installer { get; private set; }

    public InstallationLibrary Library { get; }

    public ProjectStore Projects { get; }

    public EditorLauncher Launcher { get; }

    public DownloadManager Downloads { get; }

    /// <summary>Reads the latest launcher release from GitHub.</summary>
    public UpdateChecker Updates { get; private set; }

    /// <summary>Newer release found by the last update check, if any.</summary>
    public LauncherRelease? AvailableUpdate { get; private set; }

    /// <summary>True while the update check is running.</summary>
    public bool IsCheckingUpdates { get; private set; }

    /// <summary>Description of the last network problem, shown as an inline hint.</summary>
    public string? NetworkIssue => _networkIssue;

    /// <summary>True when <see cref="NetworkIssue"/> has a value.</summary>
    public bool HasNetworkIssue => _networkIssue is not null;

    /// <summary>Latest catalog shown to the user, cached or fresh.</summary>
    public VersionCatalog? Catalog { get; private set; }

    /// <summary>Test seam: installs a catalog without touching the network.</summary>
    internal void SetCatalog(VersionCatalog catalog)
    {
        Catalog = catalog;
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ids that were added by the most recent successful refresh.</summary>
    public IReadOnlySet<string> NewVersionIds { get; private set; } = new HashSet<string>();

    /// <summary>When the catalog was last refreshed successfully.</summary>
    public DateTimeOffset? LastCatalogUpdate { get; private set; }

    /// <summary>True while a catalog refresh is in progress.</summary>
    public bool IsRefreshing { get; private set; }

    /// <summary>Version shown in the sidebar and settings page.</summary>
    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Installs the given settings, applying side effects (theme, language, …).</summary>
    public void UpdateSettings(Func<LauncherSettings, LauncherSettings> mutate, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var before = Settings;
        var next = mutate(before).Normalize();
        if (next == before)
        {
            return;
        }

        if (!string.Equals(next.InstallRoot, before.InstallRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (TryApplyInstallRoot(next.InstallRoot, out var installRootError))
            {
                InstallRootError = null;
            }
            else
            {
                InstallRootError = installRootError;
                next = next with { InstallRoot = before.InstallRoot };
            }

            OnPropertyChanged(nameof(InstallRootError));
        }

        Settings = next;

        if (next.Theme != before.Theme)
        {
            ApplyTheme(next.Theme);
        }

        if (!string.Equals(next.Language, before.Language, StringComparison.OrdinalIgnoreCase))
        {
            Loc = _localization.LoadStrings(next.Language);
            OnPropertyChanged(nameof(Loc));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        if (next.Proxy != before.Proxy ||
            !string.Equals(next.ProxyUrl, before.ProxyUrl, StringComparison.OrdinalIgnoreCase))
        {
            RebuildNetworkClients();
        }

        if (next.ConcurrentDownloads != before.ConcurrentDownloads)
        {
            Downloads.UpdateConcurrency(next.ConcurrentDownloads);
        }

        if (next.Autostart != before.Autostart)
        {
            TryApplyAutostart(next.Autostart);
        }

        OnPropertyChanged(nameof(Settings));
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        if (save)
        {
            ScheduleSave();
        }
    }

    /// <summary>
    /// Moves new installations to another root. Existing editors stay where they are
    /// because the registry keeps absolute paths.
    /// </summary>
    public bool TryApplyInstallRoot(string? installRoot, out string? error)
    {
        var candidate = string.IsNullOrWhiteSpace(installRoot)
            ? AppPaths.CreateFor(Paths.DataDirectory)
            : Paths.WithInstallRoot(installRoot);

        try
        {
            candidate.EnsureDirectories();
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            error = ex.Message;
            Log.Warn($"Install location '{candidate.InstallRoot}' is not usable: {ex.Message}");
            return false;
        }

        Paths = candidate;
        Installer = new Installer(Paths);
        error = null;
        Log.Info($"Install location is now '{Paths.InstallRoot}'.");
        return true;
    }

    private static bool IsPathFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;

    /// <summary>
    /// Loads the cached catalog without touching the dispatcher. This runs before the first
    /// window exists, so the list is already populated when the frame is rendered.
    /// </summary>
    public VersionCatalog? LoadCachedCatalogBlocking(CancellationToken cancellationToken = default)
    {
        var cached = Task.Run(() => CatalogService.LoadCachedAsync(cancellationToken), cancellationToken)
            .GetAwaiter()
            .GetResult();
        if (cached is not null)
        {
            Catalog = cached;
        }

        return cached;
    }

    /// <summary>
    /// Loads the installation library before the first frame. Running the load on the
    /// thread pool keeps the continuation off the UI thread.
    /// </summary>
    public void LoadInstalledBlocking(CancellationToken cancellationToken = default)
    {
        // Older launchers put .NET editors under mono/; migrate before the
        // registry is read so records can be repaired in the same pass.
        if (Installer.MigrateLegacyEditionFolders())
        {
            Log.Info("Moved .NET editors from mono/ to dotnet/.");
        }

        Task.Run(() => Library.LoadAsync(cancellationToken), cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Loads the project list before the first frame.</summary>
    public void LoadProjectsBlocking(CancellationToken cancellationToken = default)
    {
        Task.Run(() => Projects.LoadAsync(cancellationToken), cancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Loads the cached catalog so the window can render instantly.</summary>
    public async Task<VersionCatalog?> LoadCachedCatalogAsync(CancellationToken cancellationToken = default)
    {
        var cached = await CatalogService.LoadCachedAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            Catalog = cached;
            await OnUiThreadAsync(() =>
            {
                OnPropertyChanged(nameof(Catalog));
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        return cached;
    }

    /// <summary>Refreshes the catalog through all sources. Returns null when already running.</summary>
    public async Task<CatalogRefreshResult?> RefreshCatalogAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            SetRefreshing(true);
            var previous = Catalog;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));

            var result = await CatalogService.RefreshAsync(previous, progress, timeout.Token).ConfigureAwait(false);

            if (result.Status == CatalogRefreshStatus.Updated && result.Catalog is { } catalog)
            {
                Catalog = catalog;
                NewVersionIds = previous is null ? new HashSet<string>() : result.NewVersionIds;
                LastCatalogUpdate = DateTimeOffset.Now;
                Log.Info($"Catalog refreshed from {catalog.Source}: {catalog.Count} versions, " +
                         $"{NewVersionIds.Count} new.");
                SetNetworkIssue(null);
            }
            else if (result.Status == CatalogRefreshStatus.NotModified)
            {
                LastCatalogUpdate = DateTimeOffset.Now;
                Log.Info("Catalog is up to date.");
                SetNetworkIssue(null);
            }
            else
            {
                Log.Warn($"Catalog refresh failed: {result.Message}");
                SetNetworkIssue(result.Message);
            }

            await OnUiThreadAsync(() =>
            {
                OnPropertyChanged(nameof(Catalog));
                OnPropertyChanged(nameof(NewVersionIds));
                OnPropertyChanged(nameof(LastCatalogUpdate));
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            Log.Warn("Catalog refresh was cancelled or timed out.");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Catalog refresh crashed.", ex);
            return null;
        }
        finally
        {
            SetRefreshing(false);
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Checks GitHub for a newer launcher release. Network problems are reported
    /// inline through <see cref="NetworkIssue"/> instead of blocking the UI.
    /// </summary>
    public async Task<LauncherRelease?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsCheckingUpdates)
        {
            return null;
        }

        IsCheckingUpdates = true;
        OnPropertyChanged(nameof(IsCheckingUpdates));
        try
        {
            var current = VersionNumber.TryParse(AppVersion, out var version)
                ? version
                : new VersionNumber(0, 1, 0);

            var release = await Updates.CheckAsync(current, cancellationToken).ConfigureAwait(false);
            AvailableUpdate = release;
            OnPropertyChanged(nameof(AvailableUpdate));

            if (release is null)
            {
                Log.Info("Launcher is up to date.");
            }
            else
            {
                Log.Info($"Launcher update available: {release.Tag}.");
            }

            SetNetworkIssue(null);
            return release;
        }
        catch (Exception ex) when (IsRateLimited(ex))
        {
            // The anonymous GitHub API allows 60 requests per hour per address. A shared address can
            // exhaust that budget, so this is not reported as a network problem the user has to fix.
            Log.Warn("GitHub rate limit reached while checking for updates.");
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            SetNetworkIssue(ex.Message);
            // Callers such as the settings page need the real failure reason;
            // swallowing it here made every failed check look like "up to date".
            throw;
        }
        finally
        {
            IsCheckingUpdates = false;
            RaiseOnUiThread(() => OnPropertyChanged(nameof(IsCheckingUpdates)));
        }
    }

    private static bool IsRateLimited(Exception exception) =>
        exception is HttpRequestException
        {
            StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests,
        };

    /// <summary>Downloads a release asset into the downloads folder.</summary>
    public async Task<string> DownloadUpdateAsync(
        LauncherReleaseAsset asset,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var target = Path.Combine(Paths.DownloadDirectory, asset.Name);
        var request = new DownloadRequest
        {
            Uri = asset.DownloadUri,
            DestinationPath = target,
            ExpectedSize = asset.Size > 0 ? asset.Size : null,
        };

        var result = await Downloader.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);
        Log.Info($"Downloaded launcher update to {result.FilePath}.");
        return result.FilePath;
    }

    /// <summary>Re-creates the HTTP clients after a proxy change.</summary>
    private void RebuildNetworkClients()
    {
        var previousHttp = Http;
        var previousDownload = DownloadHttp;

        Http = ProxySupport.CreateClient(Settings, decompress: true, TimeSpan.FromSeconds(90));
        DownloadHttp = ProxySupport.CreateClient(Settings, decompress: false, Timeout.InfiniteTimeSpan);
        CatalogService = new CatalogService(
            Http,
            [
                new VersionsYmlSource("versions-yml", new Uri(VersionsYmlUrl)),
                new HtmlArchiveSource()
            ],
            CatalogCache);
        Downloader = new HttpDownloader(DownloadHttp);
        Updates = new UpdateChecker(Http, "Shaun-Fong", "GodotLauncher");

        previousHttp.Dispose();
        previousDownload.Dispose();
        SetNetworkIssue(null);
        Log.Info($"Proxy changed to {Settings.Proxy}; network clients were rebuilt.");
    }

    private void SetNetworkIssue(string? message)
    {
        if (string.Equals(_networkIssue, message, StringComparison.Ordinal))
        {
            return;
        }

        _networkIssue = message;
        RaiseOnUiThread(() =>
        {
            OnPropertyChanged(nameof(NetworkIssue));
            OnPropertyChanged(nameof(HasNetworkIssue));
        });
    }

    /// <summary>Starts an editor and records the launch time.</summary>
    public LaunchResult Launch(InstalledVersion installation)
    {
        var result = Launcher.Launch(installation, new LaunchOptions
        {
            UseConsole = Settings.LaunchWithConsole
        });

        _ = Library.TouchLaunchedAsync(installation.Id, DateTimeOffset.Now);
        Log.Info($"Launched {installation.VersionId} ({installation.Architecture}): {result.CommandLine}");
        EditorLaunched?.Invoke(this, installation);
        return result;
    }

    /// <summary>
    /// Picks the editor used to open a project: the explicit choice first, then an
    /// installation matching the project's major.minor version, then the default editor.
    /// </summary>
    public InstalledVersion? FindEditorForProject(ProjectRecord project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.EditorId is { Length: > 0 } editorId)
        {
            var chosen = Library.Items.FirstOrDefault(
                installation => string.Equals(installation.Id, editorId, StringComparison.OrdinalIgnoreCase));
            if (chosen is not null)
            {
                return chosen;
            }
        }

        if (!project.TryGetVersion(out var projectVersion))
        {
            return Library.FindDefault();
        }

        InstalledVersion? best = null;
        VersionNumber bestVersion = default;
        foreach (var installation in Library.Items)
        {
            if (!VersionNumber.TryParse(installation.VersionId, out var version) ||
                version.Major != projectVersion.Major ||
                version.Minor != projectVersion.Minor)
            {
                continue;
            }

            if (best is null ||
                version.CompareTo(bestVersion) > 0 ||
                (version.CompareTo(bestVersion) == 0 && IsStable(installation) && !IsStable(best)))
            {
                best = installation;
                bestVersion = version;
            }
        }

        return best;
    }

    /// <summary>Opens a project in the matching editor and records the access time.</summary>
    /// <exception cref="LaunchException">No editor is available or the editor cannot be started.</exception>
    public LaunchResult LaunchProject(ProjectRecord project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var editor = FindEditorForProject(project)
            ?? throw new LaunchException(
                $"No installed editor matches Godot {project.VersionText ?? "version"} used by '{project.Name}'. Install that version first.");

        return LaunchProject(project, editor);
    }

    /// <summary>Opens a project with a specific editor and records the access time.</summary>
    public LaunchResult LaunchProject(ProjectRecord project, InstalledVersion editor)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(editor);

        var result = Launcher.LaunchProject(editor, project.Path, new LaunchOptions
        {
            UseConsole = Settings.LaunchWithConsole
        });

        _ = Projects.TouchOpenedAsync(project.Path, DateTimeOffset.Now);
        _ = Library.TouchLaunchedAsync(editor.Id, DateTimeOffset.Now);
        Log.Info($"Opened project '{project.Path}' with {editor.VersionId} ({editor.Architecture}): {result.CommandLine}");
        EditorLaunched?.Invoke(this, editor);
        return result;
    }

    private static bool IsStable(InstalledVersion installation) =>
        installation.VersionId.EndsWith("-stable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens a file, folder or URL with the operating system handler.</summary>
    public void OpenExternally(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{target}'.", ex);
        }
    }

    /// <summary>Removes extraction directories left behind by an interrupted install.</summary>
    public void CleanStagingDirectories()
    {
        try
        {
            var removed = Installer.CleanStagingDirectories(TimeSpan.FromHours(1));
            if (removed > 0)
            {
                Log.Info($"Removed {removed} stale staging director{(removed == 1 ? "y" : "ies")}.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not clean the staging directory.", ex);
        }
    }

    /// <summary>Writes pending settings to disk immediately.</summary>
    public void FlushSettings()
    {
        _saveCts?.Cancel();
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings.", ex);
        }
    }

    private void ApplyTheme(ThemeMode mode)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void TryApplyAutostart(bool enabled)
    {
        try
        {
            AutostartService.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the autostart entry.", ex);
        }
    }

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        var snapshot = Settings;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cts.Token).ConfigureAwait(false);
                SettingsStore.Save(snapshot);
            }
            catch (OperationCanceledException)
            {
                // A newer change is queued; ignore.
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings.", ex);
            }
        });
    }

    private void SetRefreshing(bool value)
    {
        IsRefreshing = value;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsRefreshing)));
        }
    }

    private static async Task OnUiThreadAsync(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
    }

    private static void RaiseOnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


}
