using System.Globalization;

namespace GodotLauncher.Core.Models;

/// <summary>
/// One Godot version as published on the download page: a numeric version plus
/// the flavor (channel) such as <c>stable</c> or <c>beta1</c>. The pair forms a
/// unique id, for example <c>4.8-beta1</c>.
/// </summary>
public sealed record GodotVersion
{
    /// <summary>Numeric version, e.g. <c>4.8</c> or <c>4.7.2</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Flavor identifier, e.g. <c>stable</c>, <c>rc1</c>, <c>beta2</c> or <c>dev7</c>.</summary>
    public required string Flavor { get; init; }

    /// <summary>Release date of the version, or <see cref="DateOnly.MinValue"/> when unknown.</summary>
    public DateOnly ReleaseDate { get; init; }

    /// <summary>Optional link to the release notes.</summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>True when the website marks the version as featured.</summary>
    public bool IsFeatured { get; init; }

    /// <summary>Older patch/preview releases that belong to this version entry.</summary>
    public IReadOnlyList<GodotPatchRelease> PatchReleases { get; init; } = [];

    /// <summary>Stable, release candidate, beta or development channel.</summary>
    public ReleaseChannel Channel => ReleaseChannelExtensions.ClassifyFlavor(Flavor).Channel;

    /// <summary>Stage number inside the channel (<c>rc2</c> yields 2, stable yields 0).</summary>
    public int StageNumber => ReleaseChannelExtensions.ClassifyFlavor(Flavor).Number;

    /// <summary>Unique catalog id: <c>{Name}-{Flavor}</c>.</summary>
    public string Id => $"{Name}-{Flavor}";

    /// <summary>Parsed numeric version.</summary>
    public VersionNumber Number => VersionNumber.TryParse(Name, out var number) ? number : default;

    /// <summary>True for anything that is not a stable release.</summary>
    public bool IsPreview => Channel != ReleaseChannel.Stable;

    /// <summary>Release date formatted as <c>18 August 2026</c>.</summary>
    public string ReleaseDateText =>
        ReleaseDate == DateOnly.MinValue
            ? "Unknown date"
            : ReleaseDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Version with channel spelled out, e.g. <c>4.8 Beta 1</c>.</summary>
    public string DisplayName
    {
        get
        {
            var stage = ReleaseChannelExtensions.ClassifyFlavor(Flavor);
            return stage.Channel switch
            {
                ReleaseChannel.Stable => $"Godot {Name}",
                ReleaseChannel.Rc => $"Godot {Name} RC{stage.Number}",
                ReleaseChannel.Beta => $"Godot {Name} Beta{stage.Number}",
                _ => $"Godot {Name} Dev{stage.Number}",
            };
        }
    }

    /// <summary>Builds a catalog entry, validating that the version number parses.</summary>
    public static GodotVersion Create(
        string name,
        string flavor,
        DateOnly releaseDate = default,
        string? releaseNotes = null,
        bool isFeatured = false,
        IReadOnlyList<GodotPatchRelease>? patchReleases = null)
    {
        if (!VersionNumber.TryParse(name, out _))
        {
            throw new FormatException($"'{name}' is not a valid Godot version number.");
        }

        return new GodotVersion
        {
            Name = name,
            Flavor = flavor,
            ReleaseDate = releaseDate,
            ReleaseNotes = releaseNotes,
            IsFeatured = isFeatured,
            PatchReleases = patchReleases ?? [],
        };
    }

    /// <inheritdoc />
    public override string ToString() => Id;
}

/// <summary>An older entry listed below a version, e.g. <c>rc1</c> of 4.7.2.</summary>
public sealed record GodotPatchRelease
{
    /// <summary>Version name of the patch entry, usually equal to the parent version.</summary>
    public required string Name { get; init; }

    /// <summary>Release date, if the source provides one.</summary>
    public DateOnly ReleaseDate { get; init; }

    /// <summary>Optional release notes link.</summary>
    public string? ReleaseNotes { get; init; }
}
