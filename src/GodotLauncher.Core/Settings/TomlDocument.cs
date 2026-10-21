using System.Globalization;
using System.Text;

namespace GodotLauncher.Core.Settings;

/// <summary>
/// Minimal TOML reader/writer covering exactly what <c>config.toml</c> needs:
/// sections, string, boolean and integer values plus <c>#</c> comments.
/// Keeping this in-house avoids a dependency for a 40 line document and lets the
/// launcher tolerate hand edited files.
/// </summary>
public sealed class TomlDocument
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    private TomlDocument()
    {
    }

    /// <summary>Parses a TOML document.</summary>
    /// <exception cref="FormatException">A line is not valid TOML.</exception>
    public static TomlDocument Parse(string text)
    {
        var document = new TomlDocument();
        var section = string.Empty;
        var lineNumber = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']'))
                {
                    throw new FormatException($"Line {lineNumber}: malformed section header.");
                }

                section = line[1..^1].Trim();
                if (section.Length == 0)
                {
                    throw new FormatException($"Line {lineNumber}: empty section name.");
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new FormatException($"Line {lineNumber}: expected key = value.");
            }

            var key = line[..separator].Trim();
            var value = StripComment(line[(separator + 1)..]).Trim();
            if (key.Length == 0)
            {
                throw new FormatException($"Line {lineNumber}: empty key.");
            }

            document._values[$"{section}.{key}"] = value;
        }

        return document;
    }

    /// <summary>Reads a string value.</summary>
    public bool TryGetString(string section, string key, out string value)
    {
        value = string.Empty;
        if (!_values.TryGetValue($"{section}.{key}", out var raw))
        {
            return false;
        }

        value = Unquote(raw);
        return true;
    }

    /// <summary>Reads a boolean value.</summary>
    public bool TryGetBool(string section, string key, out bool value)
    {
        value = false;
        if (!_values.TryGetValue($"{section}.{key}", out var raw))
        {
            return false;
        }

        return bool.TryParse(Unquote(raw), out value);
    }

    /// <summary>Reads an integer value.</summary>
    public bool TryGetInt(string section, string key, out int value)
    {
        value = 0;
        if (!_values.TryGetValue($"{section}.{key}", out var raw))
        {
            return false;
        }

        return int.TryParse(Unquote(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads an enum value by name, ignoring case.</summary>
    public bool TryGetEnum<TEnum>(string section, string key, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        if (!TryGetString(section, key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out value);
    }

    private static string StripComment(string value)
    {
        var inSingle = false;
        var inDouble = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (character == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (character == '#' && !inSingle && !inDouble)
            {
                return value[..index];
            }
        }

        return value;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return Unescape(value[1..^1]);
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index == value.Length - 1)
            {
                builder.Append(character);
                continue;
            }

            index++;
            builder.Append(value[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '"' => '"',
                _ => value[index],
            });
        }

        return builder.ToString();
    }

    /// <summary>Escapes a string for a double quoted TOML value.</summary>
    public static string Quote(string? value)
    {
        var text = value ?? string.Empty;
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
