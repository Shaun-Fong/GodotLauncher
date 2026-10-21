namespace GodotLauncher.Core.Models;

/// <summary>Where an installation came from. Shown in the UI and used for repair hints.</summary>
public enum InstallSource
{
    /// <summary>Directly from the official download endpoint.</summary>
    Official,

    /// <summary>From GitHub release assets.</summary>
    GitHub,

    /// <summary>From a user configured mirror.</summary>
    Mirror,

    /// <summary>Imported from an executable already present on disk.</summary>
    LocalImport,
}

/// <summary>
/// One Godot editor installation known to the launcher. The record is persisted
/// in <c>installed.json</c>, so every property is init-only and JSON friendly.
/// </summary>
public sealed record InstalledVersion
{
    /// <summary>Unique installation key: <c>{versionId}|{edition}|{architecture}</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Catalog id of the version, e.g. <c>4.7.2-stable</c>.</summary>
    public required string VersionId { get; init; }

    /// <summary>Standard or .NET edition.</summary>
    public required GodotEdition Edition { get; init; }

    /// <summary>Architecture of the installed binaries.</summary>
    public required CpuArchitecture Architecture { get; init; }

    /// <summary>Friendly name, e.g. <c>Godot 4.7.2</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Absolute path of the installation directory.</summary>
    public required string InstallPath { get; init; }

    /// <summary>Executable path relative to <see cref="InstallPath"/>.</summary>
    public required string ExecutableRelativePath { get; init; }

    /// <summary>When the installation finished.</summary>
    public DateTimeOffset InstalledAt { get; init; }

    /// <summary>Total size of the installation directory in bytes.</summary>
    public long SizeOnDisk { get; init; }

    /// <summary>Download source, or local import.</summary>
    public InstallSource Source { get; init; }

    /// <summary>SHA-256 of the downloaded archive when it could be verified.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Pinned versions are kept out of cleanup and sorted first.</summary>
    public bool Pinned { get; init; }

    /// <summary>Optional user supplied alias shown instead of the version name.</summary>
    public string? Alias { get; init; }

    /// <summary>Last time the editor was launched through the launcher.</summary>
    public DateTimeOffset? LastLaunched { get; init; }

    /// <summary>True for the version launched by "Launch default".</summary>
    public bool IsDefault { get; init; }

    /// <summary>Optional extra command line arguments passed on every launch.</summary>
    public string? LaunchArguments { get; init; }

    /// <summary>Absolute path of the editor executable.</summary>
    public string ExecutablePath => Path.Combine(InstallPath, ExecutableRelativePath);

    /// <summary>Label shown in lists, preferring the user alias.</summary>
    public string Label => string.IsNullOrWhiteSpace(Alias) ? DisplayName : Alias;

    /// <summary>Builds the canonical installation key.</summary>
    public static string BuildId(string versionId, GodotEdition edition, CpuArchitecture architecture) =>
        $"{versionId}|{edition.ToString().ToLowerInvariant()}|{architecture.ToString().ToLowerInvariant()}";

    /// <summary>Creates a new installation record with a computed id.</summary>
    public static InstalledVersion Create(
        string versionId,
        GodotEdition edition,
        CpuArchitecture architecture,
        string displayName,
        string installPath,
        string executableRelativePath,
        long sizeOnDisk,
        InstallSource source,
        string? sha256 = null) => new()
        {
            Id = BuildId(versionId, edition, architecture),
            VersionId = versionId,
            Edition = edition,
            Architecture = architecture,
            DisplayName = displayName,
            InstallPath = installPath,
            ExecutableRelativePath = executableRelativePath,
            InstalledAt = DateTimeOffset.UtcNow,
            SizeOnDisk = sizeOnDisk,
            Source = source,
            Sha256 = sha256,
        };

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({InstallPath})";
}
