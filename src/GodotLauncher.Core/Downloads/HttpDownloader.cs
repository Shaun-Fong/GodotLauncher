using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace GodotLauncher.Core.Downloads;

/// <summary>
/// Downloads archives with resume support: bytes are written to a
/// <c>.part</c> file and continued with an HTTP <c>Range</c> request after an
/// interruption. Finished archives are hashed and moved into place atomically.
/// </summary>
public sealed class HttpDownloader
{
    private const int CopyBufferSize = 128 * 1024;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    private readonly HttpClient _http;

    /// <summary>Creates the downloader over the shared HTTP client.</summary>
    public HttpDownloader(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// Downloads <paramref name="request"/> and returns the verified archive.
    /// Transient network failures are retried with exponential backoff before
    /// the fallback URL is used.
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var directory = Path.GetDirectoryName(request.DestinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partPath = request.DestinationPath + ".part";

        if (TryUseExistingArchive(request, out var existing))
        {
            return existing;
        }

        var uris = new List<Uri> { request.Uri };
        if (request.FallbackUri is not null && request.FallbackUri != request.Uri)
        {
            uris.Add(request.FallbackUri);
        }

        var attempts = Math.Max(1, request.MaxAttempts);
        var errors = new List<string>();

        foreach (var uri in uris)
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await TransferAsync(uri, partPath, request, progress, cancellationToken).ConfigureAwait(false);
                    return await CompleteAsync(partPath, request, uri, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (DownloadException ex) when (ex.IsFatal)
                {
                    // Wrong hash or a definitively missing file: never retry.
                    throw;
                }
                catch (HttpRequestException ex) when (!IsRetryable(ex))
                {
                    errors.Add($"{uri} -> {ex.StatusCode?.ToString() ?? ex.Message}");
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                    errors.Add($"{uri} -> {ex.Message}");
                    if (attempt < attempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        throw new DownloadException(
            "The archive could not be downloaded. " + string.Join(" | ", errors));
    }

    private async Task TransferAsync(
        Uri uri,
        string partPath,
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        if (existing > 0)
        {
            httpRequest.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var response = await _http
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (request.ExpectedSize is > 0 && existing == request.ExpectedSize)
            {
                return;
            }

            TryDelete(partPath);
            throw new IOException("The server rejected the resume offset; starting over.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // EnsureSuccessStatusCode gives us a message and a status we can classify.
            response.EnsureSuccessStatusCode();
        }

        var appending = response.StatusCode == HttpStatusCode.PartialContent && existing > 0;
        if (!appending)
        {
            // Either a fresh download, or the server ignored the Range header.
            existing = 0;
            TryDelete(partPath);
        }

        var contentLength = response.Content.Headers.ContentLength;
        long? total = contentLength is null ? request.ExpectedSize : existing + contentLength;
        if (request.ExpectedSize is > 0)
        {
            total = request.ExpectedSize;
        }

        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var lastBytes = existing;
        var received = existing;
        var speed = 0d;

        Report(received, total, speed, force: true);

        await using (var destination = new FileStream(
            partPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            destination.Seek(existing, SeekOrigin.Begin);

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[CopyBufferSize];

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                var now = stopwatch.Elapsed;
                var elapsed = (now - lastReport).TotalSeconds;
                if (elapsed >= ProgressInterval.TotalSeconds)
                {
                    var instant = (received - lastBytes) / elapsed;
                    speed = speed <= 0 ? instant : (speed * 0.7) + (instant * 0.3);
                    lastBytes = received;
                    lastReport = now;
                    Report(received, total, speed, force: false);
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        var finalSize = new FileInfo(partPath).Length;
        if (request.ExpectedSize is > 0 && finalSize != request.ExpectedSize)
        {
            TryDelete(partPath);
            throw new IOException(
                $"The archive size did not match: expected {request.ExpectedSize} bytes but received {finalSize}.");
        }

        Report(finalSize, finalSize, 0, force: true);

        void Report(long bytes, long? bytesTotal, double bytesPerSecond, bool force)
        {
            if (progress is null || (!force && bytesPerSecond <= 0))
            {
                return;
            }

            var remaining = bytesTotal is > 0 && bytesPerSecond > 0
                ? TimeSpan.FromSeconds(Math.Max(0, (bytesTotal.Value - bytes) / bytesPerSecond))
                : (TimeSpan?)null;

            progress.Report(new DownloadProgress
            {
                BytesReceived = bytes,
                TotalBytes = bytesTotal,
                BytesPerSecond = bytesPerSecond,
                EstimatedRemaining = remaining,
            });
        }
    }

    private static bool TryUseExistingArchive(DownloadRequest request, out DownloadResult result)
    {
        result = null!;
        var path = request.DestinationPath;
        if (!File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        if (request.ExpectedSize is > 0 && info.Length != request.ExpectedSize)
        {
            TryDelete(path);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            var digest = ComputeSha256(path);
            if (!string.Equals(digest, request.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(path);
                return false;
            }

            result = new DownloadResult
            {
                FilePath = path,
                Size = info.Length,
                Sha256 = digest,
                Verified = true,
                UsedUri = request.Uri,
            };

            return true;
        }

        result = new DownloadResult
        {
            FilePath = path,
            Size = info.Length,
            Sha256 = null,
            Verified = false,
            UsedUri = request.Uri,
        };

        return true;
    }

    private static async Task<DownloadResult> CompleteAsync(
        string partPath,
        DownloadRequest request,
        Uri usedUri,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var size = new FileInfo(partPath).Length;
        string digest;
        await using (var stream = File.OpenRead(partPath))
        {
            digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                .ToLowerInvariant();
        }

        var verified = false;
        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            verified = string.Equals(digest, request.ExpectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!verified)
            {
                TryDelete(partPath);
                throw new DownloadException(
                    $"Checksum mismatch for {Path.GetFileName(request.DestinationPath)}: expected {request.ExpectedSha256}, got {digest}.",
                    fatal: true);
            }
        }

        File.Move(partPath, request.DestinationPath, overwrite: true);
        progress?.Report(new DownloadProgress
        {
            BytesReceived = size,
            TotalBytes = size,
            BytesPerSecond = 0,
        });

        return new DownloadResult
        {
            FilePath = request.DestinationPath,
            Size = size,
            Sha256 = digest,
            Verified = verified,
            UsedUri = usedUri,
        };
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsRetryable(HttpRequestException exception)
    {
        var status = exception.StatusCode;
        if (status is null)
        {
            // No status code means a transport level failure, which is retryable.
            return true;
        }

        return status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
