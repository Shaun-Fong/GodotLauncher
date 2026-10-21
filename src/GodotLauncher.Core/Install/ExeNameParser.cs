using System.Text.RegularExpressions;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Install;

/// <summary>Version information guessed from a Godot executable file name.</summary>
public sealed record ParsedExeName
{
    /// <summary>Catalog style version id, e.g. <c>4.7.2-stable</c>.</summary>
    public required string VersionId { get; init; }

    /// <summary>Numeric version.</summary>
    public required string Name { get; init; }

    /// <summary>Channel flavor.</summary>
    public required string Flavor { get; init; }

    /// <summary>Edition inferred from the <c>mono_</c> marker.</summary>
    public required GodotEdition Edition { get; init; }

    /// <summary>Architecture inferred from the platform slug, when possible.</summary>
    public CpuArchitecture? Architecture { get; init; }

    /// <summary>Human readable label, e.g. <c>Godot 4.7.2</c>.</summary>
    public string DisplayName => GodotVersion.Create(Name, Flavor).DisplayName;
}

/// <summary>
/// Recognizes Godot editor file names such as
/// <c>Godot_v4.7.2-stable_mono_win64.exe</c> or
/// <c>Godot_v4.3-stable_linux.x86_64</c>.
/// </summary>
public static partial class ExeNameParser
{
    /// <summary>Parses a file name, returning <c>null</c> when it is not a Godot build.</summary>
    public static ParsedExeName? Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = GodotNameRegex().Match(Path.GetFileName(fileName.Trim()));
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["version"].Value;
        var flavor = match.Groups["flavor"].Value;
        if (!VersionNumber.TryParse(name, out _))
        {
            return null;
        }

        var edition = fileName.Contains("mono", StringComparison.OrdinalIgnoreCase)
            ? GodotEdition.Mono
            : GodotEdition.Standard;

        return new ParsedExeName
        {
            VersionId = $"{name}-{flavor}",
            Name = name,
            Flavor = flavor,
            Edition = edition,
            Architecture = GuessArchitecture(fileName),
        };
    }

    /// <summary>Infers the architecture from tokens such as <c>win64</c> or <c>arm64</c>.</summary>
    public static CpuArchitecture? GuessArchitecture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var value = fileName.ToLowerInvariant();
        if (value.Contains("arm64") || value.Contains("aarch64"))
        {
            return CpuArchitecture.Arm64;
        }

        if (value.Contains("arm32") || value.Contains("armv7"))
        {
            return CpuArchitecture.Arm32;
        }

        if (value.Contains("win32") || value.Contains("x86_32") || value.Contains("x11.32") || value.Contains("_32."))
        {
            return CpuArchitecture.X86;
        }

        if (value.Contains("win64") || value.Contains("x86_64") || value.Contains("x11.64") || value.Contains("_64.") || value.Contains("universal"))
        {
            return CpuArchitecture.X64;
        }

        return null;
    }

    [GeneratedRegex(
        "^Godot_v(?<version>\\d+\\.\\d+(?:\\.\\d+)?)-(?<flavor>[A-Za-z][\\w.-]*?)_[\\w.-]+(?:\\.exe)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GodotNameRegex();
}
