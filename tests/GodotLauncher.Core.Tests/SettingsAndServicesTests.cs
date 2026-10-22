using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Launch;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsEverySetting()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.File("config.toml"));
        var expected = new LauncherSettings
        {
            Language = "zh-CN",
            Theme = ThemeMode.Dark,
            CloseBehavior = CloseBehavior.Quit,
            AfterLaunch = AfterLaunchBehavior.Keep,
            LaunchWithConsole = true,
            Autostart = true,
            InstallRoot = @"D:\Godot Editors",
            ConcurrentDownloads = 3,
            RefreshIntervalHours = 12,
            ShowOlderVersions = true,
            NotifyStable = true,
            NotifyBeta = true,
        };

        store.Save(expected);
        var loaded = store.Load();

        Assert.Equal(expected, loaded);
        Assert.Contains("[downloads]", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenTheFileIsMissing()
    {
        using var temp = new TempDirectory();
        Assert.Equal(LauncherSettings.Default, new SettingsStore(temp.File("config.toml")).Load());
    }

    [Fact]
    public void Load_BacksUpCorruptFilesAndReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var path = temp.File("config.toml");
        File.WriteAllText(path, "[general]\ntheme = \"dark\"\nthis line has no separator\n");
        var store = new SettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(LauncherSettings.Default, loaded);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(temp.Path, "config.toml.corrupt-*"));
    }

    [Fact]
    public void Deserialize_IgnoresUnknownKeysAndAppliesDefaults()
    {
        var settings = SettingsStore.Deserialize("""
            [general]
            theme = "light"
            unknown_option = "ignored"

            [future_section]
            something = true
            """);

        Assert.Equal(ThemeMode.Light, settings.Theme);
        Assert.Equal(LauncherSettings.Default.CloseBehavior, settings.CloseBehavior);
    }

    [Fact]
    public void Normalize_ClampsUnsafeValues()
    {
        var settings = new LauncherSettings
        {
            ConcurrentDownloads = 99,
            RefreshIntervalHours = 0,
            Language = "  en  ",
        }.Normalize();

        Assert.Equal(4, settings.ConcurrentDownloads);
        Assert.Equal(0, settings.RefreshIntervalHours);
        Assert.Equal("en", settings.Language);

        var clamped = (new LauncherSettings { RefreshIntervalHours = 500 }).Normalize();
        Assert.Equal(168, clamped.RefreshIntervalHours);
    }

    [Fact]
    public void Toml_ParsesCommentsEscapesAndLiteralStrings()
    {
        var document = TomlDocument.Parse("""
            # heading comment
            [general]
            path = "C:\\Godot \"Editors\""
            literal = 'raw \ value'
            enabled = true
            count = 7
            """);

        Assert.True(document.TryGetString("general", "path", out var path));
        Assert.Equal("C:\\Godot \"Editors\"", path);
        Assert.True(document.TryGetString("general", "literal", out var literal));
        Assert.Equal(@"raw \ value", literal);
        Assert.True(document.TryGetBool("general", "enabled", out var enabled));
        Assert.True(enabled);
        Assert.True(document.TryGetInt("general", "count", out var count));
        Assert.Equal(7, count);
    }
}

public sealed class LaunchAndLoggingTests
{
    [Fact]
    public void SplitArguments_RespectsQuotes()
    {
        var arguments = EditorLauncher.SplitArguments(@"--path ""C:\My Projects\Game"" --headless");

        Assert.Equal(new[] { "--path", @"C:\My Projects\Game", "--headless" }, arguments);
    }

    [Fact]
    public void CreateStartInfo_PassesArgumentsAndWorkingDirectory()
    {
        var startInfo = EditorLauncher.CreateStartInfo(@"C:\Godot\Godot.exe", @"C:\Godot", ["--editor"], useConsole: true);

        Assert.Equal(@"C:\Godot", startInfo.WorkingDirectory);
        Assert.False(startInfo.CreateNoWindow);
        Assert.NotEmpty(startInfo.ArgumentList);
        Assert.Equal("--editor", startInfo.ArgumentList[^1]);
    }

    [Fact]
    public void Launch_ThrowsWhenTheExecutableIsMissing()
    {
        using var temp = new TempDirectory();
        var installation = InstalledVersion.Create(
            "4.7.2-stable",
            GodotEdition.Standard,
            CpuArchitecture.X64,
            "Godot 4.7.2",
            temp.Path,
            "Godot.exe",
            0,
            InstallSource.Official);

        var exception = Assert.Throws<LaunchException>(() => new EditorLauncher().Launch(installation));

        Assert.Contains("not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindConsoleExecutable_ReturnsSiblingWhenPresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var gui = temp.File("Godot_v4.7.2-stable_win64.exe");
        var console = temp.File("Godot_v4.7.2-stable_win64_console.exe");
        File.WriteAllText(gui, "gui");
        File.WriteAllText(console, "console");

        Assert.Equal(console, EditorLauncher.FindConsoleExecutable(gui));
    }

    [Fact]
    public void FileLog_WritesAndRotates()
    {
        using var temp = new TempDirectory();
        using var log = new FileLog(temp.Path, "test.log", 64 * 1024, 2);

        log.Info("hello from the launcher");
        var content = File.ReadAllText(log.FilePath);
        Assert.Contains("[INFO] hello from the launcher", content, StringComparison.Ordinal);

        for (var index = 0; index < 80; index++)
        {
            log.Write(LogLevel.Warning, new string('x', 1024));
        }

        Assert.True(File.Exists(log.FilePath + ".1"));
        Assert.True(new FileInfo(log.FilePath).Length <= 64 * 1024);
    }
}

public sealed class CatalogServiceTests
{
    private sealed class FakeSource(string id, Func<string?, CatalogFetch> fetch) : ICatalogSource
    {
        public string? LastETag { get; private set; }

        public int Calls { get; private set; }

        public string Id => id;

        public Task<CatalogFetch> FetchAsync(HttpClient http, string? etag, CancellationToken cancellationToken)
        {
            Calls++;
            LastETag = etag;
            return Task.FromResult(fetch(etag));
        }
    }

    private sealed class ThrowingSource(string id, string message) : ICatalogSource
    {
        public string Id => id;

        public Task<CatalogFetch> FetchAsync(HttpClient http, string? etag, CancellationToken cancellationToken) =>
            throw new HttpRequestException(message);
    }

    private static GodotVersion Version(string name, string flavor = "stable") =>
        GodotVersion.Create(name, flavor, new DateOnly(2026, 1, 1));

    private static VersionCatalog Cached(params GodotVersion[] versions) => new()
    {
        Versions = versions,
        Source = "primary",
        ETag = "etag-1",
        FetchedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Refresh_UpdatesCacheAndReportsNewVersions()
    {
        using var temp = new TempDirectory();
        var cache = new CatalogCache(temp.File("catalog-cache.json"));
        var source = new FakeSource("primary", _ => new CatalogFetch
        {
            Versions = [Version("4.7.2"), Version("4.7.1")],
            Source = "primary",
            ETag = "etag-2",
        });
        var service = new CatalogService(new HttpClient(), [source], cache);

        var result = await service.RefreshAsync(Cached(Version("4.7.1")));

        Assert.Equal(CatalogRefreshStatus.Updated, result.Status);
        Assert.Contains("4.7.2-stable", result.NewVersionIds);
        Assert.DoesNotContain("4.7.1-stable", result.NewVersionIds);

        var stored = await cache.LoadAsync();
        Assert.NotNull(stored);
        Assert.True(stored.IsFromCache);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Refresh_SendsConditionalRequestToTheCachedSource()
    {
        using var temp = new TempDirectory();
        var source = new FakeSource("primary", _ => new CatalogFetch { NotModified = true, Source = "primary" });
        var service = new CatalogService(new HttpClient(), [source], new CatalogCache(temp.File("cache.json")));
        var previous = Cached(Version("4.7.2"));

        var result = await service.RefreshAsync(previous);

        Assert.Equal(CatalogRefreshStatus.NotModified, result.Status);
        Assert.Equal("etag-1", source.LastETag);
        Assert.Same(previous.Versions[0], result.Catalog!.Versions[0]);
    }

    [Fact]
    public async Task Refresh_FallsBackToTheNextSource()
    {
        using var temp = new TempDirectory();
        var failing = new ThrowingSource("primary", "offline");
        var working = new FakeSource("backup", _ => new CatalogFetch
        {
            Versions = [Version("4.7.2")],
            Source = "backup",
        });
        var service = new CatalogService(new HttpClient(), [failing, working], new CatalogCache(temp.File("cache.json")));

        var result = await service.RefreshAsync(null);

        Assert.Equal(CatalogRefreshStatus.Updated, result.Status);
        Assert.Equal("backup", result.Catalog!.Source);
    }

    [Fact]
    public async Task Refresh_RejectsTruncatedCatalogsAndKeepsTheCache()
    {
        using var temp = new TempDirectory();
        var previousVersions = Enumerable.Range(0, 20)
            .Select(index => Version($"4.{index}.0"))
            .ToArray();
        var previous = Cached(previousVersions);
        var source = new FakeSource("primary", _ => new CatalogFetch
        {
            Versions = [Version("4.0.0")],
            Source = "primary",
        });
        var cache = new CatalogCache(temp.File("cache.json"));
        var service = new CatalogService(new HttpClient(), [source], cache);

        var result = await service.RefreshAsync(previous);

        Assert.Equal(CatalogRefreshStatus.Rejected, result.Status);
        Assert.Equal(20, result.Catalog!.Count);
        Assert.False(File.Exists(cache.FilePath));
    }

    [Fact]
    public async Task Refresh_ReportsFailureWhenEverySourceIsDown()
    {
        using var temp = new TempDirectory();
        var service = new CatalogService(
            new HttpClient(),
            [new ThrowingSource("primary", "offline"), new ThrowingSource("backup", "also offline")],
            new CatalogCache(temp.File("cache.json")));
        var previous = Cached(Version("4.7.2"));

        var result = await service.RefreshAsync(previous);

        Assert.Equal(CatalogRefreshStatus.Failed, result.Status);
        Assert.Same(previous, result.Catalog);
        Assert.Contains("primary", result.Message);
        Assert.Contains("backup", result.Message);
    }
}
