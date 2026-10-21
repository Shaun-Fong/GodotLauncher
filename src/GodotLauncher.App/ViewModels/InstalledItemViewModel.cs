using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Models;

namespace GodotLauncher.App.ViewModels;

/// <summary>One installed editor row with its management actions.</summary>
public sealed partial class InstalledItemViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly InstalledViewModel _owner;
    private InstalledVersion _installation;

    public InstalledItemViewModel(AppServices services, InstalledViewModel owner, InstalledVersion installation)
    {
        _services = services;
        _owner = owner;
        _installation = installation;
    }

    public InstalledVersion Installation => _installation;

    public string Title => _installation.Label;

    public string VersionText => _installation.DisplayName;

    public string MetaText =>
        $"{_installation.Edition.ToDisplayName()} · {_installation.Architecture.ToDisplayName()} · {SizeText}";

    public bool IsDefault => _installation.IsDefault;

    public bool IsPinned => _installation.Pinned;

    public string SizeText => DownloadProgress.FormatBytes(_installation.SizeOnDisk);

    public string PathText => _installation.InstallPath;

    public string SourceText => _installation.Source switch
    {
        InstallSource.Official => L.Installed.SourceOfficial,
        InstallSource.GitHub => L.Installed.SourceGitHub,
        InstallSource.Mirror => L.Installed.SourceMirror,
        _ => L.Installed.SourceLocalImport
    };

    public string LastUsedText => _installation.LastLaunched is { } used
        ? string.Format(L.Installed.LastUsed, used.LocalDateTime.ToString("g"))
        : string.Format(L.Installed.LastUsed, L.Installed.Never);

    public string PinText => IsPinned ? L.Installed.Unpin : L.Installed.Pin;

    public string LaunchText => L.Installed.Launch;

    public string SetDefaultText => L.Installed.SetDefault;

    public string RenameText => L.Installed.Rename;

    public string ArgumentsText => L.Installed.Arguments;

    public string OpenFolderText => L.Installed.OpenFolder;

    public string UninstallText => L.Installed.Uninstall;

    public string DefaultText => L.Installed.Default;

    public string PinnedText => L.Installed.Pinned;

    private Strings L => _services.Loc;

    [RelayCommand]
    private void Launch() => _owner.Launch(this);

    [RelayCommand]
    private Task SetDefault() => _owner.SetDefaultAsync(this);

    [RelayCommand]
    private Task TogglePin() => _owner.TogglePinAsync(this);

    [RelayCommand]
    private Task Rename() => _owner.RenameAsync(this);

    [RelayCommand]
    private Task EditArguments() => _owner.EditArgumentsAsync(this);

    [RelayCommand]
    private void OpenFolder() => _owner.OpenFolder(this);

    [RelayCommand]
    private Task Uninstall() => _owner.UninstallAsync(this);

    public void Update(InstalledVersion installation, bool isDefault)
    {
        _installation = installation with { IsDefault = isDefault };
        OnPropertyChanged(string.Empty);
    }
}
