using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Downloads;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

public enum ShellPage
{
    Projects,
    Explore,
    Installed,
    Settings
}

/// <summary>Shell view model: navigation, dialogs, status and the downloads drawer.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDialogHost
{
    private bool _bannerDismissed;

    public MainWindowViewModel(AppServices services)
        : base(services)
    {
        Projects = new ProjectsViewModel(services, this);
        Explore = new ExploreViewModel(services);
        Installed = new InstalledViewModel(services, this);
        Updates = new UpdateViewModel(services);
        SettingsPage = new SettingsViewModel(services, Updates);

        services.CatalogChanged += (_, _) => OnCatalogChanged();
        services.ProjectChanged += (_, _) => UpdateCounts();
        services.PropertyChanged += OnServicePropertyChanged;
        services.Downloads.Completed += OnDownloadCompleted;
        services.Downloads.PropertyChanged += OnDownloadsPropertyChanged;
        services.Downloads.Items.CollectionChanged += (_, _) => OnDownloadsCollectionChanged();

        // Every launch path (installed page, projects, tray, explore) ends in AppServices, so the
        // after-launch behaviour is driven from there instead of from each page.
        services.EditorLaunched += (_, installation) => EditorLaunched?.Invoke(this, installation);

        UpdateStatus();
        UpdateCounts();
    }

    /// <summary>Raised when an editor was started from the shell, e.g. the default editor.</summary>
    public event EventHandler<InstalledVersion>? EditorLaunched;

    public ProjectsViewModel Projects { get; }

    public ExploreViewModel Explore { get; }

    public InstalledViewModel Installed { get; }

    public SettingsViewModel SettingsPage { get; }

    /// <summary>Self-update state shared by the sidebar badge and the settings page.</summary>
    public UpdateViewModel Updates { get; }

    public ObservableCollection<DownloadItem> Downloads => Services.Downloads.Items;

    public string AppName => L.App.Name;

    public string Tagline => L.App.Tagline;

    public string VersionText => $"v{AppServices.AppVersion}";

    [ObservableProperty]
    private ShellPage _page = ShellPage.Projects;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private string _installedCountText = string.Empty;

    [ObservableProperty]
    private string _projectsCountText = string.Empty;

    [ObservableProperty]
    private string _downloadsCountText = string.Empty;

    [ObservableProperty]
    private bool _isDownloadsOpen;

    [ObservableProperty]
    private bool _hasDownloads;

    [ObservableProperty]
    private bool _isBannerVisible;

    [ObservableProperty]
    private string _bannerTitle = string.Empty;

    [ObservableProperty]
    private string _bannerDetail = string.Empty;

    [ObservableProperty]
    private DialogViewModel? _dialog;

    public bool IsProjectsSelected => Page == ShellPage.Projects;

    public bool IsExploreSelected => Page == ShellPage.Explore;

    public bool IsInstalledSelected => Page == ShellPage.Installed;

    public bool IsSettingsSelected => Page == ShellPage.Settings;

    public bool HasDialog => Dialog is not null;

    public bool HasInstalled => Installed.Items.Count > 0;

    public bool HasInstalledCount => Services.Library.Count > 0;

    public bool HasProjectsCount => Services.Projects.Count > 0;

    public bool HasDownloadsCount => Downloads.Count > 0;

    [RelayCommand]
    private void ShowProjects() => Page = ShellPage.Projects;

    [RelayCommand]
    private void ShowExplore() => Page = ShellPage.Explore;

    [RelayCommand]
    private void ShowInstalled() => Page = ShellPage.Installed;

    [RelayCommand]
    private void ShowSettings() => Page = ShellPage.Settings;

    [RelayCommand]
    private void ToggleDownloads() => IsDownloadsOpen = !IsDownloadsOpen;

    [RelayCommand]
    private void CloseDownloads() => IsDownloadsOpen = false;

    [RelayCommand]
    private void ClearFinishedDownloads() => Services.Downloads.ClearFinished();

    [RelayCommand]
    private void DismissBanner()
    {
        _bannerDismissed = true;
        IsBannerVisible = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await Services.RefreshCatalogAsync();
        }
        catch (Exception ex)
        {
            Services.Log.Error("Manual catalog refresh failed.", ex);
        }
        finally
        {
            UpdateStatus();
        }
    }

    /// <summary>Starts the default editor; used by the tray and the sidebar shortcut.</summary>
    public bool LaunchDefault()
    {
        var installation = Services.Library.FindDefault();
        if (installation is null)
        {
            _ = ShowDialogAsync(new DialogViewModel(
                L.Tray.LaunchDefault,
                L.Tray.NoDefault,
                L.Dialog.Ok,
                L.Dialog.Cancel,
                canCancel: false));
            return false;
        }

        return LaunchInstallation(installation);
    }

    public bool LaunchInstallation(InstalledVersion installation)
    {
        try
        {
            var result = Services.Launch(installation);
            Services.Log.Info($"Launched {installation.DisplayName} from the shell (pid {result.ProcessId}).");
            Installed.ReloadFromLibrary();
            EditorLaunched?.Invoke(this, installation);
            return true;
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not launch {installation.DisplayName}.", ex);
            _ = ShowDialogAsync(new DialogViewModel(
                L.Error.Launch,
                ex.Message,
                L.Dialog.Ok,
                L.Dialog.Cancel,
                canCancel: false));
            return false;
        }
    }

    public async Task<DialogResult> ShowDialogAsync(DialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        Dialog = dialog;
        try
        {
            return await dialog.Completion;
        }
        finally
        {
            if (ReferenceEquals(Dialog, dialog))
            {
                Dialog = null;
            }
        }
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AppName));
        OnPropertyChanged(nameof(Tagline));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(NetworkIssue));
        OnPropertyChanged(nameof(NetworkIssueTitle));
        OnPropertyChanged(nameof(NetworkIssueAction));
        UpdateStatus();
        UpdateCounts();
        OnCatalogChanged();
    }

    protected override void OnSettingsChanged()
    {
        UpdateBanner();
    }

    partial void OnPageChanged(ShellPage value)
    {
        OnPropertyChanged(nameof(IsProjectsSelected));
        OnPropertyChanged(nameof(IsExploreSelected));
        OnPropertyChanged(nameof(IsInstalledSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));

        if (value == ShellPage.Installed)
        {
            Installed.ReloadFromLibrary();
        }
    }

    partial void OnDialogChanged(DialogViewModel? value) => OnPropertyChanged(nameof(HasDialog));

    /// <summary>True when the last network operation failed and the hint bar is useful.</summary>
    public bool HasNetworkIssue => Services.HasNetworkIssue;

    /// <summary>Localized explanation; the raw error text is offered as a tooltip.</summary>
    public string NetworkIssue => L.Banner.NetworkHint;

    public string NetworkIssueDetail => Services.NetworkIssue ?? string.Empty;

    public string NetworkIssueTitle => L.Banner.NetworkTitle;

    public string NetworkIssueAction => L.Banner.NetworkAction;

    [RelayCommand]
    private void OpenNetworkSettings() => Page = ShellPage.Settings;

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AppServices.IsRefreshing) or nameof(AppServices.Catalog))
        {
            UpdateStatus();
        }

        if (args.PropertyName is nameof(AppServices.NetworkIssue) or nameof(AppServices.HasNetworkIssue))
        {
            OnPropertyChanged(nameof(HasNetworkIssue));
            OnPropertyChanged(nameof(NetworkIssue));
        }
    }

    private void OnCatalogChanged()
    {
        OnPropertyChanged(nameof(DownloadProgressText));
        UpdateStatus();
        UpdateCounts();
        UpdateBanner();
    }

    private void UpdateBanner()
    {
        if (_bannerDismissed)
        {
            return;
        }

        var count = CountNotifiableNewVersions();
        if (count == 0)
        {
            IsBannerVisible = false;
            return;
        }

        BannerTitle = string.Format(CultureInfo.CurrentCulture, L.Banner.NewVersions, count);
        BannerDetail = L.Banner.NewVersionsHint;
        IsBannerVisible = true;
    }

    /// <summary>
    /// Counts releases that appeared since the previous refresh for channels the user asked to be
    /// notified about. Notification channels are off by default, so a first run stays quiet.
    /// </summary>
    private int CountNotifiableNewVersions()
    {
        var catalog = Services.Catalog;
        if (catalog is null || Services.NewVersionIds.Count == 0)
        {
            return 0;
        }

        var settings = Services.Settings;
        var count = 0;
        foreach (var version in catalog.Versions)
        {
            if (!Services.NewVersionIds.Contains(version.Id))
            {
                continue;
            }

            var enabled = version.Channel switch
            {
                ReleaseChannel.Stable => settings.NotifyStable,
                ReleaseChannel.Rc => settings.NotifyRc,
                ReleaseChannel.Beta => settings.NotifyBeta,
                _ => settings.NotifyDev,
            };

            if (enabled)
            {
                count++;
            }
        }

        return count;
    }

    private void OnDownloadCompleted(DownloadItem item)
    {
        UpdateCounts();
        Services.Log.Info($"Download finished: {item.DisplayName} ({item.MetaText}).");
    }

    private void OnDownloadsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DownloadManager.ActiveCount) or nameof(DownloadManager.HasActive))
        {
            UpdateCounts();
        }
    }

    private void OnDownloadsCollectionChanged()
    {
        UpdateCounts();
        OnPropertyChanged(nameof(HasDownloads));
    }

    /// <summary>Progress summary shown on the downloads button.</summary>
    public string DownloadProgressText
    {
        get
        {
            var active = Downloads.Where(item => item.IsActive).ToArray();
            if (active.Length == 0)
            {
                return string.Empty;
            }

            var fraction = active.Average(item => item.Progress);
            return $"{Math.Round(fraction * 100)}%";
        }
    }

    private void UpdateCounts()
    {
        InstalledCountText = Services.Library.Count.ToString(CultureInfo.CurrentCulture);
        ProjectsCountText = Services.Projects.Count.ToString(CultureInfo.CurrentCulture);
        HasDownloads = Downloads.Count > 0;
        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(HasInstalledCount));
        OnPropertyChanged(nameof(HasProjectsCount));
        OnPropertyChanged(nameof(HasDownloadsCount));
        OnPropertyChanged(nameof(DownloadProgressText));

        var active = Downloads.Count(item => item.IsActive);
        DownloadsCountText = active > 0
            ? string.Format(CultureInfo.CurrentCulture, L.Downloads.Of, active, Downloads.Count)
            : string.Empty;
    }

    private void UpdateStatus()
    {
        if (Services.IsRefreshing)
        {
            StatusText = L.Status.Checking;
        }
        else
        {
            StatusText = Services.Catalog is null ? L.Status.Offline : L.Status.Ready;
        }

        var catalog = Services.Catalog;
        if (catalog is null)
        {
            StatusDetail = L.Explore.Offline;
            return;
        }

        StatusDetail = string.Format(
            CultureInfo.CurrentCulture,
            L.Explore.Updated,
            catalog.FetchedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
    }
}
