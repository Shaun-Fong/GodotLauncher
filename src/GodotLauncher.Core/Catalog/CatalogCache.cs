using System.Text.Json;
using System.Text.Json.Serialization;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Catalog;

/// <summary>
/// Persists the last known catalog so the launcher can render instantly on a
/// cold and offline start, before any network request completes.
/// </summary>
public sealed class CatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private readonly string _filePath;

    /// <summary>Creates the cache at the given file path.</summary>
    public CatalogCache(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>Path of the cache file.</summary>
    public string FilePath => _filePath;

    /// <summary>Loads the cached catalog, returning <c>null</c> when absent or unreadable.</summary>
    public async Task<VersionCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var catalog = await JsonSerializer.DeserializeAsync<VersionCatalog>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (catalog is null || catalog.Versions.Count == 0)
            {
                return null;
            }

            return catalog with { IsFromCache = true };
        }
        catch (JsonException)
        {
            // A corrupt cache is not fatal: the launcher refreshes in the background.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Writes the catalog atomically.</summary>
    public async Task SaveAsync(VersionCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var payload = catalog with { IsFromCache = false };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await Storage.AtomicFile.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }
}
