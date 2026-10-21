using GodotLauncher.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GodotLauncher.Core.Catalog;

/// <summary>
/// Parses the official <c>versions.yml</c> document served from the Godot
/// website repository. The document is a list of releases; every release may
/// list older preview builds of the same version, which become catalog entries
/// of their own (for example <c>4.8-beta1</c> and <c>4.8-dev7</c>).
/// </summary>
public sealed class VersionsYmlParser
{
    private readonly IDeserializer _deserializer;

    /// <summary>Creates a parser with a tolerant YamlDotNet configuration.</summary>
    public VersionsYmlParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Parses the YAML document into a normalized, newest-first catalog.
    /// </summary>
    /// <exception cref="FormatException">The document is not valid YAML.</exception>
    public IReadOnlyList<GodotVersion> Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        List<RawEntry?>? entries;
        try
        {
            entries = _deserializer.Deserialize<List<RawEntry?>>(yaml);
        }
        catch (Exception ex)
        {
            throw new FormatException("The version catalog is not valid YAML.", ex);
        }

        if (entries is null)
        {
            return [];
        }

        var versions = new List<GodotVersion>(entries.Count * 2);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Flavor))
            {
                continue;
            }

            if (!VersionNumber.TryParse(entry.Name, out _))
            {
                continue;
            }

            var name = entry.Name.Trim();
            var patches = BuildPatchReleases(entry.Releases);
            var featured = IsFeatured(entry.Featured);

            versions.Add(GodotVersion.Create(
                name,
                entry.Flavor.Trim(),
                ReleaseDates.Parse(entry.ReleaseDate),
                entry.ReleaseNotes,
                featured,
                patches));

            // Preview builds are published as separate downloads, so they are
            // promoted to their own catalog entries with the flavor as the id.
            foreach (var patch in entry.Releases ?? [])
            {
                if (patch is null || string.IsNullOrWhiteSpace(patch.Name))
                {
                    continue;
                }

                if (!LooksLikeFlavor(patch.Name))
                {
                    continue;
                }

                versions.Add(GodotVersion.Create(
                    name,
                    patch.Name.Trim(),
                    ReleaseDates.Parse(patch.ReleaseDate),
                    patch.ReleaseNotes));
            }
        }

        return CatalogNormalizer.Normalize(versions);
    }

    private static List<GodotPatchRelease> BuildPatchReleases(List<RawRelease?>? releases)
    {
        var result = new List<GodotPatchRelease>();
        if (releases is null)
        {
            return result;
        }

        foreach (var release in releases)
        {
            if (release is null || string.IsNullOrWhiteSpace(release.Name))
            {
                continue;
            }

            result.Add(new GodotPatchRelease
            {
                Name = release.Name.Trim(),
                ReleaseDate = ReleaseDates.Parse(release.ReleaseDate),
                ReleaseNotes = release.ReleaseNotes,
            });
        }

        return result;
    }

    /// <summary>
    /// Nested releases are named after the channel (<c>dev7</c>, <c>rc2</c>,
    /// <c>alpha-unofficial</c>). A numeric name would be a different version and
    /// is ignored to avoid mislabeling.
    /// </summary>
    private static bool LooksLikeFlavor(string name) => !VersionNumber.TryParse(name, out _);

    private static bool IsFeatured(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true,
    };

    private sealed class RawEntry
    {
        public string? Name { get; set; }

        public string? Flavor { get; set; }

        public string? ReleaseDate { get; set; }

        public string? ReleaseNotes { get; set; }

        public object? Featured { get; set; }

        public List<RawRelease?>? Releases { get; set; }
    }

    private sealed class RawRelease
    {
        public string? Name { get; set; }

        public string? ReleaseDate { get; set; }

        public string? ReleaseNotes { get; set; }
    }
}
