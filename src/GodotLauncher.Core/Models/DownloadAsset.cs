using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Models;

/// <summary>
/// A concrete downloadable archive for one version, edition, platform and
/// architecture combination.
/// </summary>
public sealed record DownloadAsset
{
    /// <summary>Catalog id of the version this asset belongs to.</summary>
    public required string VersionId { get; init; }

    /// <summary>Editor edition contained in the archive.</summary>
    public required GodotEdition Edition { get; init; }

    /// <summary>Target architecture.</summary>
    public required CpuArchitecture Architecture { get; init; }

    /// <summary>Target operating system.</summary>
    public required HostPlatform Platform { get; init; }

    /// <summary>Official slug inside the release, e.g. <c>win64.exe.zip</c>.</summary>
    public required string Slug { get; init; }

    /// <summary>Primary download URI.</summary>
    public required Uri DownloadUri { get; init; }

    /// <summary>Alternative URI, typically the GitHub release asset.</summary>
    public Uri? FallbackUri { get; init; }

    /// <summary>Expected archive size in bytes when known.</summary>
    public long? SizeInBytes { get; init; }

    /// <summary>Expected SHA-256 digest in hex when known.</summary>
    public string? Sha256 { get; init; }

    /// <summary>File name used on disk, derived from the slug.</summary>
    public string FileName => $"{VersionId}_{Edition.ToFolderName()}_{Slug}";
}
