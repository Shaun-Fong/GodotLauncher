using Avalonia.Headless.XUnit;
using GodotLauncher.App.ViewModels;

namespace GodotLauncher.App.Tests;

/// <summary>
/// The update status is stored as a status kind and rendered on demand, so switching the
/// language must also re-translate messages that carry no version number.
/// </summary>
public sealed class UpdateStatusTests
{
    [AvaloniaFact]
    public void UpToDateStatus_FollowsTheLanguageSwitch()
    {
        using var environment = new TestEnvironment("en");
        var updates = new UpdateViewModel(environment.Services);

        updates.ApplyStatus(UpdateStatusKind.UpToDate);
        Assert.Equal("You are using the latest version.", updates.StatusText);

        environment.Services.UpdateSettings(settings => settings with { Language = "zh-CN" }, save: false);
        Assert.Equal("当前已是最新版本。", updates.StatusText);

        environment.Services.UpdateSettings(settings => settings with { Language = "en" }, save: false);
        Assert.Equal("You are using the latest version.", updates.StatusText);
    }

    [AvaloniaFact]
    public void RateLimitStatus_FollowsTheLanguageSwitch()
    {
        using var environment = new TestEnvironment("zh-CN");
        var updates = new UpdateViewModel(environment.Services);

        updates.ApplyStatus(UpdateStatusKind.RateLimited);
        Assert.Equal("GitHub 暂时限制了更新检查，请稍后再试。", updates.StatusText);

        environment.Services.UpdateSettings(settings => settings with { Language = "en" }, save: false);
        Assert.Equal("GitHub is limiting update checks right now. Try again later.", updates.StatusText);
    }

    [AvaloniaFact]
    public void AvailableStatus_RebuildsWithTheVersionTag()
    {
        using var environment = new TestEnvironment("en");
        var updates = new UpdateViewModel(environment.Services);

        updates.ApplyStatus(UpdateStatusKind.Available, "v9.9.9");
        Assert.Equal("Version v9.9.9 is available.", updates.StatusText);

        environment.Services.UpdateSettings(settings => settings with { Language = "zh-CN" }, save: false);
        Assert.Equal("发现新版本 v9.9.9。", updates.StatusText);
    }

    [AvaloniaFact]
    public void ActionText_UsesTheLocalizedVerb()
    {
        using var environment = new TestEnvironment("en");
        var updates = new UpdateViewModel(environment.Services);
        updates.HasUpdate = true;

        Assert.Equal("Download update", updates.ActionText);

        environment.Services.UpdateSettings(settings => settings with { Language = "zh-CN" }, save: false);
        Assert.Equal("下载更新", updates.ActionText);
    }
}
