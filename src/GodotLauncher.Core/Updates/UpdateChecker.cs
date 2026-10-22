using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Updates;

/// <summary>One downloadable file of a launcher release.</summary>
public sealed record LauncherReleaseAsset(string Name, Uri DownloadUri, long Size);

/// <summary>A published launcher release that is newer than the running build.</summary>
public sealed record LauncherRelease(
    VersionNumber Version,
    string Tag,
    Uri PageUrl,
    string? Notes,
    IReadOnlyList<LauncherReleaseAsset> Assets);

/// <summary>Reads the latest release of the launcher from the GitHub API.</summary>
public sealed class UpdateChecker
{
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repository;

    /// <summary>Creates a checker for <paramref name="owner"/>/<paramref name="repository"/>.</summary>
    public UpdateChecker(HttpClient http, string owner, string repository)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        _http = http;
        _owner = owner;
        _repository = repository;
    }

    /// <summary>API endpoint of the latest release.</summary>
    public Uri LatestReleaseEndpoint => new($"https://api.github.com/repos/{_owner}/{_repository}/releases/latest");

    /// <summary>Public release page that redirects to the latest tag.</summary>
    public Uri LatestReleasePage => new($"https://github.com/{_owner}/{_repository}/releases/latest");

    /// <summary>Archive names produced by the release workflow.</summary>
    private static readonly string[] KnownAssetNames =
    [
        "GodotLauncher-win-x64.zip",
        "GodotLauncher-win-arm64.zip",
        "GodotLauncher-linux-x64.tar.gz",
        "GodotLauncher-linux-arm64.tar.gz",
        "GodotLauncher-osx-x64.tar.gz",
        "GodotLauncher-osx-arm64.tar.gz",
    ];

    /// <summary>
    /// Returns the latest release when it is newer than <paramref name="current"/>,
    /// <c>null</c> when the launcher is up to date or no release exists yet.
    /// </summary>
    /// <exception cref="HttpRequestException">The API could not be reached or refused the request.</exception>
    public async Task<LauncherRelease?> CheckAsync(VersionNumber current, CancellationToken cancellationToken = default)
    {
        try
        {
            return await CheckViaApiAsync(current, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            // The anonymous API is limited per IP address; the release page
            // redirect is not, so it keeps update checks working behind a
            // shared address.
            return await CheckViaRedirectAsync(current, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<LauncherRelease?> CheckViaApiAsync(VersionNumber current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} while checking for updates.",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<ReleasePayload>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || string.IsNullOrWhiteSpace(payload.TagName))
        {
            return null;
        }

        if (!TryParseTag(payload.TagName, out var version) || version.CompareTo(current) <= 0)
        {
            return null;
        }

        var page = Uri.TryCreate(payload.HtmlUrl, UriKind.Absolute, out var html)
            ? html
            : new Uri($"https://github.com/{_owner}/{_repository}/releases/latest");

        var assets = new List<LauncherReleaseAsset>();
        foreach (var asset in payload.Assets ?? [])
        {
            if (string.IsNullOrWhiteSpace(asset.Name) || asset.Size <= 0)
            {
                continue;
            }

            if (Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var download))
            {
                assets.Add(new LauncherReleaseAsset(asset.Name, download, asset.Size));
            }
        }

        return new LauncherRelease(version, payload.TagName, page, payload.Body, assets);
    }

    /// <summary>
    /// Falls back to the release page redirect, which is not rate limited. The
    /// archive names are predictable, so the assets can be built without the API.
    /// </summary>
    private async Task<LauncherRelease?> CheckViaRedirectAsync(VersionNumber current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleasePage);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // With automatic redirects the tag sits in the final request URI; when
        // the handler surfaces the redirect itself, the Location header carries it.
        var candidates = new List<Uri?>();
        if (response.Headers.Location is { } location)
        {
            candidates.Add(location.IsAbsoluteUri
                ? location
                : new Uri(LatestReleasePage, location));
        }

        candidates.Add(response.RequestMessage?.RequestUri);

        foreach (var candidate in candidates)
        {
            var segment = candidate?.Segments.LastOrDefault()?.Trim('/');
            if (string.IsNullOrEmpty(segment)
                || string.Equals(segment, "latest", StringComparison.OrdinalIgnoreCase)
                || !TryParseTag(segment, out var version)
                || version.CompareTo(current) <= 0)
            {
                continue;
            }

            var assets = KnownAssetNames
                .Select(name => new LauncherReleaseAsset(
                    name,
                    new Uri($"https://github.com/{_owner}/{_repository}/releases/download/{segment}/{name}"),
                    0))
                .ToList();

            var page = new Uri($"https://github.com/{_owner}/{_repository}/releases/tag/{segment}");
            return new LauncherRelease(version, segment, page, null, assets);
        }

        return null;
    }

    /// <summary>Parses a tag such as <c>v0.2.0</c> into a version number.</summary>
    public static bool TryParseTag(string? tag, out VersionNumber version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        return VersionNumber.TryParse(text, out version);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private sealed class ReleasePayload
    {
        public string? TagName { get; set; }

        public string? HtmlUrl { get; set; }

        public string? Body { get; set; }

        public List<AssetPayload>? Assets { get; set; }
    }

    private sealed class AssetPayload
    {
        public string? Name { get; set; }

        public string? BrowserDownloadUrl { get; set; }

        public long Size { get; set; }
    }
}
