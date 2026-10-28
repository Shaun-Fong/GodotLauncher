using System.Globalization;
using System.Text;

namespace GodotLauncher.Core.Storage;

/// <summary>Severity of a log entry.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Abstraction over the launcher log, so core code stays testable.</summary>
public interface IAppLog
{
    /// <summary>Writes one entry.</summary>
    void Write(LogLevel level, string message, Exception? exception = null);
}

/// <summary>Convenience helpers for <see cref="IAppLog"/>.</summary>
public static class AppLogExtensions
{
    /// <summary>Writes an informational entry.</summary>
    public static void Info(this IAppLog log, string message) => log.Write(LogLevel.Info, message);

    /// <summary>Writes a warning entry.</summary>
    public static void Warn(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Warning, message, exception);

    /// <summary>Writes an error entry.</summary>
    public static void Error(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Error, message, exception);
}

/// <summary>Log implementation that discards everything.</summary>
public sealed class NullLog : IAppLog
{
    /// <summary>Shared instance.</summary>
    public static NullLog Instance { get; } = new();

    /// <inheritdoc />
    public void Write(LogLevel level, string message, Exception? exception = null)
    {
    }
}

/// <summary>
/// Appends entries to <c>launcher.log</c> and rotates the file when it grows past
/// a threshold. Writes are serialized so background downloads can log safely.
/// </summary>
public sealed class FileLog : IAppLog, IDisposable
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private bool _disposed;

    /// <summary>Creates a rolling log inside <paramref name="directory"/>.</summary>
    public FileLog(string directory, string fileName = "launcher.log", long maxBytes = 2 * 1024 * 1024, int maxFiles = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        _filePath = Path.Combine(directory, fileName);
        _maxBytes = Math.Max(64 * 1024, maxBytes);
        _maxFiles = Math.Clamp(maxFiles, 1, 10);
    }

    /// <summary>Path of the active log file.</summary>
    public string FilePath => _filePath;

    /// <inheritdoc />
    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var builder = new StringBuilder(160);
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        builder.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
        builder.Append(message);

        if (exception is not null)
        {
            builder.Append(" | ").Append(exception);
        }

        var line = builder.ToString();

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                RotateIfNeeded();
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never break the application.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_filePath);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = index == 1 ? _filePath : $"{_filePath}.{index - 1}";
            var destination = $"{_filePath}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;
}
