using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Assets;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Install;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Downloads;

/// <summary>
/// Runs the download queue with a configurable concurrency limit: resolve the asset, download
/// with resume support, verify, install, register, then clean up the archive.
/// </summary>
public sealed class DownloadManager : ObservableObject
{
    private readonly AppServices _services;
    private readonly object _gate = new();
    private readonly Queue<DownloadItem> _queue = new();
    private int _running;
    private int _concurrency;

    public DownloadManager(AppServices services)
    {
        _services = services;
        _concurrency = Math.Clamp(services.Settings.ConcurrentDownloads, 1, 4);
        services.LanguageChanged += (_, _) =>
        {
            var strings = services.Loc;
            foreach (var item in Items)
            {
                item.ApplyStrings(strings);
            }
        };
    }

    /// <summary>Queue entries, newest first.</summary>
    public ObservableCollection<DownloadItem> Items { get; } = [];

    /// <summary>Raised on the UI thread after an installation completed.</summary>
    public event Action<DownloadItem>? Completed;

    /// <summary>Number of downloads that are queued or running.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _running + _queue.Count;
            }
        }
    }

    public bool HasActive => ActiveCount > 0;

    /// <summary>Adds a version to the queue unless it is already queued or running.</summary>
    public DownloadItem Enqueue(GodotVersion version, GodotEdition edition, CpuArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(version);

        var existing = Items.FirstOrDefault(item =>
            item.IsActive &&
            item.VersionId == version.Id &&
            item.Edition == edition &&
            item.Architecture == architecture);
        if (existing is not null)
        {
            return existing;
        }

        var item = new DownloadItem(version, edition, architecture) { Manager = this };
        item.MarkQueued(_services.Loc);
        Items.Insert(0, item);
        lock (_gate)
        {
            _queue.Enqueue(item);
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
        Pump();
        return item;
    }

    /// <summary>Cancels a queued or running download.</summary>
    public void Cancel(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var removedFromQueue = false;
        lock (_gate)
        {
            if (_queue.Contains(item))
            {
                var remaining = _queue.Where(entry => entry != item).ToArray();
                _queue.Clear();
                foreach (var entry in remaining)
                {
                    _queue.Enqueue(entry);
                }

                removedFromQueue = true;
            }
        }

        if (removedFromQueue)
        {
            item.Cancel(_services.Loc);
        }
        else
        {
            item.CancelToken();
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
    }

    /// <summary>Re-queues a failed or cancelled entry.</summary>
    public void Retry(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry)
        {
            return;
        }

        item.Reset();
        item.MarkQueued(_services.Loc);
        lock (_gate)
        {
            _queue.Enqueue(item);
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
        Pump();
    }

    /// <summary>Removes a finished entry from the list.</summary>
    public void Remove(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Items.Remove(item);
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
    }

    /// <summary>Removes all finished entries.</summary>
    public void ClearFinished()
    {
        foreach (var item in Items.Where(entry => entry.IsFinished).ToArray())
        {
            Items.Remove(item);
        }
    }

    /// <summary>Applies a new concurrency limit and starts pending work.</summary>
    public void UpdateConcurrency(int concurrency)
    {
        _concurrency = Math.Clamp(concurrency, 1, 4);
        Pump();
    }

    private void Pump()
    {
        List<DownloadItem> start;
        lock (_gate)
        {
            start = [];
            while (_running < _concurrency && _queue.Count > 0)
            {
                var next = _queue.Dequeue();
                if (next.State != DownloadState.Queued)
                {
                    continue;
                }

                _running++;
                start.Add(next);
            }
        }

        foreach (var item in start)
        {
            _ = RunAsync(item);
        }
    }

    private async Task RunAsync(DownloadItem item)
    {
        var strings = _services.Loc;
        try
        {
            Dispatch(() => item.SetState(DownloadState.Resolving, strings.Downloads.Preparing));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, item.Token);

            var resolved = await _services.Resolver.ResolveAsync(
                _services.Http,
                item.Version,
                item.Edition,
                item.Architecture,
                HostPlatformExtensions.Current,
                linked.Token).ConfigureAwait(false);

            var asset = resolved.Asset;
            var destination = Path.Combine(_services.Paths.DownloadDirectory, asset.FileName);
            Dispatch(() =>
            {
                item.DestinationPath = destination;
                item.SetState(DownloadState.Downloading, strings.Downloads.Downloading);
            });

            var progress = new Progress<DownloadProgress>(snapshot => Dispatch(() => item.ReportProgress(snapshot, strings)));
            var download = await _services.Downloader.DownloadAsync(
                new DownloadRequest
                {
                    Uri = resolved.FinalUri,
                    FallbackUri = asset.FallbackUri,
                    DestinationPath = destination,
                    ExpectedSize = asset.SizeInBytes,
                    ExpectedSha256 = asset.Sha256,
                    MaxAttempts = 3
                },
                progress,
                item.Token).ConfigureAwait(false);

            Dispatch(() => item.SetState(DownloadState.Extracting, strings.Version.Installing));

            var source = ClassifySource(download.UsedUri);
            var result = await _services.Installer.InstallAsync(
                item.Version,
                item.Edition,
                item.Architecture,
                download.FilePath,
                source,
                download.Sha256,
                item.Token).ConfigureAwait(false);

            await _services.Library.AddOrUpdateAsync(result.Installed, item.Token).ConfigureAwait(false);
            _services.Log.Info($"Installed {result.Installed.DisplayName} at {result.Installed.InstallPath}.");

            TryDelete(download.FilePath);
            Dispatch(() => item.Complete(strings));
            Completed?.Invoke(item);
        }
        catch (OperationCanceledException)
        {
            _services.Log.Info($"Download of {item.VersionId} was cancelled.");
            Dispatch(() => item.Cancel(strings));
        }
        catch (AssetResolutionException ex)
        {
            _services.Log.Error($"No download found for {item.VersionId}.", ex);
            Dispatch(() => item.Fail(ex.Message, strings));
        }
        catch (DownloadException ex)
        {
            _services.Log.Error($"Download failed for {item.VersionId}.", ex);
            Dispatch(() => item.Fail(ex.Message, strings));
        }
        catch (InstallException ex)
        {
            _services.Log.Error($"Installation failed for {item.VersionId}.", ex);
            Dispatch(() => item.Fail(ex.Message, strings));
        }
        catch (Exception ex)
        {
            _services.Log.Error($"Unexpected failure while installing {item.VersionId}.", ex);
            Dispatch(() => item.Fail(ex.Message, strings));
        }
        finally
        {
            lock (_gate)
            {
                _running--;
            }

            Dispatch(() =>
            {
                OnPropertyChanged(nameof(ActiveCount));
                OnPropertyChanged(nameof(HasActive));
            });
            Pump();
        }
    }

    private static InstallSource ClassifySource(Uri uri)
    {
        var host = uri.Host;
        if (host.Contains("github", StringComparison.OrdinalIgnoreCase))
        {
            return InstallSource.GitHub;
        }

        return host.Contains("godotengine.org", StringComparison.OrdinalIgnoreCase)
            ? InstallSource.Official
            : InstallSource.Mirror;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _services.Log.Warn($"Could not delete the archive '{path}': {ex.Message}");
        }
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
