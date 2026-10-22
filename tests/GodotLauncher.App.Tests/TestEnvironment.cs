using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.Tests;

/// <summary>Creates a throw-away data directory and an <see cref="AppServices"/> bound to it.</summary>
internal sealed class TestEnvironment : IDisposable
{
    public TestEnvironment(string language = "en")
    {
        Root = Path.Combine(Path.GetTempPath(), "GodotLauncherTests", Guid.NewGuid().ToString("N"));
        Paths = AppPaths.CreateFor(Root);
        Paths.EnsureDirectories();
        Log = new FileLog(Paths.LogDirectory);
        Services = new AppServices(Paths, Log);
        Services.UpdateSettings(settings => settings with { Language = language }, save: false);
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public FileLog Log { get; }

    public AppServices Services { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless for the assertion result.
        }
    }
}

/// <summary>Dialog host that records requests and answers without a window.</summary>
internal sealed class FakeDialogHost : IDialogHost
{
    public List<DialogViewModel> Requests { get; } = [];

    public Task<DialogResult> ShowDialogAsync(DialogViewModel dialog)
    {
        Requests.Add(dialog);
        return Task.FromResult(DialogResult.Cancel);
    }
}
