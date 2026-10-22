using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using GodotLauncher.App.Tests;
using GodotLauncher.App.Services;
using GodotLauncher.App.ViewModels;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Settings;
using Xunit;

namespace GodotLauncher.App.Tests;

/// <summary>Every settings switch has to reach <see cref="LauncherSettings"/> and its side effect.</summary>
public class FeatureToggleTests
{
    private static (TestEnvironment Environment, SettingsViewModel Model) Create()
    {
        var environment = new TestEnvironment();
        var updates = new UpdateViewModel(environment.Services);
        var model = new SettingsViewModel(environment.Services, updates);
        return (environment, model);
    }

    [AvaloniaFact]
    public void ThemeSwitchUpdatesTheApplicationVariant()
    {
        var (environment, model) = Create();
        using var _ = environment;

        model.SelectedTheme = model.Themes.Single(option => option.Value == ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, environment.Services.Settings.Theme);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

        model.SelectedTheme = model.Themes.Single(option => option.Value == ThemeMode.Light);
        Assert.Equal(ThemeMode.Light, environment.Services.Settings.Theme);
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void BehaviourSwitchesArePersisted()
    {
        var (environment, model) = Create();
        using var _ = environment;

        model.SelectedCloseBehavior = model.CloseBehaviors.Single(option => option.Value == CloseBehavior.Quit);
        model.SelectedAfterLaunch = model.AfterLaunchBehaviors.Single(option => option.Value == AfterLaunchBehavior.Quit);
        model.LaunchWithConsole = true;

        var settings = environment.Services.Settings;
        Assert.Equal(CloseBehavior.Quit, settings.CloseBehavior);
        Assert.Equal(AfterLaunchBehavior.Quit, settings.AfterLaunch);
        Assert.True(settings.LaunchWithConsole);
    }

    [AvaloniaFact]
    public void AutostartSwitchIsAppliedAndPersisted()
    {
        var (environment, model) = Create();
        using var _ = environment;

        var applied = new List<bool>();
        AutostartService.ApplyOverride = enabled => applied.Add(enabled);
        try
        {
            model.Autostart = true;
            model.Autostart = false;
        }
        finally
        {
            AutostartService.ApplyOverride = null;
        }

        Assert.Equal(new[] { true, false }, applied);
        Assert.False(environment.Services.Settings.Autostart);
    }

    [AvaloniaFact]
    public void NotificationChannelsArePersistedPerChannel()
    {
        var (environment, model) = Create();
        using var _ = environment;

        model.NotifyStable = true;
        model.NotifyRc = true;

        var settings = environment.Services.Settings;
        Assert.True(settings.NotifyStable);
        Assert.True(settings.NotifyRc);
        Assert.False(settings.NotifyBeta);
        Assert.False(settings.NotifyDev);
    }

    [AvaloniaFact]
    public void ParallelDownloadsReachTheQueue()
    {
        var (environment, model) = Create();
        using var _ = environment;

        model.SelectedParallelDownloads = model.ParallelDownloadOptions.Single(option => option.Value == 3);

        Assert.Equal(3, environment.Services.Settings.ConcurrentDownloads);
        Assert.Equal(3, environment.Services.Downloads.MaxConcurrency);
    }

    [AvaloniaFact]
    public void InstallRootSwitchIsAppliedToTheInstaller()
    {
        var (environment, model) = Create();
        using var _ = environment;
        var custom = Path.Combine(environment.Root, "custom-editors");

        model.InstallRoot = custom;
        model.SaveInstallRootCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(custom), environment.Services.Paths.InstallRoot);
        Assert.False(model.HasInstallRootError);
        Assert.StartsWith(
            Path.GetFullPath(custom),
            environment.Services.Installer.GetInstallDirectory("4.8-beta1", GodotEdition.Standard),
            StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void UnusableInstallRootKeepsThePreviousFolderAndShowsAnError()
    {
        var (environment, model) = Create();
        using var _ = environment;
        var blocked = Path.Combine(environment.Root, "blocked");
        File.WriteAllText(blocked, "not a folder");
        var before = environment.Services.Paths.InstallRoot;

        model.InstallRoot = blocked;
        model.SaveInstallRootCommand.Execute(null);

        Assert.True(model.HasInstallRootError);
        Assert.Equal(before, environment.Services.Paths.InstallRoot);
        Assert.Equal(string.Empty, environment.Services.Settings.InstallRoot);
        Assert.Equal(environment.Services.Settings.InstallRoot, model.InstallRoot);
    }

    [AvaloniaFact]
    public void ExploreSwitchesBetweenNewerAndAllVersions()
    {
        var environment = new TestEnvironment();
        using var _ = environment;
        var model = new ExploreViewModel(environment.Services);
        model.Activate();

        environment.Services.SetCatalog(new VersionCatalog
        {
            Versions =
            [
                GodotVersion.Create("4.8", "beta1", new DateOnly(2026, 10, 15)),
                GodotVersion.Create("3.6.3", "stable", new DateOnly(2026, 9, 1)),
                GodotVersion.Create("3.5", "beta1", new DateOnly(2026, 3, 1)),
                GodotVersion.Create("2.1", "stable", new DateOnly(2019, 1, 1)),
            ],
            FetchedAt = DateTimeOffset.Now,
            Source = "test",
        });

        Assert.Equal(2, model.Versions.Count);

        model.ShowOlderVersions = true;
        Assert.Equal(4, model.Versions.Count);

        model.ShowOlderVersions = false;
        Assert.Equal(2, model.Versions.Count);
    }
}
