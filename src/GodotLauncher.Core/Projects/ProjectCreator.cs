using System.Text;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Projects;

/// <summary>Creates minimal Godot projects that the editor can open and import.</summary>
public static class ProjectCreator
{
    /// <summary>
    /// Creates <paramref name="directory"/> and a <c>project.godot</c> inside it.
    /// The recorded editor version comes from <paramref name="version"/> when supplied.
    /// </summary>
    /// <exception cref="ProjectException">A project file already exists in the directory.</exception>
    public static GodotProjectFile Create(string directory, string name, VersionNumber? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, GodotProjectFile.FileName);
        if (File.Exists(filePath))
        {
            throw new ProjectException($"'{filePath}' already exists.");
        }

        var major = version?.Major ?? 4;
        var minor = version?.Minor ?? 0;
        var configVersion = major >= 4 ? 5 : 4;
        var versionText = $"{major}.{minor}";
        var escapedName = name
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        var content = new StringBuilder()
            .Append("; Engine configuration file, created by Godot Launcher.").Append('\n')
            .Append("config_version=").Append(configVersion).Append('\n')
            .Append('\n')
            .Append("[application]").Append('\n')
            .Append('\n')
            .Append("config/name=\"").Append(escapedName).Append('"').Append('\n')
            .Append("config/features=PackedStringArray(\"").Append(versionText).Append("\", \"GL Compatibility\")").Append('\n')
            .ToString();

        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return GodotProjectFile.Parse(filePath, content);
    }
}
