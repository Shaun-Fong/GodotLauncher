using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>Helpers for the <c>{name}-{flavor}</c> version id format.</summary>
public static class VersionIds
{
    /// <summary>Splits a version id into its numeric name and flavor.</summary>
    public static bool TryParse(string? versionId, out string name, out string flavor)
    {
        name = string.Empty;
        flavor = string.Empty;

        if (string.IsNullOrWhiteSpace(versionId))
        {
            return false;
        }

        var value = versionId.Trim();

        // The flavor may itself contain dashes (alpha-unofficial), so only the
        // first separator belongs to the numeric part.
        var separator = value.IndexOf('-');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var candidateName = value[..separator];
        if (!VersionNumber.TryParse(candidateName, out _))
        {
            return false;
        }

        name = candidateName;
        flavor = value[(separator + 1)..];
        return true;
    }

    /// <summary>Reconstructs a catalog version from an id, or returns <c>null</c>.</summary>
    public static GodotVersion? ToVersion(string? versionId)
    {
        if (!TryParse(versionId, out var name, out var flavor))
        {
            return null;
        }

        return GodotVersion.Create(name, flavor);
    }
}
