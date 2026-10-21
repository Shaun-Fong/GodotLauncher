using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Downloads;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

public enum ChannelFilter
{
    All,
    Stable,
    Rc,
    Beta,
    Dev
}

/// <summary>An item for a combo box that displays a label but carries a typed value.</summary>
public sealed record LabeledOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Explore page: filters the catalog, starts downloads and launches installed editors.</summary>
public sealed partial class ExploreViewModel : ViewModelBase
{
    private readonly Dictionary<string, VersionCardViewModel> _cards = new(StringComparer.Ordinal);
    private bool _lastRefreshFailed;
    private bool _suppressRebuild;
    private bool _activated;

    public ExploreViewModel(AppServices services)
        : base(services)
    {
        Channels = [];
        Editions = [];
        Architectures = [];
        SelectedEdition = new LabeledOption<GodotEdition>(GodotEdition.Standard, string.Empty);
        SelectedArchitecture = new LabeledOption<CpuArchitecture>(HostArchitecture.Current, string.Empty);
        BuildOptions();

        services.CatalogChanged += (_, _) => OnCatalogChanged();
        services.PropertyChanged += OnServicePropertyChanged;
        services.Downloads.Items.CollectionChanged += (_, _) => RefreshCardStates();

        foreach (var item in services.Downloads.Items)
        {
            item.PropertyChanged += OnDownloadItemChanged;
        }

        services.Downloads.Items.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
            {
                foreach (DownloadItem item in args.NewItems)
                {
                    item.PropertyChanged += OnDownloadItemChanged;
                }
            }
        };

        OnLanguageChanged();
    }

    /// <summary>
    /// Fills the list after the shell is on screen. Building every card up front costs
    /// hundreds of milliseconds on cold start, which the speed budget does not allow.
    /// </summary>
    public void Activate()
    {
        if (_activated)
        {
            return;
        }

        _activated = true;
        Rebuild();
    }

    public ObservableCollection<VersionCardViewModel> Versions { get; } = [];

    public ObservableCollection<LabeledOption<ChannelFilter>> Channels { get; }

    public ObservableCollection<LabeledOption<GodotEdition>> Editions { get; }

    public ObservableCollection<LabeledOption<CpuArchitecture>> Architectures { get; }

    [ObservableProperty]
    private LabeledOption<ChannelFilter>? _selectedChannel;

    [ObservableProperty]
    private LabeledOption<GodotEdition> _selectedEdition;

    [ObservableProperty]
    private LabeledOption<CpuArchitecture> _selectedArchitecture;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _updatedText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Mirrors the persisted preference; toggling it updates the settings file.</summary>
    public bool ShowOlderVersions
    {
        get => Services.Settings.ShowOlderVersions;
        set
        {
            if (value == Services.Settings.ShowOlderVersions)
            {
                return;
            }

            _suppressRebuild = true;
            Services.UpdateSettings(settings => settings with { ShowOlderVersions = value });
            _suppressRebuild = false;
            OnPropertyChanged();
            Rebuild();
        }
    }

    public bool HasVersions => Versions.Count > 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var result = await Services.RefreshCatalogAsync(null);
            _lastRefreshFailed = result is null ||
                result.Status is CatalogRefreshStatus.Failed or CatalogRefreshStatus.Rejected;
        }
        catch (OperationCanceledException)
        {
            // The window is closing; nothing to report.
        }
        catch (Exception ex)
        {
            _lastRefreshFailed = true;
            Services.Log.Error("Catalog refresh from the explore page failed.", ex);
        }
        finally
        {
            UpdateStatus();
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedChannel = Channels.FirstOrDefault();
        SelectedEdition = Editions.FirstOrDefault(option => option.Value == GodotEdition.Standard) ?? Editions[0];
        SelectedArchitecture = Architectures.FirstOrDefault(option => option.Value == HostArchitecture.Current) ?? Architectures[0];
        Rebuild();
    }

    partial void OnSelectedChannelChanged(LabeledOption<ChannelFilter>? value) => Rebuild();

    partial void OnSelectedEditionChanged(LabeledOption<GodotEdition> value) => RefreshCardStates();

    partial void OnSelectedArchitectureChanged(LabeledOption<CpuArchitecture> value) => RefreshCardStates();

    partial void OnSearchTextChanged(string value) => Rebuild();

    protected override void OnLanguageChanged()
    {
        BuildOptions();
        foreach (var card in _cards.Values)
        {
            card.RefreshState();
        }

        if (_activated)
        {
            Rebuild();
        }
    }

    protected override void OnInstallationsChanged() => RefreshCardStates();

    private void BuildOptions()
    {
        var selectedChannel = SelectedChannel?.Value ?? ChannelFilter.All;
        var selectedEdition = SelectedEdition?.Value ?? GodotEdition.Standard;
        var selectedArch = SelectedArchitecture?.Value ?? HostArchitecture.Current;

        Channels.Clear();
        Channels.Add(new LabeledOption<ChannelFilter>(ChannelFilter.All, L.Explore.AllChannels));
        Channels.Add(new LabeledOption<ChannelFilter>(ChannelFilter.Stable, L.Channel.Stable));
        Channels.Add(new LabeledOption<ChannelFilter>(ChannelFilter.Rc, L.Channel.RC));
        Channels.Add(new LabeledOption<ChannelFilter>(ChannelFilter.Beta, L.Channel.Beta));
        Channels.Add(new LabeledOption<ChannelFilter>(ChannelFilter.Dev, L.Channel.Dev));

        Editions.Clear();
        Editions.Add(new LabeledOption<GodotEdition>(GodotEdition.Standard, L.Edition.Standard));
        Editions.Add(new LabeledOption<GodotEdition>(GodotEdition.Mono, L.Edition.Mono));

        Architectures.Clear();
        foreach (var architecture in new[]
                 {
                     CpuArchitecture.X64, CpuArchitecture.X86, CpuArchitecture.Arm64, CpuArchitecture.Arm32
                 })
        {
            Architectures.Add(new LabeledOption<CpuArchitecture>(architecture, architecture.ToDisplayName()));
        }

        _suppressRebuild = true;
        SelectedChannel = Channels.FirstOrDefault(option => option.Value == selectedChannel) ?? Channels[0];
        SelectedEdition = Editions.FirstOrDefault(option => option.Value == selectedEdition) ?? Editions[0];
        SelectedArchitecture = Architectures.FirstOrDefault(option => option.Value == selectedArch) ?? Architectures[0];
        _suppressRebuild = false;
    }

    private void OnCatalogChanged()
    {
        _lastRefreshFailed = false;
        Rebuild();
        UpdateStatus();
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AppServices.IsRefreshing))
        {
            IsRefreshing = Services.IsRefreshing;
            UpdateStatus();
        }
    }

    private void OnDownloadItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DownloadItem.State))
        {
            RefreshCardStates();
        }
    }

    private void Rebuild()
    {
        if (_suppressRebuild)
        {
            return;
        }

        var catalog = Services.Catalog;
        Versions.Clear();

        if (catalog is not null)
        {
            var query = SearchText.Trim();
            var channel = SelectedChannel?.Value ?? ChannelFilter.All;
            var showOlder = Services.Settings.ShowOlderVersions;

            foreach (var version in catalog.Versions)
            {
                if (!Matches(version, query, channel, showOlder))
                {
                    continue;
                }

                if (!_cards.TryGetValue(version.Id, out var card))
                {
                    card = new VersionCardViewModel(
                        Services,
                        version,
                        Services.NewVersionIds.Contains(version.Id),
                        () => SelectedEdition.Value,
                        () => SelectedArchitecture.Value,
                        Download,
                        Launch);
                    _cards[version.Id] = card;
                }

                Versions.Add(card);
            }
        }

        IsEmpty = Versions.Count == 0;
        OnPropertyChanged(nameof(HasVersions));
        CountText = string.Format(CultureInfo.CurrentCulture, L.Explore.Count, Versions.Count);
        UpdateStatus();
    }

    private static bool Matches(GodotVersion version, string query, ChannelFilter channel, bool showOlder)
    {
        if (!showOlder && IsOlder(version))
        {
            return false;
        }

        var matchesChannel = channel switch
        {
            ChannelFilter.Stable => version.Channel == ReleaseChannel.Stable,
            ChannelFilter.Rc => version.Channel == ReleaseChannel.Rc,
            ChannelFilter.Beta => version.Channel == ReleaseChannel.Beta,
            ChannelFilter.Dev => version.Channel == ReleaseChannel.Dev,
            _ => true
        };
        if (!matchesChannel)
        {
            return false;
        }

        if (query.Length == 0)
        {
            return true;
        }

        return version.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               version.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               version.Number.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOlder(GodotVersion version) =>
        version.Number.Major < 3 ||
        (version.Number.Major == 3 && version.Channel != ReleaseChannel.Stable);

    private void UpdateStatus()
    {
        var catalog = Services.Catalog;
        if (Services.IsRefreshing)
        {
            StatusText = L.Status.Checking;
            return;
        }

        if (catalog is null)
        {
            StatusText = _lastRefreshFailed ? L.Status.Offline : L.Status.Ready;
            UpdatedText = string.Empty;
            return;
        }

        UpdatedText = string.Format(
            CultureInfo.CurrentCulture,
            L.Explore.Updated,
            catalog.FetchedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
        if (catalog.IsFromCache)
        {
            StatusText = L.Explore.Cached;
        }
        else if (Services.NewVersionIds.Count > 0)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, L.Status.NewVersions, Services.NewVersionIds.Count);
        }
        else
        {
            StatusText = L.Status.UpToDate;
        }
    }

    private void RefreshCardStates()
    {
        foreach (var card in _cards.Values)
        {
            card.RefreshState();
        }
    }

    private void Download(VersionCardViewModel card)
    {
        var edition = SelectedEdition.Value;
        var architecture = SelectedArchitecture.Value;
        Services.Downloads.Enqueue(card.Version, edition, architecture);
        card.RefreshState();
    }

    private void Launch(VersionCardViewModel card)
    {
        var installed = Services.Library.FindByVersion(card.Version.Id, SelectedEdition.Value);
        if (installed is null)
        {
            return;
        }

        try
        {
            Services.Launch(installed);
        }
        catch (Exception ex)
        {
            Services.Log.Error($"Could not launch {installed.DisplayName}.", ex);
        }
    }
}
