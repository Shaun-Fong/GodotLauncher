using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GodotLauncher.App.ViewModels;
using GodotLauncher.Core.Install;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Projects;

namespace GodotLauncher.App.Tests;

/// <summary>Project import flow: parsing a folder, matching an editor and warning when it is missing.</summary>
public sealed class ProjectsViewModelTests
{
    private static string CreateProject(string name, VersionNumber version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "GodotLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ProjectCreator.Create(directory, name, version);
        return directory;
    }

    [AvaloniaFact]
    public async Task ImportingAProject_AddsItToTheList()
    {
        using var environment = new TestEnvironment("en");
        var dialogs = new FakeDialogHost();
        var projects = new ProjectsViewModel(environment.Services, dialogs);
        var directory = CreateProject("Import Probe", new VersionNumber(4, 7, 0));

        await projects.ImportDirectoryAsync(directory);
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(projects.Items);
        Assert.Equal("Import Probe", item.Title);
        Assert.Equal(directory, item.PathText);
        Assert.Equal("Never opened", item.OpenedText);
        Assert.True(item.HasWarning, "A project whose editor is missing should warn.");
        Assert.Equal("Godot 4.7 is not installed", item.WarningText);
        Assert.Contains(dialogs.Requests, dialog => dialog.Message.Contains("4.7", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ImportingAProject_SelectsAMatchingInstalledEditor()
    {
        using var environment = new TestEnvironment("en");
        var installPath = Path.Combine(environment.Root, "std", "4.8-beta1");
        Directory.CreateDirectory(installPath);
        await environment.Services.Library.AddOrUpdateAsync(InstalledVersion.Create(
            "4.8-beta1",
            GodotEdition.Standard,
            CpuArchitecture.X64,
            "Godot 4.8 Beta1",
            installPath,
            "Godot_v4.8-beta1_win64.exe",
            1024,
            InstallSource.LocalImport));

        var dialogs = new FakeDialogHost();
        var projects = new ProjectsViewModel(environment.Services, dialogs);
        var directory = CreateProject("Physics Sandbox", new VersionNumber(4, 8, 0));

        await projects.ImportDirectoryAsync(directory);
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(projects.Items);
        Assert.NotNull(item.SelectedEditor);
        Assert.Equal("4.8-beta1", item.SelectedEditor!.Installation.VersionId);
        Assert.False(item.HasWarning);
    }

    [AvaloniaFact]
    public async Task ImportingAFolderWithoutAProject_ReportsAnError()
    {
        using var environment = new TestEnvironment("en");
        var dialogs = new FakeDialogHost();
        var projects = new ProjectsViewModel(environment.Services, dialogs);
        var directory = Path.Combine(Path.GetTempPath(), "GodotLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        await projects.ImportDirectoryAsync(directory);

        Assert.Empty(projects.Items);
        Assert.Contains(dialogs.Requests, dialog => dialog.Message.Contains("project.godot", StringComparison.Ordinal));
    }
}
