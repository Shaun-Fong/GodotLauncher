using System.Globalization;

namespace GodotLauncher.Core.Models;

/// <summary>
/// A Godot version number such as 4.7.2. Only the numeric part is stored; the
/// channel and stage live in the flavor (for example <c>4.8-beta1</c>).
/// </summary>
public readonly record struct VersionNumber(int Major, int Minor, int Patch) : IComparable<VersionNumber>
{
    /// <summary>Parses a dotted version such as <c>4.7.2</c>, <c>4.8</c> or <c>v3.6</c>.</summary>
    public static bool TryParse(string? text, out VersionNumber value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        // Ignore any suffix that is not numeric, e.g. "4.8-beta1".
        var dash = span.IndexOfAny(['-', '+', ' ']);
        if (dash >= 0)
        {
            span = span[..dash];
        }

        var parts = span.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        if (!TryReadPart(parts[0], out var major))
        {
            return false;
        }

        var minor = 0;
        var patch = 0;
        if (parts.Length > 1 && !TryReadPart(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length > 2 && !TryReadPart(parts[2], out patch))
        {
            return false;
        }

        value = new VersionNumber(major, minor, patch);
        return true;
    }

    /// <summary>Parses a dotted version, throwing when the text is not numeric.</summary>
    public static VersionNumber Parse(string text) =>
        TryParse(text, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid Godot version number.");

    /// <inheritdoc />
    public int CompareTo(VersionNumber other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    /// <summary>True when this version is at least <paramref name="other"/>.</summary>
    public bool IsAtLeast(VersionNumber other) => CompareTo(other) >= 0;

    /// <inheritdoc />
    public override string ToString() =>
        Patch == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    private static bool TryReadPart(string part, out int value) =>
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
