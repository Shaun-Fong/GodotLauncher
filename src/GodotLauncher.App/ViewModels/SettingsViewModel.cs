using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Updates;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

/// <summary>Settings page: every option is written back to config.toml immediately.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private bool _loading;

    public SettingsViewModel(AppServices services)
        : base(services)
    {
        Themes = [];
        Languages = [];
        CloseBehaviors = [];
        AfterLaunchBehaviors = [];
        ProxyModes = [];
        ParallelDownloadOptions = [];
        RefreshIntervals = [];
        BuildOptions();
        Load();
    }

    public ObservableCollection<LabeledOption<ThemeMode>> Themes { get; }

    public ObservableCollection<LabeledOption<string>> Languages { get; }

    public ObservableCollection<LabeledOption<CloseBehavior>> CloseBehaviors { get; }

    public ObservableCollection<LabeledOption<AfterLaunchBehavior>> AfterLaunchBehaviors { get; }

    public ObservableCollection<LabeledOption<int>> ParallelDownloadOptions { get; }

    public ObservableCollection<LabeledOption<int>> RefreshIntervals { get; }

    public ObservableCollection<LabeledOption<ProxyMode>> ProxyModes { get; }

    [ObservableProperty]
    private LabeledOption<ThemeMode>? _selectedTheme;

    [ObservableProperty]
    private LabeledOption<string>? _selectedLanguage;

    [ObservableProperty]
    private LabeledOption<CloseBehavior>? _selectedCloseBehavior;

    [ObservableProperty]
    private LabeledOption<AfterLaunchBehavior>? _selectedAfterLaunch;

    [ObservableProperty]
    private LabeledOption<int>? _selectedParallelDownloads;

    [ObservableProperty]
    private LabeledOption<int>? _selectedRefreshInterval;

    [ObservableProperty]
    private bool _launchWithConsole;

    [ObservableProperty]
    private bool _autostart;

    [ObservableProperty]
    private bool _showOlderVersions;

    [ObservableProperty]
    private bool _notifyStable;

    [ObservableProperty]
    private bool _notifyRc;

    [ObservableProperty]
    private bool _notifyBeta;

    [ObservableProperty]
    private bool _notifyDev;

    [ObservableProperty]
    private string _installRoot = string.Empty;

    [ObservableProperty]
    private string _mirrorTemplate = string.Empty;

    [ObservableProperty]
    private LabeledOption<ProxyMode>? _selectedProxy;

    [ObservableProperty]
    private string _proxyUrl = string.Empty;

    [ObservableProperty]
    private string _savedText = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _hasUpdate;

    public string VersionText => $"Godot Launcher {AppServices.AppVersion}";

    public string DataFolder => Services.Paths.DataDirectory;

    public string LicenseText => L.Settings.License;

    public string RepositoryUrl => "https://github.com/Shaun-Fong/GodotLauncher";

    public bool AutostartSupported => AutostartService.IsSupported;

    protected override void OnLanguageChanged()
    {
        BuildOptions();
        Load();
    }

    protected override void OnSettingsChanged()
    {
        if (_loading)
        {
            return;
        }

        Load();
    }

    partial void OnSelectedThemeChanged(LabeledOption<ThemeMode>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { Theme = value.Value });
    }

    partial void OnSelectedLanguageChanged(LabeledOption<string>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { Language = value.Value });
    }

    partial void OnSelectedCloseBehaviorChanged(LabeledOption<CloseBehavior>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { CloseBehavior = value.Value });
    }

    partial void OnSelectedAfterLaunchChanged(LabeledOption<AfterLaunchBehavior>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { AfterLaunch = value.Value });
    }

    partial void OnSelectedParallelDownloadsChanged(LabeledOption<int>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { ConcurrentDownloads = value.Value });
    }

    partial void OnSelectedRefreshIntervalChanged(LabeledOption<int>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { RefreshIntervalHours = value.Value });
    }

    partial void OnSelectedProxyChanged(LabeledOption<ProxyMode>? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Apply(settings => settings with { Proxy = value.Value });
    }

    partial void OnLaunchWithConsoleChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { LaunchWithConsole = value });
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { Autostart = value });
    }

    partial void OnShowOlderVersionsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { ShowOlderVersions = value });
    }

    partial void OnNotifyStableChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { NotifyStable = value });
    }

    partial void OnNotifyRcChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { NotifyRc = value });
    }

    partial void OnNotifyBetaChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { NotifyBeta = value });
    }

    partial void OnNotifyDevChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { NotifyDev = value });
    }

    [RelayCommand]
    private void SaveInstallRoot()
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { InstallRoot = InstallRoot.Trim() });
    }

    [RelayCommand]
    private void SaveMirrorTemplate()
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { MirrorTemplate = MirrorTemplate.Trim() });
    }

    [RelayCommand]
    private void SaveProxyUrl()
    {
        if (_loading)
        {
            return;
        }

        Apply(settings => settings with { ProxyUrl = ProxyUrl.Trim() });
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatus = L.Settings.CheckingUpdates;
        try
        {
            var release = await Services.CheckForUpdatesAsync();
            HasUpdate = release is not null;
            if (release is null)
            {
                UpdateStatus = L.Settings.UpToDate;
            }
            else
            {
                UpdateStatus = string.Format(CultureInfo.CurrentCulture, L.Settings.UpdateAvailable, release.Tag);
            }
        }
        catch (Exception exception)
        {
            Services.Log.Warn($"Could not check for launcher updates: {exception.Message}");
            HasUpdate = false;
            UpdateStatus = L.Settings.UpdateCheckFailed;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        var release = Services.AvailableUpdate;
        var asset = release is null ? null : PickUpdateAsset(release);
        if (asset is null)
        {
            return;
        }

        UpdateStatus = string.Format(CultureInfo.CurrentCulture, L.Settings.UpdateAvailable, release!.Tag);
        try
        {
            var path = await Services.DownloadUpdateAsync(asset);
            UpdateStatus = string.Format(CultureInfo.CurrentCulture, L.Settings.UpdateDownloaded, path);
            HasUpdate = false;
        }
        catch (Exception exception)
        {
            Services.Log.Error("Could not download the launcher update.", exception);
            UpdateStatus = L.Settings.UpdateCheckFailed;
        }
    }

    private static LauncherReleaseAsset? PickUpdateAsset(LauncherRelease release)
    {
        var os = GodotLauncher.Core.Platform.HostPlatformExtensions.Current;
        var arch = GodotLauncher.Core.Platform.HostArchitecture.Current;
        var tokens = os switch
        {
            GodotLauncher.Core.Platform.HostPlatform.Windows => new[] { "win-x64", "win-arm64", "win" },
            GodotLauncher.Core.Platform.HostPlatform.MacOS => arch == GodotLauncher.Core.Models.CpuArchitecture.Arm64
                ? new[] { "osx-arm64", "osx-x64" }
                : new[] { "osx-x64", "osx-arm64" },
            _ => new[] { "linux-x64", "linux-arm64", "linux" },
        };

        foreach (var token in tokens)
        {
            var match = release.Assets.FirstOrDefault(asset =>
                asset.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task BrowseInstallRootAsync()
    {
        var folder = await FilePickers.PickFolderAsync(L.Settings.FolderPicker);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        InstallRoot = folder;
        Apply(settings => settings with { InstallRoot = folder });
    }

    [RelayCommand]
    private void OpenDataFolder() => Services.OpenExternally(Services.Paths.DataDirectory);

    [RelayCommand]
    private void OpenLogs() => Services.OpenExternally(Services.Paths.LogDirectory);

    [RelayCommand]
    private void OpenRepository() => Services.OpenExternally(RepositoryUrl);

    private void BuildOptions()
    {
        var theme = SelectedTheme?.Value ?? Services.Settings.Theme;
        var language = SelectedLanguage?.Value ?? Services.Settings.Language;
        var close = SelectedCloseBehavior?.Value ?? Services.Settings.CloseBehavior;
        var after = SelectedAfterLaunch?.Value ?? Services.Settings.AfterLaunch;
        var parallel = SelectedParallelDownloads?.Value ?? Services.Settings.ConcurrentDownloads;
        var refresh = SelectedRefreshInterval?.Value ?? Services.Settings.RefreshIntervalHours;
        var proxy = SelectedProxy?.Value ?? Services.Settings.Proxy;

        Themes.Clear();
        Themes.Add(new LabeledOption<ThemeMode>(ThemeMode.System, L.Settings.ThemeSystem));
        Themes.Add(new LabeledOption<ThemeMode>(ThemeMode.Light, L.Settings.ThemeLight));
        Themes.Add(new LabeledOption<ThemeMode>(ThemeMode.Dark, L.Settings.ThemeDark));

        Languages.Clear();
        Languages.Add(new LabeledOption<string>(LocalizationService.SystemLanguage, L.Settings.SystemDefault));
        foreach (var option in Services.Languages)
        {
            Languages.Add(new LabeledOption<string>(option.Code, option.DisplayName));
        }

        CloseBehaviors.Clear();
        CloseBehaviors.Add(new LabeledOption<CloseBehavior>(CloseBehavior.Tray, L.Settings.CloseTray));
        CloseBehaviors.Add(new LabeledOption<CloseBehavior>(CloseBehavior.Quit, L.Settings.CloseQuit));

        AfterLaunchBehaviors.Clear();
        AfterLaunchBehaviors.Add(new LabeledOption<AfterLaunchBehavior>(AfterLaunchBehavior.Hide, L.Settings.AfterLaunchHide));
        AfterLaunchBehaviors.Add(new LabeledOption<AfterLaunchBehavior>(AfterLaunchBehavior.Keep, L.Settings.AfterLaunchKeep));
        AfterLaunchBehaviors.Add(new LabeledOption<AfterLaunchBehavior>(AfterLaunchBehavior.Quit, L.Settings.AfterLaunchQuit));

        ProxyModes.Clear();
        ProxyModes.Add(new LabeledOption<ProxyMode>(ProxyMode.System, L.Settings.ProxySystem));
        ProxyModes.Add(new LabeledOption<ProxyMode>(ProxyMode.None, L.Settings.ProxyNone));
        ProxyModes.Add(new LabeledOption<ProxyMode>(ProxyMode.Custom, L.Settings.ProxyCustom));

        ParallelDownloadOptions.Clear();
        for (var count = 1; count <= 4; count++)
        {
            ParallelDownloadOptions.Add(new LabeledOption<int>(count, count.ToString(CultureInfo.CurrentCulture)));
        }

        RefreshIntervals.Clear();
        RefreshIntervals.Add(new LabeledOption<int>(0, L.Settings.IntervalNever));
        foreach (var hours in new[] { 1, 3, 6, 12, 24, 72 })
        {
            RefreshIntervals.Add(new LabeledOption<int>(
                hours,
                string.Format(CultureInfo.CurrentCulture, L.Settings.Interval, hours)));
        }

        _loading = true;
        SelectedTheme = Themes.FirstOrDefault(option => option.Value == theme) ?? Themes[0];
        SelectedLanguage = Languages.FirstOrDefault(option => option.Value == language) ?? Languages[0];
        SelectedCloseBehavior = CloseBehaviors.FirstOrDefault(option => option.Value == close) ?? CloseBehaviors[0];
        SelectedAfterLaunch = AfterLaunchBehaviors.FirstOrDefault(option => option.Value == after) ?? AfterLaunchBehaviors[0];
        SelectedParallelDownloads = ParallelDownloadOptions.FirstOrDefault(option => option.Value == parallel) ?? ParallelDownloadOptions[0];
        SelectedRefreshInterval = RefreshIntervals.FirstOrDefault(option => option.Value == refresh) ?? RefreshIntervals[0];
        SelectedProxy = ProxyModes.FirstOrDefault(option => option.Value == proxy) ?? ProxyModes[0];
        _loading = false;
    }

    private void Load()
    {
        _loading = true;
        var settings = Services.Settings;
        LaunchWithConsole = settings.LaunchWithConsole;
        Autostart = settings.Autostart;
        ShowOlderVersions = settings.ShowOlderVersions;
        NotifyStable = settings.NotifyStable;
        NotifyRc = settings.NotifyRc;
        NotifyBeta = settings.NotifyBeta;
        NotifyDev = settings.NotifyDev;
        InstallRoot = settings.InstallRoot;
        MirrorTemplate = settings.MirrorTemplate;
        ProxyUrl = settings.ProxyUrl;
        _loading = false;
    }

    private void Apply(Func<LauncherSettings, LauncherSettings> change)
    {
        Services.UpdateSettings(change);
        SavedText = L.Settings.Saved;
    }

    [RelayCommand]
    private void MarkSaved() => SavedText = L.Settings.Saved;
}
