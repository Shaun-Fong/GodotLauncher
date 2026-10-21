using System.Globalization;

namespace GodotLauncher.Core.Catalog;

/// <summary>Parses the dates used by the official Godot website.</summary>
public static class ReleaseDates
{
    private const string WebsiteFormat = "d MMMM yyyy";

    /// <summary>
    /// Parses a date such as <c>15 October 2026</c> or <c>9 May 2023</c>.
    /// Returns <see cref="DateOnly.MinValue"/> when the text is empty or malformed.
    /// </summary>
    public static DateOnly Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateOnly.MinValue;
        }

        var value = text.Trim();
        if (DateOnly.TryParseExact(value, WebsiteFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        // Be forgiving about casing and whitespace in case the website changes.
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
        {
            return DateOnly.FromDateTime(loose);
        }

        return DateOnly.MinValue;
    }
}
