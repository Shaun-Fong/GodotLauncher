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
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;

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

    public AppServices(AppPaths paths, FileLog log)
    {
        Paths = paths;
        Log = log;

        SettingsStore = new SettingsStore(paths.ConfigFile);
        var loaded = SettingsStore.Load().Normalize();
        Settings = loaded;

        _localization = LocalizationService.Load((message, error) => log.Error(message, error));
        Loc = _localization.LoadStrings(loaded.Language);

        Http = CreateHttpClient(decompress: true, timeout: TimeSpan.FromSeconds(90));
        DownloadHttp = CreateHttpClient(decompress: false, timeout: Timeout.InfiniteTimeSpan);

        CatalogCache = new CatalogCache(paths.CatalogCacheFile);
        CatalogService = new CatalogService(
            Http,
            [
                new VersionsYmlSource("versions-yml", new Uri(VersionsYmlUrl)),
                new HtmlArchiveSource()
            ],
            CatalogCache);

        Resolver = new AssetResolver(Settings.MirrorTemplate);
        Downloader = new HttpDownloader(DownloadHttp);
        Installer = new Installer(paths);
        Library = new InstallationLibrary(new InstallRegistry(paths.InstalledFile));
        Launcher = new EditorLauncher();
        Downloads = new DownloadManager(this);

        Library.Changed += (_, _) =>
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                InstallationChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => InstallationChanged?.Invoke(this, EventArgs.Empty));
            }
        };
        ApplyTheme(Settings.Theme);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed; views refresh localized computed strings.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Raised after any setting changed and was persisted.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when the installation list changed on disk.</summary>
    public event EventHandler? InstallationChanged;

    /// <summary>Raised when the catalog was loaded from cache or refreshed.</summary>
    public event EventHandler? CatalogChanged;

    public AppPaths Paths { get; }

    public FileLog Log { get; }

    public SettingsStore SettingsStore { get; }

    public LauncherSettings Settings { get; private set; }

    public Strings Loc { get; private set; }

    /// <summary>Languages discovered from the localization files, independent of the active catalog.</summary>
    public IReadOnlyList<LanguageOption> Languages => _localization.Languages;

    public HttpClient Http { get; }

    public HttpClient DownloadHttp { get; }

    public CatalogCache CatalogCache { get; }

    public CatalogService CatalogService { get; }

    public AssetResolver Resolver { get; private set; }

    public HttpDownloader Downloader { get; }

    public Installer Installer { get; }

    public InstallationLibrary Library { get; }

    public EditorLauncher Launcher { get; }

    public DownloadManager Downloads { get; }

    /// <summary>Latest catalog shown to the user, cached or fresh.</summary>
    public VersionCatalog? Catalog { get; private set; }

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

        if (!string.Equals(next.MirrorTemplate, before.MirrorTemplate, StringComparison.Ordinal))
        {
            Resolver = new AssetResolver(next.MirrorTemplate);
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
        Task.Run(() => Library.LoadAsync(cancellationToken), cancellationToken)
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
            }
            else if (result.Status == CatalogRefreshStatus.NotModified)
            {
                LastCatalogUpdate = DateTimeOffset.Now;
                Log.Info("Catalog is up to date.");
            }
            else
            {
                Log.Warn($"Catalog refresh failed: {result.Message}");
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

    /// <summary>Starts an editor and records the launch time.</summary>
    public LaunchResult Launch(InstalledVersion installation)
    {
        var result = Launcher.Launch(installation, new LaunchOptions
        {
            UseConsole = Settings.LaunchWithConsole
        });

        _ = Library.TouchLaunchedAsync(installation.Id, DateTimeOffset.Now);
        Log.Info($"Launched {installation.VersionId} ({installation.Architecture}): {result.CommandLine}");
        return result;
    }

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static HttpClient CreateHttpClient(bool decompress, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = decompress ? DecompressionMethods.All : DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GodotLauncher", AppVersion));
        return client;
    }
}
