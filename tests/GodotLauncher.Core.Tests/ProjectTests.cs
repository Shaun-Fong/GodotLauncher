using GodotLauncher.Core.Launch;
using GodotLauncher.Core.Projects;
using GodotLauncher.Core.Storage;
using Xunit;

namespace GodotLauncher.Core.Tests;

public sealed class ProjectTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Parse_ReadsNameVersionFeaturesAndScene()
    {
        var path = _temp.File("project.godot");
        File.WriteAllText(path, """
            ; Engine configuration file.
            config_version=5

            [application]

            config/name="Demo Platformer"
            run/main_scene="res://main.tscn"
            config/features=PackedStringArray("4.8", "GL Compatibility")
            """);

        var project = GodotProjectFile.Load(path);

        Assert.Equal("Demo Platformer", project.Name);
        Assert.Equal("4.8", project.VersionText);
        Assert.NotNull(project.Version);
        Assert.Equal(5, project.ConfigVersion);
        Assert.Equal("res://main.tscn", project.MainScene);
        Assert.Contains("GL Compatibility", project.Features);
        Assert.Equal("Demo Platformer", project.DisplayName);
    }

    [Fact]
    public void Parse_FallsBackToMajorVersionForShortFeatureList()
    {
        var path = _temp.File("project.godot");
        File.WriteAllText(path, """
            config_version=4

            [application]

            config/name="Legacy"
            config/features=PackedStringArray("GLES3")
            """);

        var project = GodotProjectFile.Load(path);

        Assert.Null(project.Version);
        Assert.Equal("3", project.VersionText);
    }

    [Fact]
    public void DisplayName_UsesDirectoryWhenNameMissing()
    {
        var directory = _temp.Subdirectory("MyGame");
        var path = System.IO.Path.Combine(directory, "project.godot");
        File.WriteAllText(path, "config_version=5\n");

        var project = GodotProjectFile.Load(path);

        Assert.Equal("MyGame", project.DisplayName);
    }

    [Fact]
    public void Create_WritesParsableFileAndRefusesOverwrite()
    {
        var directory = _temp.Subdirectory("NewGame");

        var created = ProjectCreator.Create(directory, "New Game", new GodotLauncher.Core.Models.VersionNumber(4, 8, 0));

        Assert.True(File.Exists(created.FilePath));
        var parsed = GodotProjectFile.Load(created.FilePath);
        Assert.Equal("New Game", parsed.Name);
        Assert.Equal("4.8", parsed.VersionText);
        Assert.Throws<ProjectException>(() =>
            ProjectCreator.Create(directory, "Again", new GodotLauncher.Core.Models.VersionNumber(4, 8, 0)));
    }

    [Fact]
    public async Task Store_PersistsEditorAndOrderAndRemovesEntries()
    {
        var store = new ProjectStore(System.IO.Path.Combine(_temp.Path, "projects.json"));
        await store.LoadAsync();
        var older = new ProjectRecord
        {
            Path = @"C:\games\older",
            Name = "Older",
            VersionText = "4.7",
            EditorId = null,
            AddedAt = DateTimeOffset.UtcNow.AddDays(-2),
            LastOpened = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var newer = older with { Path = @"C:\games\newer", Name = "Newer", LastOpened = DateTimeOffset.UtcNow };

        await store.AddOrUpdateAsync(older);
        await store.AddOrUpdateAsync(newer);
        await store.SetEditorAsync(newer.Path, "4.8-beta1|standard|x64");

        var reloaded = new ProjectStore(System.IO.Path.Combine(_temp.Path, "projects.json"));
        await reloaded.LoadAsync();

        Assert.Equal(2, reloaded.Items.Count);
        Assert.Equal("Newer", reloaded.Items[0].Name);
        Assert.Equal("4.8-beta1|standard|x64", reloaded.Items[0].EditorId);
        Assert.True(await reloaded.RemoveAsync(newer.Path, deleteFromDisk: false));
        Assert.Single(reloaded.Items);
    }

    [Fact]
    public void BuildArguments_PrependsProjectArguments()
    {
        var options = new LaunchOptions
        {
            ProjectPath = @"C:\games\demo",
            Arguments = ["--verbose"],
        };

        var arguments = EditorLauncher.BuildArguments("--editor-pid 42", options);

        Assert.Equal(
            ["--path", @"C:\games\demo", "--editor", "--editor-pid", "42", "--verbose"],
            arguments);
    }

    [Fact]
    public void BuildArguments_WithoutProjectKeepsUserArguments()
    {
        var arguments = EditorLauncher.BuildArguments(null, new LaunchOptions { Arguments = ["--verbose"] });

        Assert.Equal(["--verbose"], arguments);
    }
}
