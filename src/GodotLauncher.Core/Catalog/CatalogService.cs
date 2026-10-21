using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>How a refresh attempt ended.</summary>
public enum CatalogRefreshStatus
{
    /// <summary>A source returned a fresh catalog.</summary>
    Updated,

    /// <summary>The primary source confirmed the cached catalog is current.</summary>
    NotModified,

    /// <summary>Every source failed; the cached catalog (if any) remains in use.</summary>
    Failed,

    /// <summary>A source returned suspiciously little data, so the cache was kept.</summary>
    Rejected,
}

/// <summary>Result of one catalog refresh attempt.</summary>
public sealed record CatalogRefreshResult
{
    /// <summary>Outcome of the attempt.</summary>
    public required CatalogRefreshStatus Status { get; init; }

    /// <summary>Catalog that should now be displayed.</summary>
    public VersionCatalog? Catalog { get; init; }

    /// <summary>Human readable detail, suitable for a status bar.</summary>
    public string? Message { get; init; }

    /// <summary>Ids that were not present in the previous catalog.</summary>
    public IReadOnlySet<string> NewVersionIds { get; init; } = new HashSet<string>();

    /// <summary>True when the user should see a "new versions" indicator.</summary>
    public bool HasNewVersions => NewVersionIds.Count > 0;
}

/// <summary>
/// Fetches the catalog from a prioritized chain of sources and keeps the local
/// cache in sync. Each source is tried in order; transport failures fall through
/// to the next one, while a successful but truncated response is rejected in
/// favor of the cache.
/// </summary>
public sealed class CatalogService
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<ICatalogSource> _sources;
    private readonly CatalogCache _cache;

    /// <summary>Creates the service with a source chain (ordered by preference).</summary>
    public CatalogService(HttpClient http, IReadOnlyList<ICatalogSource> sources, CatalogCache cache)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(cache);

        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one catalog source is required.", nameof(sources));
        }

        _http = http;
        _sources = sources;
        _cache = cache;
    }

    /// <summary>Loads the cached catalog, if one exists.</summary>
    public Task<VersionCatalog?> LoadCachedAsync(CancellationToken cancellationToken = default) =>
        _cache.LoadAsync(cancellationToken);

    /// <summary>
    /// Refreshes the catalog, conditionally when the previous catalog came from
    /// the same source. Never throws for network problems: the failure is
    /// reported in the result so the UI can show a non-blocking warning.
    /// </summary>
    public async Task<CatalogRefreshResult> RefreshAsync(
        VersionCatalog? previous,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Contacting {source.Id}...");

            // Only send a conditional request to the source that produced the cache.
            var etag = previous is not null && string.Equals(previous.Source, source.Id, StringComparison.Ordinal)
                ? previous.ETag
                : null;

            try
            {
                var fetch = await source.FetchAsync(_http, etag, cancellationToken).ConfigureAwait(false);

                if (fetch.NotModified)
                {
                    var unchanged = previous is null
                        ? null
                        : previous with { FetchedAt = DateTimeOffset.UtcNow, IsFromCache = false };

                    return new CatalogRefreshResult
                    {
                        Status = CatalogRefreshStatus.NotModified,
                        Catalog = unchanged ?? previous,
                        Message = "The version list is up to date.",
                    };
                }

                var versions = CatalogNormalizer.Normalize(fetch.Versions);
                if (versions.Count == 0)
                {
                    throw new InvalidOperationException("The source returned an empty catalog.");
                }

                if (previous is not null && CatalogNormalizer.LooksLikeTruncatedCatalog(previous.Count, versions.Count))
                {
                    return new CatalogRefreshResult
                    {
                        Status = CatalogRefreshStatus.Rejected,
                        Catalog = previous,
                        Message = $"The downloaded catalog only contained {versions.Count} of {previous.Count} versions; keeping the cached list.",
                    };
                }

                var catalog = new VersionCatalog
                {
                    Versions = versions,
                    FetchedAt = DateTimeOffset.UtcNow,
                    Source = source.Id,
                    ETag = fetch.ETag,
                };

                await _cache.SaveAsync(catalog, cancellationToken).ConfigureAwait(false);

                var newIds = FindNewIds(previous, versions);
                return new CatalogRefreshResult
                {
                    Status = CatalogRefreshStatus.Updated,
                    Catalog = catalog,
                    NewVersionIds = newIds,
                    Message = newIds.Count == 0
                        ? $"Version list refreshed from {source.Id}."
                        : $"{newIds.Count} new version(s) found.",
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{source.Id}: {ex.Message}");
                progress?.Report($"{source.Id} failed, trying the next source...");
            }
        }

        return new CatalogRefreshResult
        {
            Status = CatalogRefreshStatus.Failed,
            Catalog = previous,
            Message = previous is null
                ? $"Could not load the version list. {string.Join(" | ", errors)}"
                : $"Could not refresh the version list; showing the cached copy. {string.Join(" | ", errors)}",
        };
    }

    private static HashSet<string> FindNewIds(VersionCatalog? previous, IReadOnlyList<GodotVersion> current)
    {
        if (previous is null || previous.Versions.Count == 0)
        {
            return [];
        }

        var known = previous.Versions.Select(v => v.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in current)
        {
            if (!known.Contains(version.Id))
            {
                result.Add(version.Id);
            }
        }

        return result;
    }
}
