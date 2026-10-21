using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>
/// Sorts Godot versions newest first and removes duplicate ids.
/// Ordering rules (from the design document):
/// numeric version descending, then channel stability (stable &gt; rc &gt; beta &gt; dev),
/// then stage number descending, then release date descending.
/// </summary>
public sealed class VersionComparer : IComparer<GodotVersion>
{
    /// <summary>Shared instance.</summary>
    public static VersionComparer Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(GodotVersion? x, GodotVersion? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        // Compare in reverse order so List.Sort produces newest first.
        var result = y.Number.CompareTo(x.Number);
        if (result != 0)
        {
            return result;
        }

        result = ((int)y.Channel).CompareTo((int)x.Channel);
        if (result != 0)
        {
            return result;
        }

        result = y.StageNumber.CompareTo(x.StageNumber);
        if (result != 0)
        {
            return result;
        }

        result = y.ReleaseDate.CompareTo(x.ReleaseDate);
        if (result != 0)
        {
            return result;
        }

        return string.CompareOrdinal(x.Id, y.Id);
    }
}

/// <summary>Catalog normalization helpers.</summary>
public static class CatalogNormalizer
{
    /// <summary>
    /// Drops entries with invalid identifiers, removes duplicate ids (keeping the
    /// first occurrence) and returns the list sorted newest first.
    /// </summary>
    public static IReadOnlyList<GodotVersion> Normalize(IEnumerable<GodotVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GodotVersion>();

        foreach (var version in versions)
        {
            if (version is null ||
                string.IsNullOrWhiteSpace(version.Name) ||
                string.IsNullOrWhiteSpace(version.Flavor) ||
                !VersionNumber.TryParse(version.Name, out _))
            {
                continue;
            }

            if (seen.Add(version.Id))
            {
                result.Add(version);
            }
        }

        result.Sort(VersionComparer.Instance);
        return result;
    }

    /// <summary>
    /// Safety valve from the design: a refresh that returns less than half of the
    /// cached version count is treated as suspicious and rejected.
    /// </summary>
    public static bool LooksLikeTruncatedCatalog(int cachedCount, int freshCount) =>
        cachedCount >= 10 && freshCount * 2 < cachedCount;
}
