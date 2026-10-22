using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Services;

/// <summary>
/// Replaces the running launcher with a downloaded release archive.
/// </summary>
/// <remarks>
/// A running executable cannot overwrite itself on Windows, so the launcher writes a small script
/// that waits for this process to exit, unpacks the archive, copies the new files over the install
/// directory and starts the launcher again. The script lives in a temporary folder and deletes
/// itself and the archive when it is done.
/// </remarks>
public static class UpdateInstaller
{
    /// <summary>
    /// Schedules the replacement of <paramref name="executablePath"/> with the files in
    /// <paramref name="archivePath"/> and restarts the launcher.
    /// </summary>
    /// <returns><c>true</c> when the update script was started; otherwise <c>false</c>.</returns>
    public static bool TryApply(string archivePath, string executablePath, IAppLog log, out string? error)
    {
        error = null;

        if (!File.Exists(archivePath))
        {
            error = "The downloaded update archive is missing.";
            return false;
        }

        var targetDirectory = Path.GetDirectoryName(executablePath);
        var executableName = Path.GetFileName(executablePath);
        if (string.IsNullOrEmpty(targetDirectory) || string.IsNullOrEmpty(executableName))
        {
            error = "The launcher executable path is not valid.";
            return false;
        }

        if (!CanWriteTo(targetDirectory))
        {
            error = "The install folder is not writable. Replace the files manually.";
            return false;
        }

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "GodotLauncherUpdate-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            var processId = Environment.ProcessId;
            var script = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? WriteWindowsScript(stagingDirectory, archivePath, targetDirectory, executableName, processId)
                : WriteUnixScript(stagingDirectory, archivePath, targetDirectory, executableName, processId);

            log.Info($"Update script written to {script}.");

            ProcessStartInfo startInfo;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo = new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath(),
                };
            }
            else
            {
                startInfo = new ProcessStartInfo("/bin/sh", $"\"{script}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath(),
                };
            }

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
            log.Error("Could not start the update script.", exception);
            return false;
        }
    }

    /// <summary>
    /// Deletes temporary update folders left behind by earlier updates. The running update script keeps
    /// its own folder in use, so only a later process can remove it.
    /// </summary>
    public static int CleanupStaleDirectories(TimeSpan minimumAge)
    {
        var removed = 0;
        var cutoff = DateTime.UtcNow - minimumAge;
        foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), "GodotLauncherUpdate-*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) > cutoff)
                {
                    continue;
                }

                Directory.Delete(directory, true);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Another update may still be running from this folder.
            }
        }

        return removed;
    }

    private static string WriteWindowsScript(
        string directory,
        string archivePath,
        string targetDirectory,
        string executableName,
        int processId)
    {
        var payload = Path.Combine(directory, "payload");
        var script = Path.Combine(directory, "apply.cmd");
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("setlocal");
        builder.AppendLine("set \"FOLDER=%~dp0\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"set \"STAGING={payload}\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"set \"TARGET={targetDirectory}\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"set \"ARCHIVE={archivePath}\"");
        builder.AppendLine("mkdir \"%STAGING%\" 2>nul");
        builder.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '%ARCHIVE%' -DestinationPath '%STAGING%' -Force\"");
        builder.AppendLine(":wait");
        builder.AppendLine(CultureInfo.InvariantCulture, $"tasklist /FI \"PID eq {processId}\" 2>nul | find \"{processId}\" >nul");
        builder.AppendLine("if not errorlevel 1 (");
        builder.AppendLine("  timeout /t 1 /nobreak >nul");
        builder.AppendLine("  goto wait");
        builder.AppendLine(")");
        builder.AppendLine("robocopy \"%STAGING%\" \"%TARGET%\" /E /IS /IT /NFL /NDL /NJH /NJS >nul");
        builder.AppendLine(CultureInfo.InvariantCulture, $"start \"\" \"%TARGET%\\{executableName}\"");
        builder.AppendLine("rmdir /s /q \"%STAGING%\"");
        builder.AppendLine("del \"%ARCHIVE%\"");
        // The script cannot delete the folder it runs from. Spawn a short-lived helper that waits for
        // this script to exit, then removes the whole folder (including the script itself).
        builder.AppendLine("start \"\" /min cmd /c \"timeout /t 2 /nobreak >nul & rmdir /s /q \"\"%~dp0.\"\"\"");
        builder.AppendLine("exit /b 0");
        File.WriteAllText(script, builder.ToString(), new UTF8Encoding(false));
        return script;
    }

    private static string WriteUnixScript(
        string directory,
        string archivePath,
        string targetDirectory,
        string executableName,
        int processId)
    {
        var payload = Path.Combine(directory, "payload");
        var script = Path.Combine(directory, "apply.sh");
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/sh");
        builder.AppendLine("FOLDER=$(dirname \"$0\")");
        builder.AppendLine(CultureInfo.InvariantCulture, $"STAGING='{payload}'");
        builder.AppendLine("mkdir -p \"$STAGING\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"tar -xf '{archivePath}' -C \"$STAGING\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"while kill -0 {processId} 2>/dev/null; do sleep 1; done");
        builder.AppendLine(CultureInfo.InvariantCulture, $"cp -R \"$STAGING\"/. '{targetDirectory}'/");
        builder.AppendLine(CultureInfo.InvariantCulture, $"chmod +x '{Path.Combine(targetDirectory, executableName)}'");
        builder.AppendLine(CultureInfo.InvariantCulture, $"'{Path.Combine(targetDirectory, executableName)}' >/dev/null 2>&1 &");
        builder.AppendLine("rm -rf \"$STAGING\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"rm -f '{archivePath}'");
        builder.AppendLine("rm -f \"$0\"");
        builder.AppendLine("cd /");
        builder.AppendLine("rmdir \"$FOLDER\" 2>/dev/null");
        File.WriteAllText(script, builder.ToString(), new UTF8Encoding(false));
        return script;
    }

    private static bool CanWriteTo(string directory)
    {
        var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
