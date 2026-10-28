using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.App.ViewModels;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Views;

/// <summary>
/// Shell window: custom chrome, close-to-tray behaviour and window sizing.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AppServices _services;
    private readonly bool _benchmark;
    private TrayService? _tray;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = null!;
        _services = null!;
    }

    public MainWindow(MainWindowViewModel viewModel, AppServices services, bool benchmark)
    {
        _viewModel = viewModel;
        _services = services;
        _benchmark = benchmark;

        InitializeComponent();

        // File and folder pickers need a TopLevel; without it every picker returns
        // null and the import/create buttons look dead.
        FilePickers.Host = this;

        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GodotLauncher/Assets/app.png")));
        DataContext = viewModel;

        viewModel.EditorLaunched += OnEditorLaunched;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closing += OnClosing;
        Opened += OnOpened;

        // The initial page is rendered before the first PropertyChanged event,
        // so its view has to be created here.
        EnsurePageView();
    }

    /// <summary>Connects the tray icon so the window can hide instead of closing.</summary>
    public void AttachTray(TrayService? tray)
    {
        _tray = tray;
    }

    /// <summary>Brings the window back from the tray or from a second instance.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Toggles the window between visible and hidden, used by a tray click.</summary>
    public void ToggleFromTray()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            ShowFromTray();
        }
    }

    /// <summary>Shuts the application down through the normal close path.</summary>
    public void ExitApplication()
    {
        _exiting = true;
        Close();

        // The application uses explicit shutdown because the tray icon keeps it alive
        // after the last window closes.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var elapsed = Program.Clock.ElapsedMilliseconds;
        _services.Log.Info($"Main window shown after {elapsed} ms.");

        if (!_benchmark)
        {
            return;
        }

        // Benchmark mode reports the time to first frame and exits.
        _services.Log.Info($"startup-ms={elapsed}");
        DispatcherTimer.RunOnce(
            () =>
            {
                Console.WriteLine($"startup-ms={elapsed}");
                ExitApplication();
            },
            TimeSpan.FromMilliseconds(150));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_exiting || _tray is null || !_tray.IsAvailable)
        {
            return;
        }

        if (_services.Settings.CloseBehavior == CloseBehavior.Tray)
        {
            e.Cancel = true;
            Hide();
            _services.Log.Info("Window hidden to the tray.");
        }
    }

    /// <summary>
    /// Builds secondary pages on first navigation. Parsing every view at startup would
    /// delay the first frame for features the user may never open.
    /// </summary>
    private void EnsurePageView()
    {
        switch (_viewModel.Page)
        {
            case ShellPage.Projects when ProjectsHost.Content is null:
                ProjectsHost.Content = new ProjectsView { DataContext = _viewModel.Projects };
                break;
            case ShellPage.Installed when InstalledHost.Content is null:
                InstalledHost.Content = new InstalledView { DataContext = _viewModel.Installed };
                break;
            case ShellPage.Blog when BlogHost.Content is null:
                BlogHost.Content = new BlogView { DataContext = _viewModel.Blog };
                _ = _viewModel.Blog.EnsureLoadedAsync();
                break;
            case ShellPage.Settings when SettingsHost.Content is null:
                SettingsHost.Content = new SettingsView { DataContext = _viewModel.SettingsPage };
                break;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainWindowViewModel.Page))
        {
            EnsurePageView();
        }
    }

    private void OnEditorLaunched(object? sender, Core.Models.InstalledVersion installation)
    {
        switch (_services.Settings.AfterLaunch)
        {
            case AfterLaunchBehavior.Hide:
                Hide();
                break;
            case AfterLaunchBehavior.Quit:
                ExitApplication();
                break;
            default:
                Activate();
                break;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximized();

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximized();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_viewModel.Dialog is not null)
        {
            _viewModel.Dialog.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (_viewModel.IsDownloadsOpen)
        {
            _viewModel.CloseDownloadsCommand.Execute(null);
            e.Handled = true;
        }
    }
}
