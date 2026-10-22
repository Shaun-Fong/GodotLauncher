using Avalonia;
using Avalonia.Headless;
using GodotLauncher.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace GodotLauncher.App.Tests;

/// <summary>
/// Boots a headless Avalonia application so view models and services that touch the UI thread,
/// assets or the theme variant can be exercised on CI without a desktop.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
