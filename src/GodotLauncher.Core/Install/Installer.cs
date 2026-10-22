using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.Core.Install;

/// <summary>Move result of an installation, including where the executable lives.</summary>
public sealed record InstallResult
{
    /// <summary>Registry record describing the new installation.</summary>
    public required InstalledVersion Installed { get; init; }
}

/// <summary>
/// Installs a downloaded archive: extract into staging, locate the editor,
/// move the directory into place and describe the result. The registry itself
/// is updated by <see cref="InstallationLibrary"/>.
/// </summary>
public sealed class Installer
{
    private readonly AppPaths _paths;
    private readonly HostPlatform _platform;

    /// <summary>Creates an installer that installs under the configured root.</summary>
    public Installer(AppPaths paths, HostPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _platform = platform ?? HostPlatformExtensions.Current;
    }

    /// <summary>Directory a version installs into: <c>{root}/{std|dotnet}/{version-id}</c>.</summary>
    public string GetInstallDirectory(string versionId, GodotEdition edition) =>
        Path.Combine(_paths.InstallRoot, edition.ToFolderName(), versionId);

    /// <summary>
    /// Moves .NET editors installed by older launchers from <c>mono/</c> into their own
    /// <c>dotnet/</c> directory so the two editions never share a folder.
    /// </summary>
    /// <returns>True when a legacy directory was moved.</returns>
    public bool MigrateLegacyEditionFolders()
    {
        var legacy = Path.Combine(_paths.InstallRoot, GodotEdition.Mono.ToLegacyFolderName());
        var current = Path.Combine(_paths.InstallRoot, GodotEdition.Mono.ToFolderName());
        if (!Directory.Exists(legacy) || Directory.Exists(current))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_paths.InstallRoot);
            Directory.Move(legacy, current);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Returns true when an installation directory already exists.</summary>
    public bool IsInstalled(string versionId, GodotEdition edition) =>
        Directory.Exists(GetInstallDirectory(versionId, edition));

    /// <summary>
    /// Installs the archive for the given version. The archive stays on disk so
    /// a failed installation can be retried without downloading again.
    /// </summary>
    /// <exception cref="InstallException">Extraction failed or no executable was found.</exception>
    public async Task<InstallResult> InstallAsync(
        GodotVersion version,
        GodotEdition edition,
        CpuArchitecture architecture,
        string archivePath,
        InstallSource source,
        string? sha256 = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        if (!File.Exists(archivePath))
        {
            throw new InstallException($"The archive '{archivePath}' does not exist.");
        }

        var installDirectory = GetInstallDirectory(version.Id, edition);
        if (Directory.Exists(installDirectory))
        {
            throw new InstallException($"{version.DisplayName} ({edition.ToDisplayName()}) is already installed.");
        }

        var staging = Path.Combine(
            _paths.StagingDirectory,
            $"{version.Id}-{edition.ToFolderName()}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(staging);

            await Task.Run(() => ZipExtractor.Extract(archivePath, staging), cancellationToken).ConfigureAwait(false);

            // Mono archives wrap the payload in a folder named after the build;
            // install its contents directly so every edition shares one layout.
            var contentRoot = ResolveContentRoot(staging);
            var executable = ExecutableFinder.Find(contentRoot, _platform)
                ?? throw new InstallException(
                    $"No Godot editor executable was found inside {Path.GetFileName(archivePath)}.");

            var size = MeasureDirectory(contentRoot);
            MoveDirectory(contentRoot, installDirectory);

            return new InstallResult
            {
                Installed = InstalledVersion.Create(
                    version.Id,
                    edition,
                    architecture,
                    version.DisplayName,
                    installDirectory,
                    executable,
                    size,
                    source,
                    sha256),
            };
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Returns the extracted payload root. Archives that only contain a single
    /// top-level folder (the .NET builds) are unwrapped so the executable and
    /// its data folder land directly in the install directory.
    /// </summary>
    private static string ResolveContentRoot(string staging)
    {
        var directories = Directory.GetDirectories(staging);
        var files = Directory.GetFiles(staging);
        return directories.Length == 1 && files.Length == 0 ? directories[0] : staging;
    }

    /// <summary>
    /// Registers an editor executable that already exists on disk. Nothing is
    /// copied and nothing is deleted on uninstall.
    /// </summary>
    public InstalledVersion RegisterExisting(string executablePath, CpuArchitecture? architecture = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new InstallException($"The file '{executablePath}' does not exist.");
        }

        if (!Path.GetFileName(fullPath).StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException("The selected file does not look like a Godot editor executable.");
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        var edition = fullPath.Contains("mono", StringComparison.OrdinalIgnoreCase)
            ? GodotEdition.Mono
            : GodotEdition.Standard;

        var parsed = ExeNameParser.Parse(Path.GetFileName(fullPath));
        var versionId = parsed?.VersionId ?? $"imported-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var displayName = parsed?.DisplayName ?? Path.GetFileNameWithoutExtension(fullPath);
        var arch = architecture ?? ExeNameParser.GuessArchitecture(fullPath) ?? HostArchitecture.Current;

        return InstalledVersion.Create(
            versionId,
            edition,
            arch,
            displayName,
            directory,
            Path.GetRelativePath(directory, fullPath),
            MeasureDirectory(directory),
            InstallSource.LocalImport);
    }

    /// <summary>Sums the size of every file below <paramref name="directory"/>.</summary>
    public static long MeasureDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // A file that disappeared mid-scan does not affect the estimate.
            }
        }

        return total;
    }

    private static void MoveDirectory(string source, string destination)
    {
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException) when (!Directory.Exists(destination))
        {
            // Different volumes cannot be moved directly; copy and remove instead.
            CopyDirectory(source, destination);
            TryDeleteDirectory(source);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Deletes staging directories left behind by interrupted installs. Only directories last written
    /// before <paramref name="minimumAge"/> are removed so a concurrent install is never disturbed.
    /// </summary>
    public int CleanStagingDirectories(TimeSpan minimumAge)
    {
        if (!Directory.Exists(_paths.StagingDirectory))
        {
            return 0;
        }

        var removed = 0;
        var cutoff = DateTime.UtcNow - minimumAge;
        foreach (var directory in Directory.EnumerateDirectories(_paths.StagingDirectory))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) > cutoff)
                {
                    continue;
                }
            }
            catch (IOException)
            {
                continue;
            }

            TryDeleteDirectory(directory);
            if (!Directory.Exists(directory))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Deletes a directory, ignoring the usual access and in-use errors.</summary>
    public static void TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
