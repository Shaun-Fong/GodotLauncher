using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Assets;

/// <summary>Thrown when no download candidate could be reached.</summary>
public sealed class AssetResolutionException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public AssetResolutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>A candidate that answered a probe, together with the final URL.</summary>
public sealed record ResolvedAsset
{
    /// <summary>The chosen asset, with size filled in when known.</summary>
    public required DownloadAsset Asset { get; init; }

    /// <summary>URL that actually served the probe response (after redirects).</summary>
    public required Uri FinalUri { get; init; }

    /// <summary>True when the server advertised byte range support.</summary>
    public bool AcceptsRanges { get; init; }
}

/// <summary>
/// Maps a version and target platform to download slugs and URLs. Godot renamed
/// its platform slugs several times (for example <c>x11.64.zip</c> became
/// <c>linux.x86_64.zip</c>), so the resolver returns ordered candidates and
/// probes them instead of trusting a fixed table.
/// </summary>
public sealed class AssetResolver
{
    /// <summary>Official redirect endpoint.
    /// Example: <c>https://downloads.godotengine.org/?version=4.7.2&amp;flavor=stable&amp;slug=win64.exe.zip&amp;platform=windows.64</c>.
    /// </summary>
    public const string OfficialEndpoint = "https://downloads.godotengine.org/";

    /// <summary>Root of the GitHub release downloads.</summary>
    public const string GitHubReleaseRoot = "https://github.com/godotengine/godot-builds/releases/download";

    /// <summary>Ordered slugs for the combination, most likely first.</summary>
    public static IReadOnlyList<string> GetSlugs(GodotEdition edition, CpuArchitecture architecture, HostPlatform platform)
    {
        var mono = edition == GodotEdition.Mono;

        return platform switch
        {
            HostPlatform.Windows => architecture switch
            {
                CpuArchitecture.X64 => [mono ? "mono_win64.zip" : "win64.exe.zip"],
                CpuArchitecture.X86 => [mono ? "mono_win32.zip" : "win32.exe.zip"],
                CpuArchitecture.Arm64 => [mono ? "mono_windows_arm64.zip" : "windows_arm64.exe.zip"],
                _ => [],
            },
            HostPlatform.Linux => architecture switch
            {
                CpuArchitecture.X64 => mono
                    ? ["mono_linux_x86_64.zip", "mono_x11_64.zip"]
                    : ["linux.x86_64.zip", "x11.64.zip"],
                CpuArchitecture.X86 => mono
                    ? ["mono_linux_x86_32.zip", "mono_x11_32.zip"]
                    : ["linux.x86_32.zip", "x11.32.zip"],
                CpuArchitecture.Arm64 => mono
                    ? ["mono_linux_arm64.zip"]
                    : ["linux.arm64.zip"],
                CpuArchitecture.Arm32 => mono
                    ? ["mono_linux_arm32.zip"]
                    : ["linux.arm32.zip"],
                _ => [],
            },
            HostPlatform.MacOS => architecture switch
            {
                CpuArchitecture.Arm64 => mono
                    ? ["mono_macos.arm64.zip", "mono_macos.universal.zip", "mono_osx.universal.zip"]
                    : ["macos.arm64.zip", "macos.universal.zip", "osx.universal.zip"],
                CpuArchitecture.X64 => mono
                    ? ["mono_macos.x86_64.zip", "mono_macos.universal.zip", "mono_osx.universal.zip"]
                    : ["macos.x86_64.zip", "macos.universal.zip", "osx.universal.zip"],
                _ => mono
                    ? ["mono_macos.universal.zip", "mono_osx.universal.zip"]
                    : ["macos.universal.zip", "osx.universal.zip"],
            },
            _ => [],
        };
    }

    /// <summary>
    /// Builds every candidate for the combination, ordered by preference. Each
    /// candidate carries the official URL and the GitHub release fallback.
    /// </summary>
    public IReadOnlyList<DownloadAsset> GetCandidates(
        GodotVersion version,
        GodotEdition edition,
        CpuArchitecture architecture,
        HostPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(version);

        var slugs = GetSlugs(edition, architecture, platform);
        var candidates = new List<DownloadAsset>(slugs.Count);

        foreach (var slug in slugs)
        {
            candidates.Add(new DownloadAsset
            {
                VersionId = version.Id,
                Edition = edition,
                Architecture = architecture,
                Platform = platform,
                Slug = slug,
                DownloadUri = BuildOfficialUri(version, platform, architecture, slug),
                FallbackUri = BuildGitHubUri(version, slug),
            });
        }

        return candidates;
    }

    /// <summary>
    /// Probes candidates with a HEAD request until one answers successfully.
    /// Throws <see cref="AssetResolutionException"/> when nothing is reachable,
    /// so the caller can report a clear error instead of failing mid-download.
    /// </summary>
    public async Task<ResolvedAsset> ResolveAsync(
        HttpClient http,
        GodotVersion version,
        GodotEdition edition,
        CpuArchitecture architecture,
        HostPlatform platform,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (var candidate in GetCandidates(version, edition, architecture, platform))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var uri in new[] { candidate.DownloadUri, candidate.FallbackUri })
            {
                if (uri is null)
                {
                    continue;
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, uri);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        errors.Add($"{uri} -> {(int)response.StatusCode}");
                        continue;
                    }

                    var size = response.Content.Headers.ContentLength;
                    var acceptsRanges = response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase);

                    return new ResolvedAsset
                    {
                        Asset = candidate with
                        {
                            DownloadUri = uri,
                            SizeInBytes = size,
                        },
                        FinalUri = response.RequestMessage?.RequestUri ?? uri,
                        AcceptsRanges = acceptsRanges,
                    };
                }
                catch (HttpRequestException ex)
                {
                    errors.Add($"{uri} -> {ex.Message}");
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    errors.Add($"{uri} -> timed out");
                }
            }
        }

        throw new AssetResolutionException(
            $"No downloadable package was found for {version.Id} ({edition.ToDisplayName()}, {architecture.ToDisplayName()}, {platform.ToDisplayName()}). " +
            (errors.Count > 0 ? string.Join(" | ", errors) : "No candidates are defined for this platform."));
    }

    /// <summary>Builds the official redirect URL for a slug.</summary>
    public static Uri BuildOfficialUri(
        GodotVersion version,
        HostPlatform platform,
        CpuArchitecture architecture,
        string slug)
    {
        var platformParameter = platform switch
        {
            HostPlatform.Windows => architecture switch
            {
                CpuArchitecture.X86 => "windows.32",
                CpuArchitecture.Arm64 => "windows.arm64",
                _ => "windows.64",
            },
            HostPlatform.Linux => architecture switch
            {
                CpuArchitecture.X86 => "linux.32",
                CpuArchitecture.Arm64 => "linux.arm64",
                CpuArchitecture.Arm32 => "linux.arm32",
                _ => "linux.64",
            },
            _ => architecture == CpuArchitecture.Arm64 ? "osx.arm64" : "osx.universal",
        };

        var query =
            $"version={Uri.EscapeDataString(version.Name)}" +
            $"&flavor={Uri.EscapeDataString(version.Flavor)}" +
            $"&slug={Uri.EscapeDataString(slug)}" +
            $"&platform={Uri.EscapeDataString(platformParameter)}";

        return new Uri($"{OfficialEndpoint}?{query}");
    }

    /// <summary>Builds the direct GitHub release asset URL for a slug.</summary>
    public static Uri BuildGitHubUri(GodotVersion version, string slug)
    {
        var versionId = Uri.EscapeDataString(version.Id);
        return new Uri($"{GitHubReleaseRoot}/{versionId}/Godot_v{versionId}_{slug}");
    }
}
