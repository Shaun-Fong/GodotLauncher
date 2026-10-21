using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GodotLauncher.App.Services;
using GodotLauncher.App.ViewModels;
using GodotLauncher.App.Views;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App;

/// <summary>
/// Composition root. Creates the services once, loads the cached catalog from disk before the
/// first frame and schedules every network request after the window has been shown.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;
    private DispatcherTimer? _refreshTimer;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var startup = System.Diagnostics.Stopwatch.StartNew();
        var services = new AppServices(Program.Paths, Program.Log);
        _services = services;
        var servicesMs = startup.ElapsedMilliseconds;

        // Scratch space from an interrupted install is never needed after a restart.
        services.CleanStagingDirectories();

        // Update scripts remove themselves, but their temporary folder stays locked until they exit.
        var staleUpdates = UpdateInstaller.CleanupStaleDirectories(TimeSpan.FromHours(1));
        if (staleUpdates > 0)
        {
            services.Log.Info($"Removed {staleUpdates} temporary update folder(s).");
        }

        // Reading the cache is a local file access and guarantees a populated first frame.
        try
        {
            services.LoadCachedCatalogBlocking();
        }
        catch (Exception ex)
        {
            Program.Log.Write(LogLevel.Warning, "Could not load the cached catalog.", ex);
        }

        var catalogMs = startup.ElapsedMilliseconds;

        try
        {
            services.LoadInstalledBlocking();
        }
        catch (Exception ex)
        {
            Program.Log.Write(LogLevel.Warning, "Could not load the installation library.", ex);
        }

        try
        {
            services.LoadProjectsBlocking();
        }
        catch (Exception ex)
        {
            Program.Log.Write(LogLevel.Warning, "Could not load the project list.", ex);
        }

        var libraryMs = startup.ElapsedMilliseconds;
        var viewModel = new MainWindowViewModel(services);
        var window = new MainWindow(viewModel, services, Program.Benchmark);
        var windowMs = startup.ElapsedMilliseconds;

        Program.Log.Info(
            $"Startup breakdown: services={servicesMs} ms, catalog={catalogMs} ms, library={libraryMs} ms, window={windowMs} ms.");

        TrayService? tray = null;

        void CreateTray()
        {
            try
            {
                tray = new TrayService(services, viewModel, window);
                window.AttachTray(tray);
            }
            catch (Exception ex)
            {
                Program.Log.Write(LogLevel.Warning, "The tray icon is not available; closing quits the app.", ex);
            }
        }

        // Building a tray icon costs hundreds of milliseconds on Windows, so it must not
        // delay the first frame. Close-to-tray applies as soon as the icon exists.
        Dispatcher.UIThread.Post(CreateTray, DispatcherPriority.Background);

        if (Program.Instance is not null)
        {
            Program.Instance.ShowRequested += (_, _) => Dispatcher.UIThread.Post(window.ShowFromTray);
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.MainWindow = window;
        desktop.Exit += (_, _) =>
        {
            services.FlushSettings();
            tray?.Dispose();
        };

        if (!Program.StartHidden || Program.Benchmark)
        {
            window.Show();
        }

        // The UI is interactive now; refreshing the catalog may touch the network.
        Dispatcher.UIThread.Post(
            () => viewModel.Explore.Activate(),
            DispatcherPriority.Background);

        Dispatcher.UIThread.Post(
            () => _ = services.RefreshCatalogAsync(),
            DispatcherPriority.Background);

        Dispatcher.UIThread.Post(
            () => _ = CheckForLauncherUpdateAsync(services),
            DispatcherPriority.ApplicationIdle);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _refreshTimer.Tick += (_, _) => RefreshWhenDue(services);
        _refreshTimer.Start();

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForLauncherUpdateAsync(AppServices services)
    {
        try
        {
            await services.CheckForUpdatesAsync();
        }
        catch (Exception exception)
        {
            services.Log.Warn($"Could not check for launcher updates: {exception.Message}");
        }
    }

    private static void RefreshWhenDue(AppServices services)
    {
        var hours = services.Settings.RefreshIntervalHours;
        if (hours <= 0)
        {
            return;
        }

        if (services.LastCatalogUpdate is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(hours))
        {
            return;
        }

        _ = services.RefreshCatalogAsync();
    }
}
