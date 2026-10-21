using System.Text.Json.Serialization;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Projects;

/// <summary>Thrown when a project cannot be created, imported or opened.</summary>
public sealed class ProjectException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public ProjectException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>A Godot project known to the launcher.</summary>
public sealed record ProjectRecord
{
    /// <summary>Full path of the project directory, used as the identity.</summary>
    public required string Path { get; init; }

    /// <summary>Name shown in the project list.</summary>
    public required string Name { get; init; }

    /// <summary>Editor version recorded by the project file, when known.</summary>
    public string? VersionText { get; init; }

    /// <summary>Installation id of the editor chosen to open the project.</summary>
    public string? EditorId { get; init; }

    /// <summary>When the project was added to the list.</summary>
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>When the project was last opened from the launcher.</summary>
    public DateTimeOffset? LastOpened { get; init; }

    /// <summary>True when the project file still exists on disk.</summary>
    [JsonIgnore]
    public bool Exists => File.Exists(System.IO.Path.Combine(Path, GodotProjectFile.FileName));

    /// <summary>True when the recorded version is a complete major.minor pair.</summary>
    public bool TryGetVersion(out VersionNumber version) => VersionNumber.TryParse(VersionText, out version);

    /// <summary>Normalizes a directory path for storage and comparison.</summary>
    public static string NormalizePath(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    /// <summary>Builds a record from a parsed project file.</summary>
    public static ProjectRecord FromProject(GodotProjectFile project, string? editorId = null) =>
        new()
        {
            Path = NormalizePath(project.Directory),
            Name = project.DisplayName,
            VersionText = project.VersionText,
            EditorId = editorId,
        };
}
