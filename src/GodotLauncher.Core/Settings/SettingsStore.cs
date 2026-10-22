using System.Text;

namespace GodotLauncher.Core.Settings;

/// <summary>Reads and writes <c>config.toml</c>.</summary>
public sealed class SettingsStore
{
    private const string GeneralSection = "general";
    private const string DownloadsSection = "downloads";
    private const string UpdatesSection = "updates";
    private const string NotificationsSection = "notifications";
    private const string NetworkSection = "network";

    private readonly string _filePath;

    /// <summary>Creates the store at the given file path.</summary>
    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>Path of the configuration file.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Loads the configuration. A corrupt file is renamed to
    /// <c>config.toml.corrupt-{timestamp}</c> and defaults are returned, so a bad
    /// edit can never block startup.
    /// </summary>
    public LauncherSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return LauncherSettings.Default;
        }

        try
        {
            return Deserialize(File.ReadAllText(_filePath)).Normalize();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            TryBackupCorruptFile();
            return LauncherSettings.Default;
        }
    }

    /// <summary>Writes the configuration atomically.</summary>
    public void Save(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Storage.AtomicFile.WriteAllText(_filePath, Serialize(settings.Normalize()));
    }

    /// <summary>Serializes settings into the TOML layout used by the launcher.</summary>
    public static string Serialize(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = new StringBuilder();
        builder.AppendLine("# Godot Launcher configuration. Safe to edit by hand.");
        builder.AppendLine();

        builder.AppendLine($"[{GeneralSection}]");
        builder.AppendLine($"language = {TomlDocument.Quote(settings.Language)}");
        builder.AppendLine($"theme = {TomlDocument.Quote(settings.Theme.ToString().ToLowerInvariant())}");
        builder.AppendLine($"close_behavior = {TomlDocument.Quote(settings.CloseBehavior.ToString().ToLowerInvariant())}");
        builder.AppendLine($"after_launch = {TomlDocument.Quote(settings.AfterLaunch.ToString().ToLowerInvariant())}");
        builder.AppendLine($"launch_with_console = {Boolean(settings.LaunchWithConsole)}");
        builder.AppendLine($"autostart = {Boolean(settings.Autostart)}");
        builder.AppendLine();

        builder.AppendLine($"[{DownloadsSection}]");
        builder.AppendLine($"install_root = {TomlDocument.Quote(settings.InstallRoot)}");
        builder.AppendLine($"concurrent_downloads = {settings.ConcurrentDownloads.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        builder.AppendLine();

        builder.AppendLine($"[{NetworkSection}]");
        builder.AppendLine($"proxy = {TomlDocument.Quote(settings.Proxy.ToString().ToLowerInvariant())}");
        builder.AppendLine($"proxy_url = {TomlDocument.Quote(settings.ProxyUrl)}");
        builder.AppendLine();

        builder.AppendLine($"[{UpdatesSection}]");
        builder.AppendLine($"refresh_interval_hours = {settings.RefreshIntervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        builder.AppendLine($"show_older_versions = {Boolean(settings.ShowOlderVersions)}");
        builder.AppendLine();

        builder.AppendLine($"[{NotificationsSection}]");
        builder.AppendLine($"new_stable = {Boolean(settings.NotifyStable)}");
        builder.AppendLine($"new_rc = {Boolean(settings.NotifyRc)}");
        builder.AppendLine($"new_beta = {Boolean(settings.NotifyBeta)}");
        builder.AppendLine($"new_dev = {Boolean(settings.NotifyDev)}");

        return builder.ToString();
    }

    /// <summary>Parses a TOML document into settings, applying defaults for missing keys.</summary>
    /// <exception cref="FormatException">The document is not valid TOML.</exception>
    public static LauncherSettings Deserialize(string toml)
    {
        var document = TomlDocument.Parse(toml);
        var defaults = LauncherSettings.Default;

        var language = document.TryGetString(GeneralSection, "language", out var configuredLanguage)
            ? configuredLanguage
            : defaults.Language;

        var theme = document.TryGetEnum<ThemeMode>(GeneralSection, "theme", out var configuredTheme)
            ? configuredTheme
            : defaults.Theme;

        var closeBehavior = document.TryGetEnum<CloseBehavior>(GeneralSection, "close_behavior", out var configuredClose)
            ? configuredClose
            : defaults.CloseBehavior;

        var afterLaunch = document.TryGetEnum<AfterLaunchBehavior>(GeneralSection, "after_launch", out var configuredAfterLaunch)
            ? configuredAfterLaunch
            : defaults.AfterLaunch;

        return new LauncherSettings
        {
            Language = language,
            Theme = theme,
            CloseBehavior = closeBehavior,
            AfterLaunch = afterLaunch,
            LaunchWithConsole = document.TryGetBool(GeneralSection, "launch_with_console", out var console) && console,
            Autostart = document.TryGetBool(GeneralSection, "autostart", out var autostart) && autostart,
            InstallRoot = document.TryGetString(DownloadsSection, "install_root", out var installRoot) ? installRoot : string.Empty,
            Proxy = document.TryGetEnum<ProxyMode>(NetworkSection, "proxy", out var proxy) ? proxy : defaults.Proxy,
            ProxyUrl = document.TryGetString(NetworkSection, "proxy_url", out var proxyUrl) ? proxyUrl : string.Empty,
            ConcurrentDownloads = document.TryGetInt(DownloadsSection, "concurrent_downloads", out var concurrent)
                ? concurrent
                : defaults.ConcurrentDownloads,
            RefreshIntervalHours = document.TryGetInt(UpdatesSection, "refresh_interval_hours", out var interval)
                ? interval
                : defaults.RefreshIntervalHours,
            ShowOlderVersions = document.TryGetBool(UpdatesSection, "show_older_versions", out var older) && older,
            NotifyStable = document.TryGetBool(NotificationsSection, "new_stable", out var stable) && stable,
            NotifyRc = document.TryGetBool(NotificationsSection, "new_rc", out var rc) && rc,
            NotifyBeta = document.TryGetBool(NotificationsSection, "new_beta", out var beta) && beta,
            NotifyDev = document.TryGetBool(NotificationsSection, "new_dev", out var dev) && dev,
        };
    }

    private static string Boolean(bool value) => value ? "true" : "false";

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(_filePath, backup, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
