using System.IO.Compression;
using GodotLauncher.Core.Install;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.Core.Tests;

public sealed class ZipExtractorTests
{
    private static string CreateArchive(string directory, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(directory, $"archive-{Guid.NewGuid():N}.zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            var zipEntry = archive.CreateEntry(entry);
            using var writer = new StreamWriter(zipEntry.Open());
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public void Extract_WritesNestedEntries()
    {
        using var temp = new TempDirectory();
        var archivePath = CreateArchive(temp.Path, ("Godot.exe", "binary"), ("data/notes.txt", "hello"));
        var destination = temp.Subdirectory("out");

        ZipExtractor.Extract(archivePath, destination);

        Assert.True(File.Exists(Path.Combine(destination, "Godot.exe")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "data", "notes.txt")));
    }

    [Fact]
    public void Extract_RejectsEntriesThatEscapeTheDestination()
    {
        using var temp = new TempDirectory();
        var archivePath = CreateArchive(temp.Path, ("../escaped.txt", "bad"));
        var destination = temp.Subdirectory("out");

        var exception = Assert.Throws<InstallException>(() => ZipExtractor.Extract(archivePath, destination));

        Assert.Contains("escape", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(temp.File("escaped.txt")));
    }

    [Fact]
    public void Extract_RejectsAbsolutePaths()
    {
        using var temp = new TempDirectory();
        var archivePath = CreateArchive(temp.Path, ("/absolute.txt", "bad"));

        Assert.Throws<InstallException>(() => ZipExtractor.Extract(archivePath, temp.Subdirectory("out")));
    }
}

public sealed class ExecutableFinderTests
{
    [Fact]
    public void Find_WindowsPrefersTheGraphicalExecutable()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Godot_v4.7.2-stable_win64.exe"), "gui");
        File.WriteAllText(temp.File("Godot_v4.7.2-stable_win64_console.exe"), "console");

        Assert.Equal("Godot_v4.7.2-stable_win64.exe", ExecutableFinder.Find(temp.Path, HostPlatform.Windows));
    }

    [Fact]
    public void Find_WindowsFallsBackToConsoleBuild()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Godot_v4.7.2-stable_win64_console.exe"), "console");

        Assert.Equal("Godot_v4.7.2-stable_win64_console.exe", ExecutableFinder.Find(temp.Path, HostPlatform.Windows));
    }

    [Fact]
    public void Find_LinuxIgnoresConsoleSuffix()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Godot_v4.7.2-stable_linux.x86_64_console"), "console");
        File.WriteAllText(temp.File("Godot_v4.7.2-stable_linux.x86_64"), "gui");

        Assert.Equal("Godot_v4.7.2-stable_linux.x86_64", ExecutableFinder.Find(temp.Path, HostPlatform.Linux));
    }

    [Fact]
    public void Find_MacOSReturnsBinaryInsideAppBundle()
    {
        using var temp = new TempDirectory();
        var binaryDirectory = Path.Combine(temp.Path, "Godot.app", "Contents", "MacOS");
        Directory.CreateDirectory(binaryDirectory);
        File.WriteAllText(Path.Combine(binaryDirectory, "Godot"), "mach-o");

        var found = ExecutableFinder.Find(temp.Path, HostPlatform.MacOS);

        Assert.Equal(Path.Combine("Godot.app", "Contents", "MacOS", "Godot"), found);
    }

    [Fact]
    public void Find_ReturnsNullForEmptyDirectory()
    {
        using var temp = new TempDirectory();
        Assert.Null(ExecutableFinder.Find(temp.Path, HostPlatform.Windows));
    }
}

public sealed class InstallerTests
{
    private static readonly GodotVersion Stable = GodotVersion.Create("4.7.2", "stable", new DateOnly(2026, 8, 18));

    private static string CreateWindowsArchive(string directory)
    {
        var path = Path.Combine(directory, "4.7.2-stable_std_win64.exe.zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var name in new[] { "Godot_v4.7.2-stable_win64.exe", "Godot_v4.7.2-stable_win64_console.exe" })
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("MZ fake executable payload");
        }

        return path;
    }

    [Fact]
    public async Task InstallAsync_ExtractsMovesAndDescribesInstallation()
    {
        using var temp = new TempDirectory();
        var paths = AppPaths.CreateFor(temp.Path);
        paths.EnsureDirectories();
        var archivePath = CreateWindowsArchive(temp.Path);
        var installer = new Installer(paths, HostPlatform.Windows);

        var result = await installer.InstallAsync(Stable, GodotEdition.Standard, CpuArchitecture.X64, archivePath, InstallSource.Official, "abc");

        var installed = result.Installed;
        Assert.Equal("4.7.2-stable|standard|x64", installed.Id);
        Assert.Equal(installer.GetInstallDirectory("4.7.2-stable", GodotEdition.Standard), installed.InstallPath);
        Assert.Equal("Godot_v4.7.2-stable_win64.exe", installed.ExecutableRelativePath);
        Assert.True(File.Exists(installed.ExecutablePath));
        Assert.True(installed.SizeOnDisk > 0);
        Assert.Equal(InstallSource.Official, installed.Source);
        Assert.Equal("abc", installed.Sha256);
        Assert.True(installer.IsInstalled("4.7.2-stable", GodotEdition.Standard));
        Assert.Empty(Directory.EnumerateDirectories(paths.StagingDirectory));
    }

    [Fact]
    public async Task InstallAsync_FailsWhenTheArchiveHasNoEditor()
    {
        using var temp = new TempDirectory();
        var paths = AppPaths.CreateFor(temp.Path);
        paths.EnsureDirectories();
        var archivePath = Path.Combine(temp.Path, "empty.zip");
        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("no editor here");
        }

        var installer = new Installer(paths, HostPlatform.Windows);

        await Assert.ThrowsAsync<InstallException>(() => installer.InstallAsync(
            Stable, GodotEdition.Standard, CpuArchitecture.X64, archivePath, InstallSource.Official));
    }

    [Fact]
    public void CleanStagingDirectories_RemovesOnlyStaleDirectories()
    {
        using var temp = new TempDirectory();
        var paths = AppPaths.CreateFor(temp.Path);
        paths.EnsureDirectories();
        var installer = new Installer(paths, HostPlatform.Windows);
        var stale = Directory.CreateDirectory(Path.Combine(paths.StagingDirectory, "4.7.2-stable-standard-old"));
        var fresh = Directory.CreateDirectory(Path.Combine(paths.StagingDirectory, "4.7.2-stable-standard-new"));
        Directory.SetLastWriteTimeUtc(stale.FullName, DateTime.UtcNow.AddHours(-2));

        var removed = installer.CleanStagingDirectories(TimeSpan.FromHours(1));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale.FullName));
        Assert.True(Directory.Exists(fresh.FullName));
    }

    [Fact]
    public async Task InstallAsync_RefusesToOverwriteAnExistingInstallation()
    {
        using var temp = new TempDirectory();
        var paths = AppPaths.CreateFor(temp.Path);
        paths.EnsureDirectories();
        var archivePath = CreateWindowsArchive(temp.Path);
        var installer = new Installer(paths, HostPlatform.Windows);
        await installer.InstallAsync(Stable, GodotEdition.Standard, CpuArchitecture.X64, archivePath, InstallSource.Official);

        var exception = await Assert.ThrowsAsync<InstallException>(() => installer.InstallAsync(
            Stable, GodotEdition.Standard, CpuArchitecture.X64, archivePath, InstallSource.Official));

        Assert.Contains("already installed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterExisting_DescriptorsDoNotCopyFiles()
    {
        using var temp = new TempDirectory();
        var paths = AppPaths.CreateFor(temp.Path);
        var editorDirectory = temp.Subdirectory("my-editors");
        var executable = Path.Combine(editorDirectory, "Godot_v4.7.2-stable_mono_win64.exe");
        File.WriteAllText(executable, "binary");
        var installer = new Installer(paths, HostPlatform.Windows);

        var installed = installer.RegisterExisting(executable);

        Assert.Equal(InstallSource.LocalImport, installed.Source);
        Assert.Equal(GodotEdition.Mono, installed.Edition);
        Assert.Equal(CpuArchitecture.X64, installed.Architecture);
        Assert.Equal("4.7.2-stable", installed.VersionId);
        Assert.Equal("Godot_v4.7.2-stable_mono_win64.exe", installed.ExecutableRelativePath);
        Assert.Equal(editorDirectory, installed.InstallPath);
    }

    [Fact]
    public void RegisterExisting_RejectsFilesThatAreNotGodot()
    {
        using var temp = new TempDirectory();
        var path = temp.File("notepad.exe");
        File.WriteAllText(path, "binary");
        var installer = new Installer(AppPaths.CreateFor(temp.Path), HostPlatform.Windows);

        Assert.Throws<InstallException>(() => installer.RegisterExisting(path));
    }
}

public sealed class InstallationLibraryTests
{
    private static InstalledVersion Record(string versionId, GodotEdition edition = GodotEdition.Standard, InstallSource source = InstallSource.Official)
    {
        var parts = versionId.Split('-', 2);
        return InstalledVersion.Create(
            versionId,
            edition,
            CpuArchitecture.X64,
            $"Godot {parts[0]}",
            Path.Combine(Path.GetTempPath(), "godot-launcher-tests", versionId),
            "Godot.exe",
            1024,
            source);
    }

    private static (InstallationLibrary Library, InstallRegistry Registry) CreateLibrary(TempDirectory temp)
    {
        var registry = new InstallRegistry(temp.File("installed.json"));
        return (new InstallationLibrary(registry), registry);
    }

    [Fact]
    public async Task Library_PersistsRecordsAndDefaultSelection()
    {
        using var temp = new TempDirectory();
        var (library, registry) = CreateLibrary(temp);
        await library.LoadAsync();
        await library.AddOrUpdateAsync(Record("4.7.2-stable"));
        await library.AddOrUpdateAsync(Record("4.3-stable", GodotEdition.Mono));
        await library.SetDefaultAsync(Record("4.3-stable", GodotEdition.Mono).Id);

        var reloaded = new InstallationLibrary(registry);
        await reloaded.LoadAsync();

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("4.3-stable|mono|x64", reloaded.FindDefault()!.Id);
        Assert.Single(reloaded.Items, item => item.IsDefault);
    }

    [Fact]
    public async Task Library_UpdatesAliasPinAndLaunchTime()
    {
        using var temp = new TempDirectory();
        var (library, _) = CreateLibrary(temp);
        await library.LoadAsync();
        var record = Record("4.7.2-stable");
        await library.AddOrUpdateAsync(record);

        await library.SetAliasAsync(record.Id, "  Main engine  ");
        await library.SetPinnedAsync(record.Id, true);
        await library.TouchLaunchedAsync(record.Id, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var item = Assert.Single(library.Items);
        Assert.Equal("Main engine", item.Alias);
        Assert.Equal("Main engine", item.Label);
        Assert.True(item.Pinned);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), item.LastLaunched);
    }

    [Fact]
    public async Task Library_RemovesManagedFilesButKeepsImportedOnes()
    {
        using var temp = new TempDirectory();
        var (library, _) = CreateLibrary(temp);
        await library.LoadAsync();

        var managed = Record("4.7.2-stable");
        Directory.CreateDirectory(managed.InstallPath);
        File.WriteAllText(Path.Combine(managed.InstallPath, "Godot.exe"), "binary");

        var importedDirectory = temp.Subdirectory("imported");
        var imported = Record("4.3-stable", source: InstallSource.LocalImport) with { InstallPath = importedDirectory };
        File.WriteAllText(Path.Combine(importedDirectory, "Godot.exe"), "binary");

        await library.AddOrUpdateAsync(managed);
        await library.AddOrUpdateAsync(imported);

        await library.RemoveAsync(managed.Id, deleteFiles: true);
        await library.RemoveAsync(imported.Id, deleteFiles: true);

        Assert.False(Directory.Exists(managed.InstallPath));
        Assert.True(Directory.Exists(importedDirectory));
        Assert.Empty(library.Items);
    }

    [Fact]
    public async Task PreviewCleanup_KeepsTheNewestPreviewOfEachMinor()
    {
        using var temp = new TempDirectory();
        var (library, _) = CreateLibrary(temp);
        await library.LoadAsync();
        foreach (var id in new[] { "4.3-beta1", "4.3-beta2", "4.2-dev1", "4.4-beta1" })
        {
            await library.AddOrUpdateAsync(Record(id));
        }

        var candidates = library.FindPreviewCleanupCandidates().Select(c => c.VersionId).ToList();

        Assert.Equal(new[] { "4.3-beta1" }, candidates);
    }

    [Fact]
    public async Task PreviewCleanup_NeverTouchesPinnedOrImportedInstallations()
    {
        using var temp = new TempDirectory();
        var (library, _) = CreateLibrary(temp);
        await library.LoadAsync();
        await library.AddOrUpdateAsync(Record("4.3-beta1"));
        await library.AddOrUpdateAsync(Record("4.3-beta2"));
        await library.SetPinnedAsync(Record("4.3-beta1").Id, true);
        await library.AddOrUpdateAsync(Record("4.2-dev1", source: InstallSource.LocalImport));

        Assert.Empty(library.FindPreviewCleanupCandidates());
    }
}
