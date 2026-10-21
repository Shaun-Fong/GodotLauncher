using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Downloads;
using GodotLauncher.App.Localization;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Models;

namespace GodotLauncher.App.ViewModels;

/// <summary>One row of the explore list: a catalog release with its download state.</summary>
public sealed partial class VersionCardViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Func<GodotEdition> _edition;
    private readonly Func<CpuArchitecture> _architecture;
    private readonly Action<VersionCardViewModel> _download;
    private readonly Action<VersionCardViewModel> _launch;

    public VersionCardViewModel(
        AppServices services,
        GodotVersion version,
        bool isNew,
        Func<GodotEdition> edition,
        Func<CpuArchitecture> architecture,
        Action<VersionCardViewModel> download,
        Action<VersionCardViewModel> launch)
    {
        _services = services;
        _edition = edition;
        _architecture = architecture;
        _download = download;
        _launch = launch;
        Version = version;
        IsNew = isNew;
        RefreshState();
    }

    public GodotVersion Version { get; }

    public string Id => Version.Id;

    public string Title => Version.DisplayName;

    public string DateText => Version.ReleaseDateText;

    public bool IsNew { get; }

    public bool IsStable => Version.Channel == ReleaseChannel.Stable;

    public bool IsRc => Version.Channel == ReleaseChannel.Rc;

    public bool IsBeta => Version.Channel == ReleaseChannel.Beta;

    public bool IsDev => Version.Channel == ReleaseChannel.Dev;

    public string ChannelText => Version.Channel.ToDisplayName();

    public string MetaText => $"{Version.Number} · {ChannelText} · {DateText}";

    public InstalledVersion? Installed => _services.Library.FindByVersion(Version.Id, _edition());

    public bool IsInstalled => Installed is not null;

    public DownloadItem? ActiveDownload => _services.Downloads.Items.FirstOrDefault(item =>
        item.IsActive &&
        item.VersionId == Version.Id &&
        item.Edition == _edition() &&
        item.Architecture == _architecture());

    public bool IsBusy => ActiveDownload is not null;

    public bool CanDownload => !IsInstalled && !IsBusy;

    public string ActionText => IsInstalled
        ? L.Version.Launch
        : IsBusy
            ? L.Version.Queued
            : L.Version.Download;

    /// <summary>Localized label of the "new release" badge.</summary>
    public string NewText => L.Version.New;

    private Strings L => _services.Loc;

    [RelayCommand]
    private void Primary()
    {
        if (IsInstalled)
        {
            _launch(this);
            return;
        }

        if (!IsBusy)
        {
            _download(this);
        }
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(Installed));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(ActiveDownload));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(ChannelText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(NewText));
        PrimaryCommand.NotifyCanExecuteChanged();
    }
}
