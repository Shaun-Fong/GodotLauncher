using System.Runtime.InteropServices;

namespace GodotLauncher.Core.Platform;

/// <summary>Operating system family supported by the launcher.</summary>
public enum HostPlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>Helpers for <see cref="HostPlatform"/>.</summary>
public static class HostPlatformExtensions
{
    /// <summary>Human readable platform name.</summary>
    public static string ToDisplayName(this HostPlatform platform) => platform switch
    {
        HostPlatform.Windows => "Windows",
        HostPlatform.Linux => "Linux",
        HostPlatform.MacOS => "macOS",
        _ => platform.ToString(),
    };

    /// <summary>The platform this process is running on.</summary>
    public static HostPlatform Current =>
        OperatingSystem.IsWindows() ? HostPlatform.Windows
        : OperatingSystem.IsMacOS() ? HostPlatform.MacOS
        : HostPlatform.Linux;
}

/// <summary>CPU architecture of the machine the launcher runs on.</summary>
public static class HostArchitecture
{
    /// <summary>Best-effort detection of the current process architecture.</summary>
    public static Models.CpuArchitecture Current => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => Models.CpuArchitecture.X86,
        Architecture.Arm64 => Models.CpuArchitecture.Arm64,
        Architecture.Arm => Models.CpuArchitecture.Arm32,
        _ => Models.CpuArchitecture.X64,
    };
}
