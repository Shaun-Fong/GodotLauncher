using System.Text;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Projects;

/// <summary>
/// The parts of a <c>project.godot</c> file the launcher needs: the display name,
/// the editor version recorded in the feature list and the main scene.
/// </summary>
public sealed record GodotProjectFile
{
    /// <summary>Name of the project configuration file.</summary>
    public const string FileName = "project.godot";

    /// <summary>Full path of the parsed <c>project.godot</c>.</summary>
    public required string FilePath { get; init; }

    /// <summary>Value of <c>application/config/name</c>, when present.</summary>
    public string? Name { get; init; }

    /// <summary>Editor version recorded in <c>application/config/features</c>.</summary>
    public VersionNumber? Version { get; init; }

    /// <summary>Textual form of <see cref="Version"/>, for display and matching.</summary>
    public string? VersionText { get; init; }

    /// <summary>Godot project file format version (4 for Godot 3, 5 for Godot 4).</summary>
    public int ConfigVersion { get; init; }

    /// <summary>Raw feature strings declared by the project.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Value of <c>application/run/main_scene</c>, when present.</summary>
    public string? MainScene { get; init; }

    /// <summary>Directory that contains the project file.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(FilePath) ?? FilePath;

    /// <summary>Best display name: the configured name, then the folder name.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileName(Directory) : Name;

    /// <summary>True when the file name is <c>project.godot</c>.</summary>
    public static bool IsProjectFile(string path) =>
        string.Equals(System.IO.Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rewrites the recorded version, keeping every other value.</summary>
    public GodotProjectFile WithVersion(VersionNumber version, string versionText) =>
        this with { Version = version, VersionText = versionText };

    /// <summary>Parses the content of a project file.</summary>
    public static GodotProjectFile Parse(string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var section = string.Empty;
        var name = string.Empty;
        var mainScene = string.Empty;
        var features = new List<string>();
        var configVersion = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (section.Length == 0)
            {
                if (key == "config_version")
                {
                    int.TryParse(value, out configVersion);
                }

                continue;
            }

            if (!section.Equals("application", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (key)
            {
                case "config/name":
                    name = ParseString(value);
                    break;
                case "run/main_scene":
                    mainScene = ParseString(value);
                    break;
                case "config/features":
                    features.AddRange(ParseStringArray(value));
                    break;
            }
        }

        var versionText = features.FirstOrDefault(feature => VersionNumber.TryParse(feature, out _));
        VersionNumber? version = versionText is not null && VersionNumber.TryParse(versionText, out var parsed)
            ? parsed
            : null;

        if (versionText is null && configVersion > 0)
        {
            versionText = configVersion >= 5 ? "4" : "3";
        }

        return new GodotProjectFile
        {
            FilePath = System.IO.Path.GetFullPath(filePath),
            Name = string.IsNullOrWhiteSpace(name) ? null : name,
            Version = version,
            VersionText = versionText,
            ConfigVersion = configVersion,
            Features = features,
            MainScene = string.IsNullOrWhiteSpace(mainScene) ? null : mainScene,
        };
    }

    /// <summary>Reads and parses a project file from disk.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static GodotProjectFile Load(string filePath) => Parse(filePath, File.ReadAllText(filePath, Encoding.UTF8));

    private static string ParseString(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => value[i],
                });
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static IEnumerable<string> ParseStringArray(string value)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            yield break;
        }

        var inner = value[(open + 1)..close];
        var index = 0;
        while (index < inner.Length)
        {
            var quote = inner.IndexOf('"', index);
            if (quote < 0)
            {
                yield break;
            }

            var builder = new StringBuilder();
            index = quote + 1;
            while (index < inner.Length && inner[index] != '"')
            {
                if (inner[index] == '\\' && index + 1 < inner.Length)
                {
                    index++;
                    builder.Append(inner[index] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        _ => inner[index],
                    });
                    index++;
                    continue;
                }

                builder.Append(inner[index]);
                index++;
            }

            index++;
            yield return builder.ToString();
        }
    }
}
