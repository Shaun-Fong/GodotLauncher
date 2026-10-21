using System.Net;
using GodotLauncher.Core.Settings;

namespace GodotLauncher.Core.Net;

/// <summary>Builds HTTP clients that honor the user's proxy preference.</summary>
public static class ProxySupport
{
    /// <summary>Application version used in the User-Agent header.</summary>
    public static string UserAgent { get; set; } =
        $"GodotLauncher/{typeof(ProxySupport).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    /// <summary>
    /// Creates a client for catalog, API and download traffic.
    /// <paramref name="decompress"/> must stay <c>false</c> for archive
    /// downloads so content lengths keep matching the file size.
    /// </summary>
    public static HttpClient CreateClient(LauncherSettings settings, bool decompress, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = decompress ? DecompressionMethods.All : DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        switch (settings.Proxy)
        {
            case ProxyMode.None:
                handler.UseProxy = false;
                break;

            case ProxyMode.Custom:
                var proxy = ParseProxy(settings.ProxyUrl);
                if (proxy is null)
                {
                    // Fail closed: a mistyped proxy must not silently fall back
                    // to the system proxy, which is what confused users report.
                    handler.UseProxy = false;
                }
                else
                {
                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }

                break;
        }

        var client = new HttpClient(handler, disposeHandler: true)
        {
            // An infinite timeout lets the downloader enforce per-request timeouts itself.
            Timeout = timeout ?? Timeout.InfiniteTimeSpan,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>
    /// Parses a proxy URL. Only absolute HTTP and HTTPS URLs are accepted; a
    /// missing scheme is assumed to be HTTP.
    /// </summary>
    public static IWebProxy? ParseProxy(string? proxyUrl)
    {
        if (string.IsNullOrWhiteSpace(proxyUrl))
        {
            return null;
        }

        var text = proxyUrl.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var webProxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // Credentials embedded in the URL are not read by WebProxy itself.
            var parts = uri.UserInfo.Split(':', 2);
            webProxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        return webProxy;
    }
}
