using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace GodotLauncher.App.Services;

/// <summary>
/// Downloads and decodes blog cover images. Results (including failures) are cached per URL so
/// paging back and forth does not fetch the same thumbnail twice.
/// </summary>
public static class ThumbnailLoader
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Gate = new(4);

    /// <summary>Returns the decoded image, or null when the download failed.</summary>
    public static async Task<Bitmap?> LoadAsync(HttpClient http, string url, CancellationToken cancellationToken = default)
    {
        if (Cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(url, out cached))
            {
                return cached;
            }

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Cache[url] = null;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            var bitmap = Bitmap.DecodeToWidth(buffer, 320);
            Cache[url] = bitmap;
            return bitmap;
        }
        catch (Exception)
        {
            Cache[url] = null;
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
