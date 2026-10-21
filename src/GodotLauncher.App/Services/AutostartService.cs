using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace GodotLauncher.App.Services;

/// <summary>
/// Registers the launcher to start with the user session. Windows uses the per-user Run key,
/// Linux a desktop entry in <c>~/.config/autostart</c> and macOS a launch agent plist.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GodotLauncher";
    private const string LinuxFileName = "godot-launcher.desktop";
    private const string MacLabel = "org.godotlauncher.GodotLauncher";

    /// <summary>True when autostart can be configured on this platform.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>Creates or removes the autostart entry for the current executable.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Autostart is not supported on this platform.");
        }

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("The launcher executable path is unknown.");

        if (OperatingSystem.IsWindows())
        {
            SetWindows(enabled, executable);
        }
        else if (OperatingSystem.IsLinux())
        {
            SetLinux(enabled, executable);
        }
        else
        {
            SetMacOs(enabled, executable);
        }
    }

    /// <summary>Returns whether an autostart entry currently exists.</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }

            if (OperatingSystem.IsLinux())
            {
                return File.Exists(Path.Combine(LinuxAutostartDirectory, LinuxFileName));
            }

            return OperatingSystem.IsMacOS() && File.Exists(MacPlistPath);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindows(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Could not open the autostart registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static void SetLinux(bool enabled, string executable)
    {
        var path = Path.Combine(LinuxAutostartDirectory, LinuxFileName);
        if (!enabled)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(LinuxAutostartDirectory);
        var content = $"""
            [Desktop Entry]
            Type=Application
            Name=Godot Launcher
            Comment=Start Godot Launcher in the background
            Exec="{executable}" --background
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """;
        File.WriteAllText(path, content.ReplaceLineEndings("\n") + "\n");
    }

    private static void SetMacOs(bool enabled, string executable)
    {
        if (!enabled)
        {
            if (File.Exists(MacPlistPath))
            {
                File.Delete(MacPlistPath);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MacPlistPath)!);
        var content = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{MacLabel}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{executable}</string>
                    <string>--background</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
            </dict>
            </plist>
            """;
        File.WriteAllText(MacPlistPath, content);
    }

    private static string LinuxAutostartDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

    private static string MacPlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{MacLabel}.plist");
}
