using System.Text.Json;
using System.Text.Json.Serialization;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.Core.Install;

/// <summary>
/// Loads and saves <c>installed.json</c>. The file is a versioned document so a
/// future launcher version can migrate it without guessing.
/// </summary>
public sealed class InstallRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    /// <summary>Creates the registry at the given file path.</summary>
    public InstallRegistry(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>Path of the registry file.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Loads every record. A corrupt file is renamed aside so the user can
    /// inspect it, and an empty registry is returned instead of failing startup.
    /// </summary>
    public async Task<IReadOnlyList<InstalledVersion>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var document = await JsonSerializer
                .DeserializeAsync<RegistryDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document?.Installations is null)
            {
                return [];
            }

            return document.Installations
                .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Id))
                .ToList();
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>Writes the registry atomically.</summary>
    public async Task SaveAsync(IReadOnlyCollection<InstalledVersion> installations, CancellationToken cancellationToken = default)
    {
        var document = new RegistryDocument
        {
            SchemaVersion = 1,
            Installations = [.. installations],
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        await AtomicFile.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private void BackupCorruptFile()
    {
        try
        {
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(_filePath, backup, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RegistryDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public List<InstalledVersion> Installations { get; set; } = [];
    }
}
