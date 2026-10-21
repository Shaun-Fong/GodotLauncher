using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>
/// Reads the catalog from a YAML document such as the official
/// <c>versions.yml</c> or its jsDelivr mirror.
/// </summary>
public sealed partial class VersionsYmlSource : ICatalogSource
{
    private readonly Uri _uri;
    private readonly VersionsYmlParser _parser;

    /// <summary>Creates a source for the given YAML URL.</summary>
    public VersionsYmlSource(string id, Uri uri, VersionsYmlParser? parser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(uri);

        Id = id;
        _uri = uri;
        _parser = parser ?? new VersionsYmlParser();
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public async Task<CatalogFetch> FetchAsync(HttpClient http, string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
        if (!string.IsNullOrWhiteSpace(etag))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new CatalogFetch { Source = Id, ETag = etag, NotModified = true };
        }

        response.EnsureSuccessStatusCode();
        var yaml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var versions = _parser.Parse(yaml);

        return new CatalogFetch
        {
            Versions = versions,
            Source = Id,
            ETag = response.Headers.ETag?.Tag,
        };
    }
}

/// <summary>
/// Emergency fallback that scrapes the public archive page. Only used when both
/// YAML sources fail; it yields version names without release dates or notes.
/// </summary>
public sealed partial class HtmlArchiveSource : ICatalogSource
{
    private static readonly Uri DefaultUri = new("https://godotengine.org/download/archive/");

    private readonly Uri _uri;

    /// <summary>Creates the scraper for the archive page.</summary>
    public HtmlArchiveSource(Uri? uri = null) => _uri = uri ?? DefaultUri;

    /// <inheritdoc />
    public string Id => "html-archive";

    /// <inheritdoc />
    public async Task<CatalogFetch> FetchAsync(HttpClient http, string? etag, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(_uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var versions = new List<GodotVersion>();
        foreach (Match match in ArchiveLinkRegex().Matches(html))
        {
            var name = match.Groups["version"].Value;
            var flavor = match.Groups["flavor"].Value;
            if (VersionNumber.TryParse(name, out _))
            {
                versions.Add(GodotVersion.Create(name, flavor));
            }
        }

        if (versions.Count == 0)
        {
            throw new InvalidOperationException("The archive page contained no version links.");
        }

        return new CatalogFetch
        {
            Versions = CatalogNormalizer.Normalize(versions),
            Source = Id,
            ETag = etag,
        };
    }

    [GeneratedRegex(
        "download/archive/(?<version>\\d+\\.\\d+(?:\\.\\d+)?)-(?<flavor>stable|rc\\d+|beta\\d+|dev\\d+|alpha[\\w-]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveLinkRegex();
}
