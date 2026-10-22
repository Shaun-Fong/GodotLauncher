namespace GodotLauncher.Core.Models;

/// <summary>Which Godot edition an installation provides.</summary>
public enum GodotEdition
{
    /// <summary>The regular editor download.</summary>
    Standard = 0,

    /// <summary>The .NET (formerly Mono) editor download.</summary>
    Mono = 1,
}

/// <summary>Helpers for <see cref="GodotEdition"/>.</summary>
public static class GodotEditionExtensions
{
    /// <summary>Short label used in the UI ("Standard" / ".NET").</summary>
    public static string ToDisplayName(this GodotEdition edition) =>
        edition == GodotEdition.Mono ? ".NET" : "Standard";

    /// <summary>Name of the install subdirectory that holds this edition.</summary>
    public static string ToFolderName(this GodotEdition edition) =>
        edition == GodotEdition.Mono ? "dotnet" : "std";

    /// <summary>
    /// Folder name used by launchers before 0.4.1 for the .NET edition. Only kept so
    /// existing installations can be moved to <c>dotnet</c> at startup.
    /// </summary>
    public static string ToLegacyFolderName(this GodotEdition edition) =>
        edition == GodotEdition.Mono ? "mono" : "std";
}

/// <summary>CPU architecture of a download or installation.</summary>
public enum CpuArchitecture
{
    X64,
    X86,
    Arm64,
    Arm32,
}

/// <summary>Helpers for <see cref="CpuArchitecture"/>.</summary>
public static class CpuArchitectureExtensions
{
    /// <summary>Human readable architecture label.</summary>
    public static string ToDisplayName(this CpuArchitecture architecture) => architecture switch
    {
        CpuArchitecture.X64 => "x86_64",
        CpuArchitecture.X86 => "x86_32",
        CpuArchitecture.Arm64 => "arm64",
        CpuArchitecture.Arm32 => "arm32",
        _ => architecture.ToString(),
    };
}
