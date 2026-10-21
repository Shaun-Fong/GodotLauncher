using System.IO.Compression;

namespace GodotLauncher.Core.Install;

/// <summary>Thrown when an archive cannot be installed.</summary>
public sealed class InstallException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public InstallException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Extracts Godot archives while defending against zip-slip paths and symbolic
/// links, then locates the editor executable by content rather than by guessing
/// a file name.
/// </summary>
public static class ZipExtractor
{
    /// <summary>Unix file type bits stored in the upper half of <c>ExternalAttributes</c>.</summary>
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    /// <summary>Extracts <paramref name="archivePath"/> into <paramref name="destinationDirectory"/>.</summary>
    /// <exception cref="InstallException">The archive contains an unsafe entry.</exception>
    public static void Extract(string archivePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var relative = entry.FullName.Replace('\\', '/');
                if (relative.Length == 0)
                {
                    continue;
                }

                if (relative.Contains('\0') || relative.StartsWith('/') || relative.Contains(':'))
                {
                    throw new InstallException($"The archive entry '{entry.FullName}' has an unsafe path.");
                }

                var mode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                if ((mode & UnixFileTypeMask) == UnixSymbolicLink)
                {
                    throw new InstallException($"The archive entry '{entry.FullName}' is a symbolic link, which is not allowed.");
                }

                var target = Path.GetFullPath(Path.Combine(root, relative));
                if (!IsInside(root, target))
                {
                    throw new InstallException($"The archive entry '{entry.FullName}' escapes the installation directory.");
                }

                // Directory entries have no content and end with a separator.
                if (relative.EndsWith('/') || (entry.Length == 0 && entry.Name.Length == 0))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(target, overwrite: true);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InstallException("The downloaded file is not a valid ZIP archive.", ex);
        }
    }

    private static bool IsInside(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
