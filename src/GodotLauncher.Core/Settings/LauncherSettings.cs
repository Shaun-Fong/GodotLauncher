namespace GodotLauncher.Core.Settings;

/// <summary>Application color theme.</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>What the main window does when the user closes it.</summary>
public enum CloseBehavior
{
    /// <summary>Hide the window and keep running in the tray.</summary>
    Tray,

    /// <summary>Exit the launcher.</summary>
    Quit,
}

/// <summary>What happens to the launcher after an editor is started.</summary>
public enum AfterLaunchBehavior
{
    Hide,
    Keep,
    Quit,
}

/// <summary>
/// User settings, persisted as TOML in <c>config.toml</c>. Properties keep the
/// values already validated by <see cref="Normalize"/>.
/// </summary>
public sealed record LauncherSettings
{
    /// <summary>UI language: <c>system</c>, <c>en</c> or <c>zh-CN</c>.</summary>
    public string Language { get; init; } = "system";

    /// <summary>Light, dark or follow the operating system.</summary>
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>Close-to-tray or quit.</summary>
    public CloseBehavior CloseBehavior { get; init; } = CloseBehavior.Tray;

    /// <summary>Behaviour after launching an editor.</summary>
    public AfterLaunchBehavior AfterLaunch { get; init; } = AfterLaunchBehavior.Hide;

    /// <summary>Start the Windows console build instead of the GUI build.</summary>
    public bool LaunchWithConsole { get; init; }

    /// <summary>Register the launcher in the user's start-up items.</summary>
    public bool Autostart { get; init; }

    /// <summary>Optional custom installation root; empty means the default.</summary>
    public string InstallRoot { get; init; } = string.Empty;

    /// <summary>Optional download mirror template with <c>{slug}</c> style placeholders.</summary>
    public string MirrorTemplate { get; init; } = string.Empty;

    /// <summary>Maximum number of simultaneous downloads (1-4).</summary>
    public int ConcurrentDownloads { get; init; } = 2;

    /// <summary>Background refresh interval in hours.</summary>
    public int RefreshIntervalHours { get; init; } = 6;

    /// <summary>Show versions older than the current and previous 4.x line.</summary>
    public bool ShowOlderVersions { get; init; }

    /// <summary>Notify about new stable releases.</summary>
    public bool NotifyStable { get; init; }

    /// <summary>Notify about new release candidates.</summary>
    public bool NotifyRc { get; init; }

    /// <summary>Notify about new beta releases.</summary>
    public bool NotifyBeta { get; init; }

    /// <summary>Notify about new development builds.</summary>
    public bool NotifyDev { get; init; }

    /// <summary>Default settings for a fresh installation.</summary>
    public static LauncherSettings Default { get; } = new();

    /// <summary>Clamps out-of-range values coming from a hand edited file.</summary>
    public LauncherSettings Normalize()
    {
        var language = Language?.Trim() ?? string.Empty;
        if (language.Length == 0)
        {
            language = "system";
        }

        return this with
        {
            Language = language,
            InstallRoot = InstallRoot?.Trim() ?? string.Empty,
            MirrorTemplate = MirrorTemplate?.Trim() ?? string.Empty,
            ConcurrentDownloads = Math.Clamp(ConcurrentDownloads, 1, 4),
            RefreshIntervalHours = Math.Clamp(RefreshIntervalHours, 1, 168),
        };
    }
}
