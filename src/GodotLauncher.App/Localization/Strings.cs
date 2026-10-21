namespace GodotLauncher.App.Localization;

/// <summary>
/// All localized strings for one language. Instances are immutable snapshots: when the
/// language changes a new <see cref="Strings"/> tree is created and published, which keeps
/// data bound views in sync without re-loading any XAML.
/// </summary>
public sealed class Strings
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public Strings(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
        App = new AppStrings(values);
        Nav = new NavStrings(values);
        Explore = new ExploreStrings(values);
        Channel = new ChannelStrings(values);
        Edition = new EditionStrings(values);
        Version = new VersionStrings(values);
        Downloads = new DownloadsStrings(values);
        Installed = new InstalledStrings(values);
        Settings = new SettingsStrings(values);
        Tray = new TrayStrings(values);
        Dialog = new DialogStrings(values);
        Error = new ErrorStrings(values);
        Status = new StatusStrings(values);
        Banner = new BannerStrings(values);
        Common = new CommonStrings(values);
    }

    public AppStrings App { get; }

    public NavStrings Nav { get; }

    public ExploreStrings Explore { get; }

    public ChannelStrings Channel { get; }

    public EditionStrings Edition { get; }

    public VersionStrings Version { get; }

    public DownloadsStrings Downloads { get; }

    public InstalledStrings Installed { get; }

    public SettingsStrings Settings { get; }

    public TrayStrings Tray { get; }

    public DialogStrings Dialog { get; }

    public ErrorStrings Error { get; }

    public StatusStrings Status { get; }

    public BannerStrings Banner { get; }

    public CommonStrings Common { get; }

    /// <summary>Looks up a raw key; returns the key itself when missing.</summary>
    public string Get(string key) => _values.TryGetValue(key, out var value) ? value : key;
}

/// <summary>Base class for a group of keys sharing a prefix.</summary>
public abstract class StringGroup(IReadOnlyDictionary<string, string> values, string prefix)
{
    /// <summary>Returns the localized value for a key inside this group.</summary>
    protected string Get(string key) =>
        values.TryGetValue($"{prefix}.{key}", out var value) ? value : $"{prefix}.{key}";
}

public sealed class AppStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "App")
{
    public string Name => Get(nameof(Name));

    public string Tagline => Get(nameof(Tagline));
}

public sealed class NavStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Nav")
{
    public string Explore => Get(nameof(Explore));

    public string Installed => Get(nameof(Installed));

    public string Settings => Get(nameof(Settings));
}

public sealed class ExploreStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Explore")
{
    public string Title => Get(nameof(Title));

    public string Subtitle => Get(nameof(Subtitle));

    public string Search => Get(nameof(Search));

    public string ShowOlder => Get(nameof(ShowOlder));

    public string Refresh => Get(nameof(Refresh));

    public string Refreshing => Get(nameof(Refreshing));

    public string Loading => Get(nameof(Loading));

    public string Empty => Get(nameof(Empty));

    public string EmptyHint => Get(nameof(EmptyHint));

    public string Cached => Get(nameof(Cached));

    public string Offline => Get(nameof(Offline));

    public string Updated => Get(nameof(Updated));

    public string Count => Get(nameof(Count));

    public string AllChannels => Get(nameof(AllChannels));

    public string Edition => Get(nameof(Edition));

    public string Arch => Get(nameof(Arch));

    public string ClearFilters => Get(nameof(ClearFilters));
}

public sealed class ChannelStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Channel")
{
    public string Stable => Get(nameof(Stable));

    public string RC => Get(nameof(RC));

    public string Beta => Get(nameof(Beta));

    public string Dev => Get(nameof(Dev));
}

public sealed class EditionStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Edition")
{
    public string Standard => Get(nameof(Standard));

    public string Mono => Get(nameof(Mono));
}

public sealed class VersionStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Version")
{
    public string Download => Get(nameof(Download));

    public string Install => Get(nameof(Install));

    public string Launch => Get(nameof(Launch));

    public string New => Get(nameof(New));

    public string Installing => Get(nameof(Installing));

    public string Installed => Get(nameof(Installed));

    public string Released => Get(nameof(Released));

    public string Queued => Get(nameof(Queued));

    public string Downloading => Get(nameof(Downloading));

    public string Cancel => Get(nameof(Cancel));

    public string Resume => Get(nameof(Resume));
}

public sealed class DownloadsStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Downloads")
{
    public string Title => Get(nameof(Title));

    public string EmptyHint => Get(nameof(EmptyHint));

    public string Cancel => Get(nameof(Cancel));

    public string Retry => Get(nameof(Retry));

    public string Clear => Get(nameof(Clear));

    public string Completed => Get(nameof(Completed));

    public string Failed => Get(nameof(Failed));

    public string Cancelled => Get(nameof(Cancelled));

    public string Downloading => Get(nameof(Downloading));

    public string Verifying => Get(nameof(Verifying));

    public string Extracting => Get(nameof(Extracting));

    public string Preparing => Get(nameof(Preparing));

    public string Queued => Get(nameof(Queued));

    public string Of => Get(nameof(Of));
}

public sealed class InstalledStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Installed")
{
    public string Count => Get(nameof(Count));

    public string Title => Get(nameof(Title));

    public string Subtitle => Get(nameof(Subtitle));

    public string Register => Get(nameof(Register));

    public string Empty => Get(nameof(Empty));

    public string EmptyHint => Get(nameof(EmptyHint));

    public string Default => Get(nameof(Default));

    public string Pinned => Get(nameof(Pinned));

    public string SetDefault => Get(nameof(SetDefault));

    public string Pin => Get(nameof(Pin));

    public string Unpin => Get(nameof(Unpin));

    public string Rename => Get(nameof(Rename));

    public string Arguments => Get(nameof(Arguments));

    public string OpenFolder => Get(nameof(OpenFolder));

    public string Uninstall => Get(nameof(Uninstall));

    public string CleanPreviews => Get(nameof(CleanPreviews));

    public string CleanPreviewsDone => Get(nameof(CleanPreviewsDone));

    public string CleanPreviewsNone => Get(nameof(CleanPreviewsNone));

    public string LastUsed => Get(nameof(LastUsed));

    public string Never => Get(nameof(Never));

    public string Size => Get(nameof(Size));

    public string Path => Get(nameof(Path));

    public string SourceOfficial => Get(nameof(SourceOfficial));

    public string SourceGitHub => Get(nameof(SourceGitHub));

    public string SourceMirror => Get(nameof(SourceMirror));

    public string SourceLocalImport => Get(nameof(SourceLocalImport));

    public string Imported => Get(nameof(Imported));

    public string Launch => Get(nameof(Launch));
}

public sealed class SettingsStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Settings")
{
    public string Title => Get(nameof(Title));

    public string Subtitle => Get(nameof(Subtitle));

    public string Appearance => Get(nameof(Appearance));

    public string Theme => Get(nameof(Theme));

    public string ThemeSystem => Get(nameof(ThemeSystem));

    public string ThemeLight => Get(nameof(ThemeLight));

    public string ThemeDark => Get(nameof(ThemeDark));

    public string Language => Get(nameof(Language));

    public string SystemDefault => Get(nameof(SystemDefault));

    public string Behavior => Get(nameof(Behavior));

    public string CloseBehavior => Get(nameof(CloseBehavior));

    public string CloseTray => Get(nameof(CloseTray));

    public string CloseQuit => Get(nameof(CloseQuit));

    public string AfterLaunch => Get(nameof(AfterLaunch));

    public string AfterLaunchHide => Get(nameof(AfterLaunchHide));

    public string AfterLaunchKeep => Get(nameof(AfterLaunchKeep));

    public string AfterLaunchQuit => Get(nameof(AfterLaunchQuit));

    public string LaunchConsole => Get(nameof(LaunchConsole));

    public string Autostart => Get(nameof(Autostart));

    public string Downloads => Get(nameof(Downloads));

    public string InstallRoot => Get(nameof(InstallRoot));

    public string InstallRootHint => Get(nameof(InstallRootHint));

    public string Browse => Get(nameof(Browse));

    public string Concurrent => Get(nameof(Concurrent));

    public string Mirror => Get(nameof(Mirror));

    public string MirrorHint => Get(nameof(MirrorHint));

    public string Updates => Get(nameof(Updates));

    public string RefreshInterval => Get(nameof(RefreshInterval));

    public string Interval => Get(nameof(Interval));

    public string IntervalNever => Get(nameof(IntervalNever));

    public string ShowOlder => Get(nameof(ShowOlder));

    public string Notifications => Get(nameof(Notifications));

    public string NotificationsHint => Get(nameof(NotificationsHint));

    public string About => Get(nameof(About));

    public string Version => Get(nameof(Version));

    public string DataFolder => Get(nameof(DataFolder));

    public string Open => Get(nameof(Open));

    public string Logs => Get(nameof(Logs));

    public string License => Get(nameof(License));

    public string Saved => Get(nameof(Saved));

    public string FolderPicker => Get(nameof(FolderPicker));

    public string DataFolderPicker => Get(nameof(DataFolderPicker));
}

public sealed class TrayStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Tray")
{
    public string Show => Get(nameof(Show));

    public string LaunchDefault => Get(nameof(LaunchDefault));

    public string Recent => Get(nameof(Recent));

    public string Refresh => Get(nameof(Refresh));

    public string Settings => Get(nameof(Settings));

    public string Exit => Get(nameof(Exit));

    public string Tooltip => Get(nameof(Tooltip));

    public string NoDefault => Get(nameof(NoDefault));
}

public sealed class DialogStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Dialog")
{
    public string Ok => Get(nameof(Ok));

    public string Cancel => Get(nameof(Cancel));

    public string Delete => Get(nameof(Delete));

    public string Save => Get(nameof(Save));

    public string UninstallTitle => Get(nameof(UninstallTitle));

    public string UninstallBody => Get(nameof(UninstallBody));

    public string DeleteFiles => Get(nameof(DeleteFiles));

    public string AliasTitle => Get(nameof(AliasTitle));

    public string AliasLabel => Get(nameof(AliasLabel));

    public string ArgumentsTitle => Get(nameof(ArgumentsTitle));

    public string ArgumentsLabel => Get(nameof(ArgumentsLabel));

    public string ArgumentsHint => Get(nameof(ArgumentsHint));

    public string RegisterFailed => Get(nameof(RegisterFailed));

    public string ImportTitle => Get(nameof(ImportTitle));

    public string ImportFilter => Get(nameof(ImportFilter));

    public string ImportFailed => Get(nameof(ImportFailed));
}

public sealed class ErrorStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Error")
{
    public string Title => Get(nameof(Title));

    public string Catalog => Get(nameof(Catalog));

    public string Download => Get(nameof(Download));

    public string Install => Get(nameof(Install));

    public string Launch => Get(nameof(Launch));

    public string Import => Get(nameof(Import));

    public string Generic => Get(nameof(Generic));
}

public sealed class StatusStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Status")
{
    public string Ready => Get(nameof(Ready));

    public string Checking => Get(nameof(Checking));

    public string NewVersions => Get(nameof(NewVersions));

    public string UpToDate => Get(nameof(UpToDate));

    public string Offline => Get(nameof(Offline));
}

public sealed class BannerStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Banner")
{
    public string NewVersions => Get(nameof(NewVersions));

    public string NewVersionsHint => Get(nameof(NewVersionsHint));

    public string Cached => Get(nameof(Cached));

    public string Offline => Get(nameof(Offline));

    public string Dismiss => Get(nameof(Dismiss));
}

public sealed class CommonStrings(IReadOnlyDictionary<string, string> values) : StringGroup(values, "Common")
{
    public string Remove => Get(nameof(Remove));

    public string Close => Get(nameof(Close));

    public string Open => Get(nameof(Open));

    public string Yes => Get(nameof(Yes));

    public string No => Get(nameof(No));

    public string Browse => Get(nameof(Browse));
    public string Save => Get(nameof(Save));
}
