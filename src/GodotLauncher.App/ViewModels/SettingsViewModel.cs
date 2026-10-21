using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Settings;
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
    private string _savedText = string.Empty;

    public string VersionText => $"Godot Launcher {AppServices.AppVersion}";

    public string DataFolder => Services.Paths.DataDirectory;

    public string LicenseText => L.Settings.License;

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

    private void BuildOptions()
    {
        var theme = SelectedTheme?.Value ?? Services.Settings.Theme;
        var language = SelectedLanguage?.Value ?? Services.Settings.Language;
        var close = SelectedCloseBehavior?.Value ?? Services.Settings.CloseBehavior;
        var after = SelectedAfterLaunch?.Value ?? Services.Settings.AfterLaunch;
        var parallel = SelectedParallelDownloads?.Value ?? Services.Settings.ConcurrentDownloads;
        var refresh = SelectedRefreshInterval?.Value ?? Services.Settings.RefreshIntervalHours;

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
