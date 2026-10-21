using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Localization;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Models;

namespace GodotLauncher.App.Downloads;

/// <summary>Lifecycle of a single queued download.</summary>
public enum DownloadState
{
    Queued,
    Resolving,
    Downloading,
    Extracting,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Observable download entry shown in the downloads panel.</summary>
public sealed partial class DownloadItem : ObservableObject
{
    private readonly CancellationTokenSource _cts = new();
    private DownloadState _state;
    private double _progress;
    private string _statusText = string.Empty;
    private string _detailText = string.Empty;
    private string? _errorMessage;
    private string? _destinationPath;
    private Strings? _strings;

    public DownloadItem(GodotVersion version, GodotEdition edition, CpuArchitecture architecture)
    {
        Version = version;
        Edition = edition;
        Architecture = architecture;
    }

    /// <summary>Queue that owns this entry; assigned when it is enqueued.</summary>
    internal DownloadManager? Manager { get; set; }

    [RelayCommand]
    private void CancelDownload() => Manager?.Cancel(this);

    [RelayCommand]
    private void RetryDownload() => Manager?.Retry(this);

    [RelayCommand]
    private void RemoveDownload() => Manager?.Remove(this);

    public GodotVersion Version { get; }

    public GodotEdition Edition { get; }

    public CpuArchitecture Architecture { get; }

    public string VersionId => Version.Id;

    public string DisplayName => Version.DisplayName;

    public string MetaText => $"{Edition.ToDisplayName()} · {Architecture.ToDisplayName()}";

    /// <summary>Unique key used to avoid duplicate queue entries.</summary>
    public string Key => $"{Version.Id}|{Edition}|{Architecture}";

    /// <summary>Localized cancel label for the queue entry.</summary>
    public string CancelText => _strings?.Downloads.Cancel ?? "Cancel";

    /// <summary>Localized retry label for the queue entry.</summary>
    public string RetryText => _strings?.Downloads.Retry ?? "Retry";

    /// <summary>Localized remove label for the queue entry.</summary>
    public string RemoveText => _strings?.Common.Remove ?? "Remove";

    /// <summary>Stores the current strings so button labels follow language changes.</summary>
    internal void ApplyStrings(Strings strings)
    {
        _strings = strings;
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(RetryText));
        OnPropertyChanged(nameof(RemoveText));
    }

    public DownloadState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanRetry));
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    /// <summary>Completion ratio between 0 and 1.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>Localized state label, e.g. "Downloading".</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Localized secondary line, e.g. size, speed and remaining time.</summary>
    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? DestinationPath
    {
        get => _destinationPath;
        internal set => SetProperty(ref _destinationPath, value);
    }

    public bool IsActive => State is DownloadState.Queued or DownloadState.Resolving
        or DownloadState.Downloading or DownloadState.Extracting;

    public bool IsFinished => State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled;

    public bool CanCancel => IsActive;

    public bool CanRetry => State is DownloadState.Failed or DownloadState.Cancelled;

    public bool CanInstall => State == DownloadState.Completed;

    internal CancellationToken Token => _cts.Token;

    internal void Reset()
    {
        State = DownloadState.Queued;
        Progress = 0;
        ErrorMessage = null;
    }

    internal void MarkQueued(Strings strings) => Apply(DownloadState.Queued, strings.Downloads.Queued, string.Empty, strings);

    internal void SetState(DownloadState state, string statusText)
    {
        State = state;
        StatusText = statusText;
        if (state is not (DownloadState.Failed or DownloadState.Cancelled))
        {
            ErrorMessage = null;
        }
    }

    internal void ReportProgress(DownloadProgress progress, Strings strings)
    {
        Progress = progress.Fraction ?? 0;
        var total = progress.TotalBytes is > 0
            ? DownloadProgress.FormatBytes(progress.TotalBytes.Value)
            : "?";
        var received = DownloadProgress.FormatBytes(progress.BytesReceived);

        if (progress.BytesPerSecond > 0)
        {
            var remaining = progress.EstimatedRemaining is { } eta
                ? $" · {FormatRemaining(eta)}"
                : string.Empty;
            DetailText = $"{received} / {total} · {progress.SpeedText}{remaining}";
        }
        else
        {
            DetailText = $"{received} / {total}";
        }

        StatusText = strings.Downloads.Downloading;
    }

    internal void Fail(string message, Strings strings)
    {
        ErrorMessage = message;
        State = DownloadState.Failed;
        StatusText = strings.Downloads.Failed;
        DetailText = string.Empty;
    }

    internal void Cancel(Strings strings)
    {
        State = DownloadState.Cancelled;
        StatusText = strings.Downloads.Cancelled;
        DetailText = string.Empty;
    }

    internal void Complete(Strings strings)
    {
        State = DownloadState.Completed;
        Progress = 1;
        StatusText = strings.Downloads.Completed;
        DetailText = strings.Version.Installed;
        ErrorMessage = null;
    }

    private void Apply(DownloadState state, string status, string detail, Strings strings)
    {
        State = state;
        StatusText = status;
        DetailText = detail;
        if (state is not (DownloadState.Failed or DownloadState.Cancelled))
        {
            ErrorMessage = null;
        }

        ApplyStrings(strings);
    }

    private void CancelInternal()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished.
        }
    }

    internal void CancelToken() => CancelInternal();

    /// <summary>Formats a remaining time as <c>mm:ss</c> or <c>h:mm:ss</c>.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
