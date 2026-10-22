using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Install;

/// <summary>Finds the editor executable inside an extracted archive.</summary>
public static class ExecutableFinder
{
    // Mono archives wrap their payload in a folder named after the build, so a
    // shallow search is needed in addition to the archive root itself.
    private const int MaximumDepth = 2;

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

        foreach (var root in EnumerateCandidateRoots(directory))
        {
            var relative = platform switch
            {
                HostPlatform.MacOS => FindMacApp(directory, root),
                HostPlatform.Windows => FindWindowsExecutable(directory, root),
                _ => FindUnixExecutable(directory, root),
            };

            if (relative is not null)
            {
                return relative;
            }
        }

        return null;
    }

    /// <summary>Yields the archive root followed by its shallowest subfolders.</summary>
    private static IEnumerable<string> EnumerateCandidateRoots(string directory)
    {
        yield return directory;

        var frontier = new List<string> { directory };
        for (var depth = 0; depth < MaximumDepth; depth++)
        {
            var next = new List<string>();
            foreach (var parent in frontier)
            {
                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(parent);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    next.Add(child);
                    yield return child;
                }
            }

            if (next.Count == 0)
            {
                yield break;
            }

            frontier = next;
        }
    }

    private static string? FindWindowsExecutable(string directory, string root)
    {
        var console = (string?)null;
        foreach (var file in EnumerateFiles(root, "*.exe"))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("console", StringComparison.OrdinalIgnoreCase))
            {
                console ??= file;
                continue;
            }

            return Path.GetRelativePath(directory, file);
        }

        // A console build alone is still usable, it just opens a terminal window.
        return console is null ? null : Path.GetRelativePath(directory, console);
    }

    private static string? FindUnixExecutable(string directory, string root)
    {
        foreach (var file in EnumerateFiles(root, "*"))
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

            return Path.GetRelativePath(directory, file);
        }

        return null;
    }

    private static string? FindMacApp(string directory, string root)
    {
        var apps = EnumerateDirectories(root, "*.app").ToList();
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

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
    }
}
