using System.Diagnostics;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Launch;

/// <summary>How an editor process should be started.</summary>
public sealed record LaunchOptions
{
    /// <summary>Additional arguments passed to the editor.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Prefer the console executable on Windows to see log output.</summary>
    public bool UseConsole { get; init; }

    /// <summary>Working directory; defaults to the installation directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Project directory to open in the editor instead of the project manager.</summary>
    public string? ProjectPath { get; init; }
}

/// <summary>Details of a started editor process.</summary>
public sealed record LaunchResult
{
    /// <summary>True when the process was created.</summary>
    public required bool Started { get; init; }

    /// <summary>Process id of the started editor (or its launcher wrapper).</summary>
    public int ProcessId { get; init; }

    /// <summary>Executable that was started.</summary>
    public required string Executable { get; init; }

    /// <summary>Concatenated command line, used for logging.</summary>
    public string CommandLine { get; init; } = string.Empty;
}

/// <summary>Thrown when the editor process cannot be started.</summary>
public sealed class LaunchException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public LaunchException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Starts an installed Godot editor as a detached child process. The launcher
/// only checks that the executable exists and then hands off; it never waits for
/// the editor to exit.
/// </summary>
public sealed class EditorLauncher
{
    /// <summary>
    /// Launches the editor described by <paramref name="installation"/>.
    /// </summary>
    /// <exception cref="LaunchException">The executable is missing or cannot be started.</exception>
    public LaunchResult Launch(InstalledVersion installation, LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(installation);
        options ??= new LaunchOptions();

        var executable = installation.ExecutablePath;
        if (!File.Exists(executable))
        {
            throw new LaunchException(
                $"The editor executable was not found at '{executable}'. Reinstall {installation.DisplayName} or repair the installation.");
        }

        var workingDirectory = options.WorkingDirectory ?? installation.InstallPath;

        if (options.UseConsole && OperatingSystem.IsWindows())
        {
            var console = FindConsoleExecutable(executable);
            if (console is not null)
            {
                executable = console;
            }
        }

        var arguments = BuildArguments(installation.LaunchArguments, options);

        try
        {
            var startInfo = CreateStartInfo(executable, workingDirectory, arguments, options.UseConsole);
            var process = Process.Start(startInfo)
                ?? throw new LaunchException($"Starting '{executable}' did not return a process.");

            var processId = process.Id;
            process.Dispose();

            return new LaunchResult
            {
                Started = true,
                ProcessId = processId,
                Executable = executable,
                CommandLine = FormatCommandLine(executable, arguments),
            };
        }
        catch (Exception ex) when (ex is not LaunchException)
        {
            throw new LaunchException($"The editor could not be started: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Starts the editor with a project directory so it opens that project directly.
    /// </summary>
    public LaunchResult LaunchProject(InstalledVersion installation, string projectPath, LaunchOptions? options = null) =>
        Launch(installation, (options ?? new LaunchOptions()) with { ProjectPath = projectPath });

    /// <summary>Builds the editor argument list for the installation and options.</summary>
    public static IReadOnlyList<string> BuildArguments(string? installationArguments, LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>(options.Arguments);
        if (!string.IsNullOrWhiteSpace(installationArguments))
        {
            arguments.InsertRange(0, SplitArguments(installationArguments));
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            arguments.InsertRange(0, ["--path", options.ProjectPath, "--editor"]);
        }

        return arguments;
    }

    /// <summary>
    /// Builds the start information for the platform. On Unix the editor is
    /// wrapped in <c>setsid</c> when available so it detaches from the launcher's
    /// terminal session.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        bool useConsole)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = !useConsole,
        };

        if (!OperatingSystem.IsWindows() && File.Exists("/usr/bin/setsid"))
        {
            startInfo.FileName = "/usr/bin/setsid";
            startInfo.ArgumentList.Add(executable);
        }
        else
        {
            startInfo.FileName = executable;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Locates the <c>_console.exe</c> sibling of a Windows editor build.</summary>
    public static string? FindConsoleExecutable(string executable)
    {
        if (!OperatingSystem.IsWindows() || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(executable);
        var name = Path.GetFileNameWithoutExtension(executable);
        if (directory is null || name.EndsWith("_console", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var candidate = Path.Combine(directory, $"{name}_console.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string FormatCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var quoted = arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
        return arguments.Count == 0 ? executable : $"{executable} {string.Join(' ', quoted)}";
    }

    /// <summary>
    /// Splits a user supplied argument string, respecting double quotes. This is
    /// deliberately simple: presets are short flag lists, not shell scripts.
    /// </summary>
    public static IReadOnlyList<string> SplitArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
