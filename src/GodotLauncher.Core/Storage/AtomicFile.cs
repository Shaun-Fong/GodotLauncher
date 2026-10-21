using System.Text;

namespace GodotLauncher.Core.Storage;

/// <summary>
/// Writes files through a temporary sibling and an atomic replace, so a crash
/// or power loss can never leave a half-written configuration behind.
/// </summary>
public static class AtomicFile
{
    /// <summary>Writes UTF-8 text atomically.</summary>
    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
        await WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes bytes atomically.</summary>
    public static async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>Synchronous variant used during shutdown.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
        WriteAllBytes(path, bytes);
    }

    /// <summary>Synchronous variant used during shutdown.</summary>
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temporary file is harmless and cleaned up on next start.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
