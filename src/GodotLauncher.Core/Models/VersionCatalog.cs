using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Models;

/// <summary>
/// A downloaded and normalized view of the Godot release catalog, together with
/// the metadata needed to refresh it conditionally.
/// </summary>
public sealed record VersionCatalog
{
    /// <summary>Versions sorted from newest to oldest.</summary>
    public IReadOnlyList<GodotVersion> Versions { get; init; } = [];

    /// <summary>When the data was obtained (UTC).</summary>
    public DateTimeOffset FetchedAt { get; init; }

    /// <summary>Identifier of the source that produced this catalog.</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>ETag reported by the source, used for conditional requests.</summary>
    public string? ETag { get; init; }

    /// <summary>True when the catalog was loaded from the local cache.</summary>
    public bool IsFromCache { get; init; }

    /// <summary>Number of versions in the catalog.</summary>
    public int Count => Versions.Count;

    /// <summary>Finds a version by its catalog id, case insensitively.</summary>
    public GodotVersion? Find(string id) =>
        Versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>An empty catalog used before the first successful refresh.</summary>
    public static VersionCatalog Empty { get; } = new()
    {
        FetchedAt = DateTimeOffset.MinValue,
        Source = "empty",
    };
}
