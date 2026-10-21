using Avalonia.Controls;
using Avalonia.Platform;
using GodotLauncher.App.ViewModels;
using GodotLauncher.App.Views;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Services;

/// <summary>
/// Tray icon and its native menu. The menu is rebuilt whenever the language, the settings or the
/// installation list changes so headers and the recent list stay current.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _viewModel;
    private readonly MainWindow _window;
    private readonly TrayIcon? _tray;
    private bool _disposed;

    public TrayService(AppServices services, MainWindowViewModel viewModel, MainWindow window)
    {
        _services = services;
        _viewModel = viewModel;
        _window = window;

        try
        {
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GodotLauncher/Assets/app.png")));
            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = viewModel.AppName,
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => _window.ToggleFromTray();
            RebuildMenu();

            _services.LanguageChanged += OnChanged;
            _services.SettingsChanged += OnChanged;
            _services.InstallationChanged += OnChanged;
        }
        catch (Exception ex)
        {
            services.Log.Write(LogLevel.Warning, "Could not create the tray icon.", ex);
            _tray = null;
        }
    }

    /// <summary>False when the platform has no usable system tray.</summary>
    public bool IsAvailable => _tray is not null;

    /// <summary>Recreates the native menu from the current strings and installations.</summary>
    public void RebuildMenu()
    {
        if (_tray is null || _disposed)
        {
            return;
        }

        var strings = _services.Loc;
        var menu = new NativeMenu();

        var show = new NativeMenuItem(strings.Tray.Show);
        show.Click += (_, _) => _window.ShowFromTray();
        menu.Add(show);

        var launch = new NativeMenuItem(strings.Tray.LaunchDefault);
        launch.Click += (_, _) =>
        {
            if (_viewModel.LaunchDefault())
            {
                ApplyAfterLaunch();
            }
        };
        menu.Add(launch);

        var recent = new NativeMenuItem(strings.Tray.Recent);
        var recentMenu = new NativeMenu();
        var items = _services.Library.Items
            .Where(item => item.LastLaunched is not null)
            .OrderByDescending(item => item.LastLaunched)
            .Take(5)
            .ToArray();

        if (items.Length == 0)
        {
            recentMenu.Add(new NativeMenuItem(strings.Tray.NoDefault) { IsEnabled = false });
        }
        else
        {
            foreach (var installation in items)
            {
                var entry = new NativeMenuItem(installation.Label);
                var captured = installation;
                entry.Click += (_, _) =>
                {
                    if (_viewModel.LaunchInstallation(captured))
                    {
                        ApplyAfterLaunch();
                    }
                };
                recentMenu.Add(entry);
            }
        }

        recent.Menu = recentMenu;
        menu.Add(recent);

        menu.Add(new NativeMenuItemSeparator());

        var refresh = new NativeMenuItem(strings.Tray.Refresh);
        refresh.Click += (_, _) =>
        {
            if (_viewModel.RefreshCommand.CanExecute(null))
            {
                _viewModel.RefreshCommand.Execute(null);
            }
        };
        menu.Add(refresh);

        var settings = new NativeMenuItem(strings.Tray.Settings);
        settings.Click += (_, _) =>
        {
            _window.ShowFromTray();
            _viewModel.ShowSettingsCommand.Execute(null);
        };
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem(strings.Tray.Exit);
        exit.Click += (_, _) => _window.ExitApplication();
        menu.Add(exit);

        _tray.ToolTipText = strings.Tray.Tooltip;
        _tray.Menu = menu;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.LanguageChanged -= OnChanged;
        _services.SettingsChanged -= OnChanged;
        _services.InstallationChanged -= OnChanged;
        _tray?.Dispose();
    }

    private void ApplyAfterLaunch()
    {
        switch (_services.Settings.AfterLaunch)
        {
            case AfterLaunchBehavior.Hide:
                _window.Hide();
                break;
            case AfterLaunchBehavior.Quit:
                _window.ExitApplication();
                break;
        }
    }

    private void OnChanged(object? sender, EventArgs e) => RebuildMenu();
}
