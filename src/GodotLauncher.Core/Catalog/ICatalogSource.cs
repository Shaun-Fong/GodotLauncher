using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>The outcome of one request against a catalog source.</summary>
public sealed record CatalogFetch
{
    /// <summary>Normalized versions returned by the source.</summary>
    public IReadOnlyList<GodotVersion> Versions { get; init; } = [];

    /// <summary>ETag for a later conditional request, when the source provides one.</summary>
    public string? ETag { get; init; }

    /// <summary>Identifier of the source that produced the data.</summary>
    public string Source { get; init; } = "unknown";

    /// <summary>True when the server answered <c>304 Not Modified</c>.</summary>
    public bool NotModified { get; init; }
}

/// <summary>A place the launcher can obtain the release catalog from.</summary>
public interface ICatalogSource
{
    /// <summary>Stable identifier persisted with the cached catalog.</summary>
    string Id { get; }

    /// <summary>
    /// Fetches the catalog, optionally conditionally on <paramref name="etag"/>.
    /// Implementations should throw on transport or parse errors so the caller
    /// can fall back to the next source.
    /// </summary>
    Task<CatalogFetch> FetchAsync(HttpClient http, string? etag, CancellationToken cancellationToken);
}
