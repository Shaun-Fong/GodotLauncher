using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Install;

/// <summary>Finds the editor executable inside an extracted archive.</summary>
public static class ExecutableFinder
{
    /// <summary>
    /// Locates the editor binary relative to <paramref name="directory"/>, or
    /// returns <c>null</c> when the archive does not contain a recognizable one.
    /// </summary>
    public static string? Find(string directory, HostPlatform platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return platform switch
        {
            HostPlatform.MacOS => FindMacApp(directory),
            HostPlatform.Windows => FindWindowsExecutable(directory),
            _ => FindUnixExecutable(directory),
        };
    }

    private static string? FindWindowsExecutable(string directory)
    {
        var console = (string?)null;
        foreach (var file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("console", StringComparison.OrdinalIgnoreCase))
            {
                console ??= name;
                continue;
            }

            return name;
        }

        // A console build alone is still usable, it just opens a terminal window.
        return console;
    }

    private static string? FindUnixExecutable(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("Godot", StringComparison.Ordinal))
            {
                continue;
            }

            if (name.Contains("console", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return name;
        }

        return null;
    }

    private static string? FindMacApp(string directory)
    {
        var apps = Directory.EnumerateDirectories(directory, "*.app", SearchOption.TopDirectoryOnly).ToList();
        if (apps.Count == 0)
        {
            return null;
        }

        var app = apps.FirstOrDefault(a =>
            string.Equals(Path.GetFileNameWithoutExtension(a), "Godot", StringComparison.OrdinalIgnoreCase))
            ?? apps[0];

        var binaryDirectory = Path.Combine(app, "Contents", "MacOS");
        if (!Directory.Exists(binaryDirectory))
        {
            return null;
        }

        var binary = Directory.EnumerateFiles(binaryDirectory, "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (binary is null)
        {
            return null;
        }

        return Path.GetRelativePath(directory, binary);
    }
}
