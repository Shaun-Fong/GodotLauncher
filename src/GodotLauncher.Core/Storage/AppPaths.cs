namespace GodotLauncher.Core.Storage;

/// <summary>
/// Resolves the directories the launcher reads and writes. The layout keeps user
/// data separate from editor installations so either can be moved independently.
/// </summary>
public sealed record AppPaths
{
    /// <summary>Directory holding configuration, caches, logs and the registry.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Directory holding editor installations, split by edition.</summary>
    public required string InstallRoot { get; init; }

    /// <summary>Where archives are downloaded and resumed.</summary>
    public required string DownloadDirectory { get; init; }

    /// <summary>Scratch directory for archives being extracted.</summary>
    public required string StagingDirectory { get; init; }

    /// <summary>Path of <c>config.toml</c>.</summary>
    public string ConfigFile => Path.Combine(DataDirectory, "config.toml");

    /// <summary>Path of <c>installed.json</c>.</summary>
    public string InstalledFile => Path.Combine(DataDirectory, "installed.json");

    /// <summary>Path of <c>catalog-cache.json</c>.</summary>
    public string CatalogCacheFile => Path.Combine(DataDirectory, "catalog-cache.json");

    /// <summary>Directory holding per-version asset metadata.</summary>
    public string AssetCacheDirectory => Path.Combine(DataDirectory, "assets");

    /// <summary>Directory holding rolling log files.</summary>
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>Creates the default layout for the current user.</summary>
    public static AppPaths CreateDefault()
    {
        var data = GetDefaultDataDirectory();
        return CreateFor(data, data);
    }

    /// <summary>Builds a layout under explicit data and install roots.</summary>
    public static AppPaths CreateFor(string dataDirectory, string? installRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var install = string.IsNullOrWhiteSpace(installRoot)
            ? Path.Combine(dataDirectory, "editors")
            : installRoot;

        return new AppPaths
        {
            DataDirectory = Path.GetFullPath(dataDirectory),
            InstallRoot = Path.GetFullPath(install),
            DownloadDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "downloads"),
            StagingDirectory = Path.Combine(Path.GetFullPath(dataDirectory), ".staging"),
        };
    }

    /// <summary>Returns a copy of these paths that installs into another root.</summary>
    public AppPaths WithInstallRoot(string? installRoot) =>
        string.IsNullOrWhiteSpace(installRoot) ? this : this with { InstallRoot = Path.GetFullPath(installRoot) };

    /// <summary>Creates every directory the launcher needs.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(InstallRoot);
        Directory.CreateDirectory(DownloadDirectory);
        Directory.CreateDirectory(StagingDirectory);
        Directory.CreateDirectory(AssetCacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    private static string GetDefaultDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, "GodotLauncher");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "GodotLauncher");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "GodotLauncher");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".local", "share", "GodotLauncher");
    }
}
