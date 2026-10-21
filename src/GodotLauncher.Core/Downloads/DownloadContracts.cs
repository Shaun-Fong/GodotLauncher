namespace GodotLauncher.Core.Downloads;

/// <summary>A snapshot of download progress, suitable for binding to the UI.</summary>
public sealed record DownloadProgress
{
    /// <summary>Bytes stored on disk so far (including bytes from an earlier run).</summary>
    public long BytesReceived { get; init; }

    /// <summary>Total size of the archive when advertised by the server.</summary>
    public long? TotalBytes { get; init; }

    /// <summary>Current transfer rate in bytes per second.</summary>
    public double BytesPerSecond { get; init; }

    /// <summary>Estimated time until completion.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }

    /// <summary>Completion ratio between 0 and 1, or <c>null</c> when unknown.</summary>
    public double? Fraction =>
        TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;

    /// <summary>Human readable transfer rate, e.g. <c>12.4 MB/s</c>.</summary>
    public string SpeedText => BytesPerSecond <= 0 ? "—" : $"{FormatBytes((long)BytesPerSecond)}/s";

    /// <summary>Formats a byte count using binary units.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Abs(bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var sign = bytes < 0 ? "-" : string.Empty;
        return unit == 0
            ? $"{sign}{(long)value} {units[unit]}"
            : $"{sign}{value:0.#} {units[unit]}";
    }
}

/// <summary>Description of a single archive download.</summary>
public sealed record DownloadRequest
{
    /// <summary>URL of the archive.</summary>
    public required Uri Uri { get; init; }

    /// <summary>URL to use when the primary one is unavailable.</summary>
    public Uri? FallbackUri { get; init; }

    /// <summary>Final path of the verified archive (without the <c>.part</c> suffix).</summary>
    public required string DestinationPath { get; init; }

    /// <summary>Expected size in bytes when known; used to detect a finished <c>.part</c> file.</summary>
    public long? ExpectedSize { get; init; }

    /// <summary>Expected SHA-256 digest in hexadecimal when known.</summary>
    public string? ExpectedSha256 { get; init; }

    /// <summary>Maximum number of transport attempts before failing.</summary>
    public int MaxAttempts { get; init; } = 3;
}

/// <summary>The outcome of a completed download.</summary>
public sealed record DownloadResult
{
    /// <summary>Path of the verified archive.</summary>
    public required string FilePath { get; init; }

    /// <summary>Number of bytes in the archive.</summary>
    public long Size { get; init; }

    /// <summary>SHA-256 digest of the archive, lowercase hexadecimal.</summary>
    public string? Sha256 { get; init; }

    /// <summary>True when the digest matched the expected value.</summary>
    public bool Verified { get; init; }

    /// <summary>URL that finally served the archive.</summary>
    public required Uri UsedUri { get; init; }
}

/// <summary>Thrown when an archive could not be downloaded or verified.</summary>
public sealed class DownloadException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public DownloadException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>Creates the exception and marks whether retrying is pointless.</summary>
    public DownloadException(string message, bool fatal, Exception? inner = null)
        : base(message, inner) => IsFatal = fatal;

    /// <summary>
    /// True when the failure cannot be fixed by retrying, for example a checksum
    /// mismatch or an archive that no longer exists.
    /// </summary>
    public bool IsFatal { get; private init; }
}
