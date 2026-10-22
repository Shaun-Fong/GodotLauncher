using System.Globalization;
using System.Net;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;
using GodotLauncher.Core.Storage;
using GodotLauncher.Core.Updates;

namespace GodotLauncher.App.ViewModels;

/// <summary>
/// Launcher self-update state, shared by the sidebar badge and the settings page so both
/// controls always show the same progress, speed and cancel behaviour.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly AppServices _services;

    private string? _archivePath;
    private string? _versionTag;
    private CancellationTokenSource? _downloadCts;

    public UpdateViewModel(AppServices services)
    {
        _services = services;
        _services.PropertyChanged += OnServicePropertyChanged;
        _services.LanguageChanged += (_, _) => RefreshStrings();
    }

    /// <summary>Current release offered by the checker, if any.</summary>
    [ObservableProperty]
    private bool _hasUpdate;

    /// <summary>True while a manual or background check is running.</summary>
    [ObservableProperty]
    private bool _isChecking;

    /// <summary>True while the archive is downloading.</summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>True once an archive waits to be applied.</summary>
    [ObservableProperty]
    private bool _isReady;

    /// <summary>Download progress from 0 to 1, painted as the button fill.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Button label: the action name, the live speed, or the install hint.</summary>
    [ObservableProperty]
    private string _actionText = string.Empty;

    /// <summary>Sentence shown next to the manual check button.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Technical detail for the last failure, shown as a tooltip.</summary>
    [ObservableProperty]
    private string _detailText = string.Empty;

    /// <summary>True when the update control should be offered.</summary>
    public bool CanAct => HasUpdate || IsDownloading || IsReady;

    partial void OnHasUpdateChanged(bool value) => RefreshState();

    partial void OnIsDownloadingChanged(bool value) => RefreshState();

    partial void OnIsReadyChanged(bool value) => RefreshState();

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanAct));
        if (!IsDownloading)
        {
            ActionText = IsReady
                ? _services.Loc.Settings.InstallUpdate
                : HasUpdate
                    ? _services.Loc.Settings.DownloadUpdate
                    : string.Empty;
        }
    }

    private void RefreshStrings()
    {
        if (!IsDownloading && !IsChecking)
        {
            if (IsReady && !string.IsNullOrEmpty(_versionTag))
            {
                StatusText = string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateReady, _versionTag);
            }
            else if (HasUpdate && _services.AvailableUpdate is { } release)
            {
                StatusText = string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateAvailable, release.Tag);
            }
        }

        RefreshState();
    }

    private void OnServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppServices.AvailableUpdate))
        {
            return;
        }

        // A ready archive or a running download owns the button; a fresh check result
        // must not replace it with a download action.
        if (IsReady || IsDownloading || IsChecking)
        {
            return;
        }

        var release = _services.AvailableUpdate;
        HasUpdate = release is not null;
        StatusText = release is null
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateAvailable, release.Tag);
    }

    /// <summary>Checks GitHub for a newer release and reports the result in <see cref="StatusText"/>.</summary>
    public async Task CheckAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        StatusText = _services.Loc.Settings.CheckingUpdates;
        DetailText = string.Empty;
        try
        {
            var release = await _services.CheckForUpdatesAsync();
            HasUpdate = release is not null;
            StatusText = release is null
                ? _services.Loc.Settings.UpToDate
                : string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateAvailable, release.Tag);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            _services.Log.Warn($"GitHub rate limit reached while checking for launcher updates: {exception.Message}");
            HasUpdate = false;
            StatusText = _services.Loc.Settings.UpdateRateLimited;
        }
        catch (Exception exception)
        {
            _services.Log.Warn($"Could not check for launcher updates: {exception.Message}");
            HasUpdate = false;
            StatusText = DescribeFailure(exception);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Single update control: downloads the archive with live speed on its face,
    /// cancels on a second click and installs as soon as the archive is ready.
    /// Concurrent executions are allowed so the second click reaches the cancel branch.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ApplyAsync()
    {
        if (IsDownloading)
        {
            _downloadCts?.Cancel();
            return;
        }

        // An archive that could not be applied (a read-only install folder, for
        // example) retries the install instead of downloading again.
        if (IsReady && !string.IsNullOrEmpty(_archivePath))
        {
            Install();
            return;
        }

        var release = _services.AvailableUpdate;
        var asset = release is null ? null : PickAsset(release);
        if (asset is null)
        {
            return;
        }

        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;
        IsDownloading = true;
        Progress = 0;
        ActionText = _services.Loc.Settings.UpdatePreparing;
        StatusText = string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateAvailable, release!.Tag);

        var progress = new Progress<DownloadProgress>(report =>
        {
            Progress = report.Fraction ?? 0;
            ActionText = string.IsNullOrWhiteSpace(report.SpeedText)
                ? _services.Loc.Settings.UpdatePreparing
                : report.SpeedText;
        });

        try
        {
            var path = await _services.DownloadUpdateAsync(asset, progress, token);
            _archivePath = path;
            _versionTag = release.Tag;
            Progress = 1;
            ActionText = _services.Loc.Settings.UpdateInstalling;
            StatusText = string.Format(CultureInfo.CurrentCulture, _services.Loc.Settings.UpdateReady, release.Tag);
            IsDownloading = false;
            IsReady = true;
            Install();

            // Reaching this point means the restart script could not be started.
            ActionText = _services.Loc.Settings.InstallUpdate;
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            Progress = 0;
            StatusText = _services.Loc.Settings.UpdateCancelled;
            _services.Log.Info("Launcher update download cancelled.");
        }
        catch (Exception exception)
        {
            IsDownloading = false;
            Progress = 0;
            _services.Log.Error("Could not download the launcher update.", exception);
            StatusText = DescribeFailure(exception);
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
        }
    }

    private void Install()
    {
        if (string.IsNullOrEmpty(_archivePath))
        {
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            StatusText = _services.Loc.Settings.UpdateInstallFailed;
            return;
        }

        if (!UpdateInstaller.TryApply(_archivePath, executable, _services.Log, out var error))
        {
            _services.Log.Warn($"Could not start the update script: {error}");
            StatusText = _services.Loc.Settings.UpdateInstallFailed;
            return;
        }

        _services.Log.Info($"Restarting to install launcher update {_versionTag ?? "(unknown)"}.");
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private string DescribeFailure(Exception exception)
    {
        var detail = exception.Message;
        if (exception.InnerException is { } inner
            && !detail.Contains(inner.Message, StringComparison.Ordinal))
        {
            detail = $"{detail} {inner.Message}";
        }

        detail = detail.Trim();
        if (detail.Length > 200)
        {
            detail = detail[..200] + "...";
        }

        // The operating system localizes exception text with its own UI language, which does not
        // always match the launcher language. Keep the raw detail out of the sentence and offer it
        // as a tooltip instead, so the visible status stays in one language.
        DetailText = detail;
        return _services.Loc.Settings.UpdateCheckFailed;
    }

    private static LauncherReleaseAsset? PickAsset(LauncherRelease release)
    {
        var platform = HostPlatformExtensions.Current;
        var architecture = HostArchitecture.Current;
        string[] candidates = platform switch
        {
            HostPlatform.Windows => architecture == CpuArchitecture.Arm64
                ? ["win-arm64", "win-x64", "win"]
                : ["win-x64", "win"],
            HostPlatform.MacOS => architecture == CpuArchitecture.Arm64
                ? ["osx-arm64", "osx-x64", "osx"]
                : ["osx-x64", "osx", "osx-arm64"],
            _ => architecture == CpuArchitecture.Arm64
                ? ["linux-arm64", "linux-x64", "linux"]
                : ["linux-x64", "linux"],
        };

        foreach (var token in candidates)
        {
            var match = release.Assets.FirstOrDefault(asset =>
                Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(asset.Name))
                    .EndsWith(token, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
