using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GodotLauncher.App.ViewModels;
using GodotLauncher.Core.Settings;

namespace GodotLauncher.App.Tests;

/// <summary>Settings page behaviour around proxy modes and their address field.</summary>
public sealed class SettingsViewModelTests
{
    private static SettingsViewModel CreateSettings(TestEnvironment environment) =>
        new MainWindowViewModel(environment.Services).SettingsPage;

    [AvaloniaFact]
    public void ProxyAddress_IsHiddenUnlessTheModeIsCustom()
    {
        using var environment = new TestEnvironment("en");
        var settings = CreateSettings(environment);

        Assert.False(settings.IsCustomProxy);
        Assert.Equal(ProxyMode.System, environment.Services.Settings.Proxy);

        settings.SelectedProxy = settings.ProxyModes.Single(option => option.Value == ProxyMode.Custom);
        Assert.True(settings.IsCustomProxy);
        Assert.Equal(ProxyMode.Custom, environment.Services.Settings.Proxy);

        settings.SelectedProxy = settings.ProxyModes.Single(option => option.Value == ProxyMode.None);
        Assert.False(settings.IsCustomProxy);
        Assert.Equal(ProxyMode.None, environment.Services.Settings.Proxy);
    }

    [AvaloniaFact]
    public void SavingAnInvalidCustomProxy_KeepsThePreviousAddress()
    {
        using var environment = new TestEnvironment("en");
        var settings = CreateSettings(environment);
        settings.SelectedProxy = settings.ProxyModes.Single(option => option.Value == ProxyMode.Custom);
        settings.ProxyUrl = "http://127.0.0.1:7890";
        settings.SaveProxyUrlCommand.Execute(null);

        settings.ProxyUrl = "socks5://127.0.0.1:1080";
        settings.SaveProxyUrlCommand.Execute(null);

        Assert.True(settings.HasProxyError);
        Assert.Equal("http://127.0.0.1:7890", environment.Services.Settings.ProxyUrl);
    }

    [AvaloniaFact]
    public void SavingAValidCustomProxy_StoresTheTrimmedAddress()
    {
        using var environment = new TestEnvironment("en");
        var settings = CreateSettings(environment);
        settings.SelectedProxy = settings.ProxyModes.Single(option => option.Value == ProxyMode.Custom);
        settings.ProxyUrl = "  http://127.0.0.1:7890  ";
        settings.SaveProxyUrlCommand.Execute(null);

        Assert.False(settings.HasProxyError);
        Assert.Equal("http://127.0.0.1:7890", environment.Services.Settings.ProxyUrl);
    }

    [AvaloniaFact]
    public void DefaultPage_IsTheProjectsPage()
    {
        using var environment = new TestEnvironment("en");
        var shell = new MainWindowViewModel(environment.Services);

        Assert.Equal(ShellPage.Projects, shell.Page);
        Assert.True(shell.IsProjectsSelected);
        Assert.False(shell.IsExploreSelected);
    }

    [AvaloniaFact]
    public void LicenseText_FollowsTheLanguage()
    {
        using var environment = new TestEnvironment("zh-CN");
        var settings = CreateSettings(environment);
        Assert.StartsWith("\u4EE5 MIT", settings.LicenseText, StringComparison.Ordinal);

        environment.Services.UpdateSettings(current => current with { Language = "en" }, save: false);
        Assert.StartsWith("MIT licensed", settings.LicenseText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LanguageSwitch_KeepsTheProxySelectionAndRelabelsTheOptions()
    {
        using var environment = new TestEnvironment("en");
        var settings = CreateSettings(environment);
        settings.SelectedProxy = settings.ProxyModes.Single(option => option.Value == ProxyMode.Custom);
        Assert.Equal("Custom proxy", settings.SelectedProxy!.Label);

        environment.Services.UpdateSettings(current => current with { Language = "zh-CN" }, save: false);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(settings.SelectedProxy);
        Assert.Equal(ProxyMode.Custom, settings.SelectedProxy!.Value);
        Assert.Equal("\u81EA\u5B9A\u4E49\u4EE3\u7406", settings.SelectedProxy.Label);
    }
}
