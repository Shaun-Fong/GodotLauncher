using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.Core.Projects;

/// <summary>
/// Persists the project list as <c>projects.json</c>. Writes are atomic and a
/// corrupt file is backed up once instead of blocking startup.
/// </summary>
public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ProjectRecord> _projects = [];
    private bool _loaded;

    /// <summary>Creates a store backed by <paramref name="filePath"/>.</summary>
    public ProjectStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>Raised after the list changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Projects ordered by most recently opened first.</summary>
    public IReadOnlyList<ProjectRecord> Items => _projects;

    /// <summary>Number of tracked projects.</summary>
    public int Count => _projects.Count;

    /// <summary>Loads the file once.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                await using var stream = File.OpenRead(_filePath);
                var document = await JsonSerializer.DeserializeAsync<ProjectDocument>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                _projects.AddRange(Sanitize(document?.Projects));
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                BackupCorruptFile();
            }

            Sort();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Finds a project by directory path.</summary>
    public ProjectRecord? Find(string path)
    {
        var normalized = ProjectRecord.NormalizePath(path);
        return _projects.FirstOrDefault(
            project => string.Equals(project.Path, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or replaces a project and persists the list.</summary>
    public async Task<ProjectRecord> AddOrUpdateAsync(ProjectRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = record with { Path = ProjectRecord.NormalizePath(record.Path) };
            var index = _projects.FindIndex(
                project => string.Equals(project.Path, normalized.Path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                normalized = normalized with { AddedAt = _projects[index].AddedAt };
                _projects[index] = normalized;
            }
            else
            {
                _projects.Add(normalized);
            }

            Sort();
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes a project from the list; optionally deletes its folder.</summary>
    public async Task<bool> RemoveAsync(string path, bool deleteFromDisk, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = ProjectRecord.NormalizePath(path);
            var record = _projects.FirstOrDefault(
                project => string.Equals(project.Path, normalized, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                return false;
            }

            _projects.Remove(record);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            RaiseChanged();

            if (deleteFromDisk)
            {
                GodotLauncher.Core.Install.Installer.TryDeleteDirectory(record.Path);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stores the editor chosen for a project.</summary>
    public async Task SetEditorAsync(string path, string? editorId, CancellationToken cancellationToken = default)
    {
        var project = Find(path);
        if (project is null)
        {
            return;
        }

        await AddOrUpdateAsync(project with { EditorId = editorId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Refreshes the name and version recorded for a project.</summary>
    public async Task UpdateFromProjectAsync(GodotProjectFile project, CancellationToken cancellationToken = default)
    {
        var existing = Find(project.Directory);
        if (existing is null)
        {
            return;
        }

        await AddOrUpdateAsync(
            existing with { Name = project.DisplayName, VersionText = project.VersionText },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Records that a project was opened.</summary>
    public async Task TouchOpenedAsync(string path, DateTimeOffset when, CancellationToken cancellationToken = default)
    {
        var project = Find(path);
        if (project is null)
        {
            return;
        }

        await AddOrUpdateAsync(project with { LastOpened = when }, cancellationToken).ConfigureAwait(false);
    }

    private void Sort() =>
        _projects.Sort((left, right) =>
        {
            var byOpened = Nullable.Compare(right.LastOpened, left.LastOpened);
            return byOpened != 0 ? byOpened : string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        });

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var document = new ProjectDocument { SchemaVersion = 1, Projects = [.. _projects] };
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await AtomicFile.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static IEnumerable<ProjectRecord> Sanitize(IEnumerable<ProjectRecord>? projects)
    {
        if (projects is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.Path) || string.IsNullOrWhiteSpace(project.Name))
            {
                continue;
            }

            string path;
            try
            {
                path = ProjectRecord.NormalizePath(project.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (seen.Add(path))
            {
                yield return project with { Path = path };
            }
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(_filePath, backup, overwrite: true);
        }
        catch (IOException)
        {
            // Keeping the original file is better than losing the list.
        }
    }

    private sealed class ProjectDocument
    {
        public int SchemaVersion { get; set; } = 1;

        public List<ProjectRecord>? Projects { get; set; } = [];

        public override string ToString() => JsonSerializer.Serialize(this, JsonOptions);
    }

}
