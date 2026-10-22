using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Install;

/// <summary>
/// In-memory view of the installation registry. Every mutation is persisted
/// atomically and published through <see cref="Changed"/> so the UI can refresh
/// without polling.
/// </summary>
public sealed class InstallationLibrary
{
    private readonly InstallRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<InstalledVersion> _items = [];

    /// <summary>Creates the library over a registry file.</summary>
    public InstallationLibrary(InstallRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>Raised after every change, on a background thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Current installations, ordered as stored.</summary>
    public IReadOnlyList<InstalledVersion> Items => Volatile.Read(ref _items);

    /// <summary>Number of installations.</summary>
    public int Count => Items.Count;

    /// <summary>Loads records from disk.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _registry.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = loaded.ToList();
            var repaired = false;

            // Launchers before 0.4.1 installed the .NET edition under mono/; the
            // startup migration moves that folder to dotnet/, so records that still
            // point at the old path are rewritten here.
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (string.IsNullOrEmpty(item.InstallPath) || Directory.Exists(item.InstallPath))
                {
                    continue;
                }

                var candidate = RepairLegacyPath(item.InstallPath);
                if (candidate is null || !Directory.Exists(candidate))
                {
                    continue;
                }

                items[i] = item with { InstallPath = candidate };
                repaired = true;
            }

            // Older libraries could be saved without a default editor; promote
            // the most recent one so launching from the tray stays predictable.
            if (items.Count > 0 && !items.Any(i => i.IsDefault))
            {
                var best = items
                    .OrderByDescending(i => i.LastLaunched ?? DateTimeOffset.MinValue)
                    .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .First();
                items[items.IndexOf(best)] = best with { IsDefault = true };
            }

            _items = [.. items];
            if (repaired)
            {
                await _registry.SaveAsync(_items, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? RepairLegacyPath(string installPath)
    {
        var segments = installPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var index = Array.FindIndex(segments, segment =>
            string.Equals(segment, GodotEdition.Mono.ToLegacyFolderName(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        segments[index] = GodotEdition.Mono.ToFolderName();
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    /// <summary>Adds a record, replacing any record with the same id.</summary>
    public async Task AddOrUpdateAsync(InstalledVersion installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        await MutateAsync(list =>
        {
            var index = list.FindIndex(i => string.Equals(i.Id, installation.Id, StringComparison.OrdinalIgnoreCase));
            var record = installation;

            // The first editor that lands in the library becomes the default,
            // so the tray "launch default" action works out of the box.
            if (!record.IsDefault && !list.Any(i => i.IsDefault))
            {
                record = record with { IsDefault = true };
            }

            if (record.IsDefault)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (i != index && list[i].IsDefault)
                    {
                        list[i] = list[i] with { IsDefault = false };
                    }
                }
            }

            if (index >= 0)
            {
                list[index] = record;
            }
            else
            {
                list.Add(record);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a record and optionally deletes its files. Files are never
    /// deleted for locally imported editors, because the launcher does not own
    /// that directory.
    /// </summary>
    public async Task<bool> RemoveAsync(string id, bool deleteFiles, CancellationToken cancellationToken = default)
    {
        InstalledVersion? target = null;
        var filesDeleted = false;

        await MutateAsync(list =>
        {
            target = list.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return false;
            }

            list.Remove(target);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (target is null)
        {
            return false;
        }

        if (deleteFiles && target.Source != InstallSource.LocalImport)
        {
            Installer.TryDeleteDirectory(target.InstallPath);
            filesDeleted = !Directory.Exists(target.InstallPath);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return filesDeleted || target.Source == InstallSource.LocalImport;
    }

    /// <summary>Marks exactly one installation as the default.</summary>
    public Task SetDefaultAsync(string? id, CancellationToken cancellationToken = default) =>
        MutateAsync(list =>
        {
            for (var index = 0; index < list.Count; index++)
            {
                var isTarget = id is not null && string.Equals(list[index].Id, id, StringComparison.OrdinalIgnoreCase);
                list[index] = list[index] with { IsDefault = isTarget };
            }

            return true;
        }, cancellationToken);

    /// <summary>Pins or unpins an installation.</summary>
    public Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default) =>
        MutateAsync(list => Update(list, id, item => item with { Pinned = pinned }), cancellationToken);

    /// <summary>Sets or clears the display alias.</summary>
    public Task SetAliasAsync(string id, string? alias, CancellationToken cancellationToken = default) =>
        MutateAsync(list => Update(list, id, item => item with
        {
            Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim(),
        }), cancellationToken);

    /// <summary>Records that the editor was launched just now.</summary>
    public Task TouchLaunchedAsync(string id, DateTimeOffset when, CancellationToken cancellationToken = default) =>
        MutateAsync(list => Update(list, id, item => item with { LastLaunched = when }), cancellationToken);

    /// <summary>The default installation, or the most recently launched one.</summary>
    public InstalledVersion? FindDefault() =>
        Items.FirstOrDefault(i => i.IsDefault)
        ?? Items.OrderByDescending(i => i.LastLaunched ?? DateTimeOffset.MinValue).FirstOrDefault();

    /// <summary>Finds an installation by its version id.</summary>
    public InstalledVersion? FindByVersion(string versionId, GodotEdition edition) =>
        Items.FirstOrDefault(i =>
            string.Equals(i.VersionId, versionId, StringComparison.OrdinalIgnoreCase) && i.Edition == edition);

    /// <summary>
    /// Preview installations that can be removed by "clean up old previews":
    /// everything unpinned except the newest preview of each minor version.
    /// </summary>
    public IReadOnlyList<InstalledVersion> FindPreviewCleanupCandidates()
    {
        var previews = new List<(InstalledVersion Installation, GodotVersion Version)>();
        foreach (var installation in Items)
        {
            if (installation.Pinned || installation.Source == InstallSource.LocalImport)
            {
                continue;
            }

            var version = VersionIds.ToVersion(installation.VersionId);
            if (version is null || !version.IsPreview)
            {
                continue;
            }

            previews.Add((installation, version));
        }

        previews.Sort((a, b) => VersionComparer.Instance.Compare(a.Version, b.Version));

        var newestPerMinor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledVersion>();
        foreach (var (installation, version) in previews)
        {
            var minorKey = $"{version.Number.Major}.{version.Number.Minor}";
            if (!newestPerMinor.Add(minorKey))
            {
                result.Add(installation);
            }
        }

        return result;
    }

    private static bool Update(List<InstalledVersion> list, string id, Func<InstalledVersion, InstalledVersion> change)
    {
        var index = list.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        list[index] = change(list[index]);
        return true;
    }

    private async Task MutateAsync(Func<List<InstalledVersion>, bool> mutate, CancellationToken cancellationToken)
    {
        bool changed;
        List<InstalledVersion> snapshot;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var working = new List<InstalledVersion>(_items);
            changed = mutate(working);
            if (!changed)
            {
                return;
            }

            await _registry.SaveAsync(working, cancellationToken).ConfigureAwait(false);
            _items = working;
            snapshot = working;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
