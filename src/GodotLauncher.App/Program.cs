using System.Diagnostics;
using Avalonia;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App;

/// <summary>
/// Process entry point. Environment specific work (paths, logging, single instance) happens
/// before Avalonia starts, so nothing is loaded twice and a second instance exits quickly.
/// </summary>
internal static class Program
{
    /// <summary>Measures cold start until the first frame is rendered.</summary>
    internal static readonly Stopwatch Clock = Stopwatch.StartNew();

    internal static AppPaths Paths { get; private set; } = null!;

    internal static FileLog Log { get; private set; } = null!;

    internal static SingleInstance? Instance { get; private set; }

    /// <summary>Started by the autostart entry; the window stays hidden until it is requested.</summary>
    internal static bool StartHidden { get; private set; }

    /// <summary>Writes the cold start measurement and exits, used by performance checks.</summary>
    internal static bool Benchmark { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        StartHidden = HasFlag(args, "--background");
        Benchmark = HasFlag(args, "--bench");

        Paths = AppPaths.CreateDefault();
        Paths.EnsureDirectories();
        Log = new FileLog(Paths.LogDirectory);

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Log.Write(LogLevel.Error, "Unhandled exception.", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Write(LogLevel.Warning, "Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        Log.Info($"Godot Launcher {Services.AppServices.AppVersion} starting on {Paths.DataDirectory}.");

        Instance = SingleInstance.TryAcquire(Paths, (message, error) => Log.Write(LogLevel.Warning, message, error));
        if (Instance is null)
        {
            Log.Info("Another instance is already running; asked it to show its window.");
            return 0;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write(LogLevel.Error, "The launcher terminated unexpectedly.", ex);
            return 1;
        }
        finally
        {
            Instance.Dispose();
            Log.Dispose();
        }
    }

    /// <summary>Avalonia configuration shared with the designer tooling.</summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            // DirectComposition presents frames without an extra copy and starts faster
            // than the default EGL path on Windows 10 and 11.
            builder = builder.With(new Avalonia.Win32PlatformOptions
            {
                CompositionMode =
                [
                    Avalonia.Win32CompositionMode.WinUIComposition,
                    Avalonia.Win32CompositionMode.RedirectionSurface
                ]
            });
        }

        return builder;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));
}
