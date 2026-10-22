using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GodotLauncher.App.Downloads;
using GodotLauncher.App.Tests;
using GodotLauncher.Core.Models;
using Xunit;

namespace GodotLauncher.App.Tests;

/// <summary>Checks that the download queue honours the parallel download setting.</summary>
public class DownloadConcurrencyTests
{
    [AvaloniaFact]
    public async Task QueueKeepsAtMostTheConfiguredDownloadsRunning()
    {
        var environment = new TestEnvironment();
        using var _ = environment;
        var manager = environment.Services.Downloads;
        environment.Services.UpdateSettings(settings => settings with { ConcurrentDownloads = 2 }, save: false);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var peak = 0;
        manager.Runner = async (_, _) =>
        {
            var current = Interlocked.Increment(ref running);
            Interlocked.Exchange(ref peak, Math.Max(peak, current));
            await gate.Task;
            Interlocked.Decrement(ref running);
        };

        for (var index = 0; index < 4; index++)
        {
            manager.Enqueue(
                GodotVersion.Create($"4.{index}", "stable", new DateOnly(2026, 1, 1)),
                GodotEdition.Standard,
                CpuArchitecture.X64);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, manager.RunningCount);
        Assert.Equal(4, manager.ActiveCount);

        gate.SetResult();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (manager.Items.Any(item => item.State != DownloadState.Completed) && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.Equal(2, peak);
        Assert.All(manager.Items, item => Assert.Equal(DownloadState.Completed, item.State));
        Assert.Equal(4, manager.Items.Count);
    }

    [AvaloniaFact]
    public void ConcurrencySettingIsClampedToFour()
    {
        var environment = new TestEnvironment();
        using var _ = environment;

        environment.Services.UpdateSettings(settings => settings with { ConcurrentDownloads = 99 }, save: false);

        Assert.Equal(4, environment.Services.Settings.ConcurrentDownloads);
        Assert.Equal(4, environment.Services.Downloads.MaxConcurrency);
    }
}
