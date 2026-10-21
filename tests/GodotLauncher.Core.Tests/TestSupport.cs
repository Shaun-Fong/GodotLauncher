namespace GodotLauncher.Core.Tests;

/// <summary>Creates a unique temporary directory that is removed when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "godot-launcher-tests")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Subdirectory(string name)
    {
        var path = System.IO.Path.Combine(Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class TestData
{
    public static string VersionsYml => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "versions.yml");

    public static string ReadVersionsYml() => File.ReadAllText(VersionsYml);
}
