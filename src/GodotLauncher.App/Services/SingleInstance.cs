using System.Net;
using System.Runtime.Versioning;
using System.Net.Sockets;
using System.Text;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Services;

/// <summary>
/// Guarantees a single launcher instance. A second instance forwards an activation request to
/// the running one and exits. Windows uses named kernel objects; other platforms use a loopback
/// socket whose port is derived from the data directory.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\GodotLauncher.SingleInstance";
    private const string EventName = @"Local\GodotLauncher.ShowWindow";
    private const int BasePort = 24000;
    private const int PortRange = 16000;

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly Thread? _watcher;
    private readonly TcpListener? _listener;
    private readonly CancellationTokenSource _stop = new();
    private volatile bool _disposed;

    private SingleInstance(Mutex? mutex, EventWaitHandle? showEvent, Thread? watcher, TcpListener? listener)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _watcher = watcher;
        _listener = listener;
    }

    /// <summary>Raised when another instance asked this one to show its window.</summary>
    public event EventHandler? ShowRequested;

    /// <summary>
    /// Creates the primary instance, or returns null when another instance is already running.
    /// <paramref name="activateExisting"/> controls whether a running instance is asked to show.
    /// </summary>
    public static SingleInstance? TryAcquire(AppPaths paths, Action<string, Exception?>? reportError = null, bool activateExisting = true)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (OperatingSystem.IsWindows())
        {
            return TryAcquireWindows(reportError, activateExisting);
        }

        return TryAcquireSocket(paths, reportError, activateExisting);
    }

    [SupportedOSPlatform("windows")]
    private static SingleInstance? TryAcquireWindows(Action<string, Exception?>? reportError, bool activateExisting)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                if (activateExisting)
                {
                    SignalWindowsInstance(reportError);
                }

                mutex.Dispose();
                return null;
            }
        }
        catch (Exception ex)
        {
            reportError?.Invoke("Could not create the single instance mutex; continuing without it.", ex);
            return new SingleInstance(null, null, null, null);
        }

        EventWaitHandle showEvent;
        try
        {
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        }
        catch (Exception ex)
        {
            reportError?.Invoke("Could not create the activation event; continuing without it.", ex);
            return new SingleInstance(mutex, null, null, null);
        }

        var instance = new SingleInstance(mutex, showEvent, null, null);
        var watcher = new Thread(() => instance.WatchWindowsEvent(showEvent))
        {
            IsBackground = true,
            Name = "GodotLauncher single instance"
        };
        watcher.Start();
        return instance;
    }

    [SupportedOSPlatform("windows")]
    private static void SignalWindowsInstance(Action<string, Exception?>? reportError)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(EventName);
                handle.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                reportError?.Invoke("Could not notify the running launcher instance.", ex);
                return;
            }
        }
    }

    private static SingleInstance? TryAcquireSocket(AppPaths paths, Action<string, Exception?>? reportError, bool activateExisting)
    {
        var port = ResolvePort(paths.DataDirectory);
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            listener.Dispose();
            if (activateExisting)
            {
                SignalSocketInstance(port, reportError);
            }

            return null;
        }

        var instance = new SingleInstance(null, null, null, listener);
        var watcher = new Thread(() => instance.WatchSocket(listener, port))
        {
            IsBackground = true,
            Name = "GodotLauncher single instance"
        };
        watcher.Start();
        return instance;
    }

    private static void SignalSocketInstance(int port, Action<string, Exception?>? reportError)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            stream.Write(Encoding.UTF8.GetBytes("show"));
        }
        catch (Exception ex)
        {
            // The other instance may be shutting down; exiting silently is fine.
            reportError?.Invoke("Could not notify the running launcher instance.", ex);
        }
    }

    private static int ResolvePort(string dataDirectory)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in dataDirectory.ToUpperInvariant())
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return BasePort + (int)(hash % PortRange);
        }
    }

    [SupportedOSPlatform("windows")]
    private void WatchWindowsEvent(EventWaitHandle showEvent)
    {
        while (!_disposed)
        {
            try
            {
                if (showEvent.WaitOne(TimeSpan.FromMilliseconds(250)))
                {
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void WatchSocket(TcpListener listener, int port)
    {
        while (!_disposed)
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException) when (_disposed)
            {
                return;
            }
            catch (SocketException)
            {
                // A failed client does not stop the listener.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Ignore shutdown races.
        }

        _showEvent?.Dispose();
        _mutex?.Dispose();
        _stop.Dispose();
    }
}
