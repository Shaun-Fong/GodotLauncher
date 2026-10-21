using System.Net;
using System.Security.Cryptography;
using GodotLauncher.Core.Assets;
using GodotLauncher.Core.Downloads;
using GodotLauncher.Core.Install;
using GodotLauncher.Core.Models;
using GodotLauncher.Core.Platform;

namespace GodotLauncher.Core.Tests;

/// <summary>Intercepts HTTP calls so resolver tests never touch the network.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

public sealed class AssetResolverTests
{
    private static readonly GodotVersion Stable = GodotVersion.Create("4.7.2", "stable", new DateOnly(2026, 8, 18));

    [Fact]
    public void Windows64Standard_UsesWin64Slug()
    {
        var candidates = new AssetResolver().GetCandidates(Stable, GodotEdition.Standard, CpuArchitecture.X64, HostPlatform.Windows);

        var asset = Assert.Single(candidates);
        Assert.Equal("win64.exe.zip", asset.Slug);
        Assert.Contains("version=4.7.2", asset.DownloadUri.Query);
        Assert.Contains("flavor=stable", asset.DownloadUri.Query);
        Assert.Contains("slug=win64.exe.zip", asset.DownloadUri.Query);
        Assert.Contains("platform=windows.64", asset.DownloadUri.Query);
        Assert.Equal(
            "https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable/Godot_v4.7.2-stable_win64.exe.zip",
            asset.FallbackUri!.AbsoluteUri);
        Assert.Equal("4.7.2-stable_std_win64.exe.zip", asset.FileName);
    }

    [Theory]
    [InlineData(GodotEdition.Mono, CpuArchitecture.Arm64, "mono_windows_arm64.zip")]
    [InlineData(GodotEdition.Mono, CpuArchitecture.X86, "mono_win32.zip")]
    [InlineData(GodotEdition.Standard, CpuArchitecture.Arm64, "windows_arm64.exe.zip")]
    public void Windows_CoversEveryArchitecture(GodotEdition edition, CpuArchitecture architecture, string expectedSlug)
    {
        var candidates = new AssetResolver().GetCandidates(Stable, edition, architecture, HostPlatform.Windows);
        Assert.Equal(expectedSlug, candidates[0].Slug);
    }

    [Fact]
    public void Linux_OffersLegacySlugAsSecondCandidate()
    {
        var candidates = new AssetResolver().GetCandidates(Stable, GodotEdition.Standard, CpuArchitecture.X64, HostPlatform.Linux);

        Assert.Equal(new[] { "linux.x86_64.zip", "x11.64.zip" }, candidates.Select(c => c.Slug));
        Assert.Contains("platform=linux.64", candidates[0].DownloadUri.Query);
    }

    [Fact]
    public void MacOS_PrefersArchitectureSpecificSlugAndFallsBackToUniversal()
    {
        var candidates = new AssetResolver().GetCandidates(Stable, GodotEdition.Standard, CpuArchitecture.Arm64, HostPlatform.MacOS);

        Assert.Equal(new[] { "macos.arm64.zip", "macos.universal.zip", "osx.universal.zip" }, candidates.Select(c => c.Slug));
    }

    [Fact]
    public void MirrorTemplate_IsInsertedBeforeOfficialCandidates()
    {
        var resolver = new AssetResolver("https://mirror.example/godot/{versionId}/{fileName}");
        var candidates = resolver.GetCandidates(Stable, GodotEdition.Standard, CpuArchitecture.X64, HostPlatform.Windows);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(
            "https://mirror.example/godot/4.7.2-stable/Godot_v4.7.2-stable_win64.exe.zip",
            candidates[0].DownloadUri.AbsoluteUri);
        Assert.StartsWith(AssetResolver.OfficialEndpoint, candidates[0].FallbackUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ResolveAsync_ProbesAndReturnsSize()
    {
        var handler = new StubHttpHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            response.Content = new ByteArrayContent(new byte[4096]);
            response.Headers.TryAddWithoutValidation("Accept-Ranges", "bytes");
            return response;
        });

        using var http = new HttpClient(handler);
        var resolved = await new AssetResolver().ResolveAsync(
            http, Stable, GodotEdition.Standard, CpuArchitecture.X64, HostPlatform.Windows);

        Assert.Equal("win64.exe.zip", resolved.Asset.Slug);
        Assert.Equal(4096, resolved.Asset.SizeInBytes);
        Assert.True(resolved.AcceptsRanges);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsWhenNothingIsReachable()
    {
        var handler = new StubHttpHandler(request => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = request,
        });

        using var http = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<AssetResolutionException>(() => new AssetResolver().ResolveAsync(
            http, Stable, GodotEdition.Standard, CpuArchitecture.X64, HostPlatform.Windows));

        Assert.Contains("4.7.2-stable", exception.Message);
    }
}

public sealed class ExeNameParserTests
{
    [Fact]
    public void Parse_ReadsWindowsMonoBuild()
    {
        var parsed = ExeNameParser.Parse("Godot_v4.7.2-stable_mono_win64.exe");

        Assert.NotNull(parsed);
        Assert.Equal("4.7.2-stable", parsed.VersionId);
        Assert.Equal(GodotEdition.Mono, parsed.Edition);
        Assert.Equal(CpuArchitecture.X64, parsed.Architecture);
        Assert.Equal("Godot 4.7.2", parsed.DisplayName);
    }

    [Fact]
    public void Parse_ReadsLinuxBuild()
    {
        var parsed = ExeNameParser.Parse("Godot_v4.3-beta2_linux.x86_64");

        Assert.NotNull(parsed);
        Assert.Equal("4.3-beta2", parsed.VersionId);
        Assert.Equal(ReleaseChannel.Beta, ReleaseChannelExtensions.ClassifyFlavor(parsed.Flavor).Channel);
    }

    [Fact]
    public void Parse_RejectsUnrelatedExecutables()
    {
        Assert.Null(ExeNameParser.Parse("notepad.exe"));
        Assert.Null(ExeNameParser.Parse("Godot.exe"));
        Assert.Null(ExeNameParser.Parse(string.Empty));
    }
}

public sealed class HttpDownloaderTests
{
    private static byte[] CreatePayload(int size)
    {
        var payload = new byte[size];
        for (var index = 0; index < size; index++)
        {
            payload[index] = (byte)((index * 31) + 7);
        }

        return payload;
    }

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class RecordingProgress : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Items { get; } = [];

        public void Report(DownloadProgress value) => Items.Add(value);
    }

    [Fact]
    public async Task Download_VerifiesHashAndWritesFile()
    {
        var payload = CreatePayload(200_000);
        await using var server = new TestHttpServer(_ => Task.FromResult<TestResponse?>(
            TestResponse.FromBody(200, payload)));

        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var destination = temp.File("godot.zip");
        var progress = new RecordingProgress();

        var result = await new HttpDownloader(http).DownloadAsync(
            new DownloadRequest
            {
                Uri = new Uri(server.BaseUrl + "/godot.zip"),
                DestinationPath = destination,
                ExpectedSize = payload.Length,
                ExpectedSha256 = Sha(payload),
            },
            progress);

        Assert.True(result.Verified);
        Assert.Equal(payload.Length, result.Size);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
        Assert.NotEmpty(progress.Items);
    }

    [Fact]
    public async Task Download_ResumesFromPartialFile()
    {
        var payload = CreatePayload(120_000);
        var split = 50_000;

        await using var server = new TestHttpServer(request =>
        {
            if (request.TryGetHeader("Range", out var range) && range.StartsWith("bytes=", StringComparison.Ordinal))
            {
                var start = int.Parse(range[6..].TrimEnd('-'));
                var slice = payload[start..];
                return Task.FromResult<TestResponse?>(new TestResponse(
                    206,
                    slice,
                    new Dictionary<string, string>
                    {
                        ["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}",
                        ["Accept-Ranges"] = "bytes",
                    }));
            }

            return Task.FromResult<TestResponse?>(TestResponse.FromBody(200, payload));
        });

        using var temp = new TempDirectory();
        var destination = temp.File("godot.zip");
        await File.WriteAllBytesAsync(destination + ".part", payload[..split]);

        using var http = new HttpClient();
        var result = await new HttpDownloader(http).DownloadAsync(new DownloadRequest
        {
            Uri = new Uri(server.BaseUrl + "/godot.zip"),
            DestinationPath = destination,
            ExpectedSize = payload.Length,
        });

        Assert.Equal(payload.Length, result.Size);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task Download_UsesFallbackWhenPrimaryIsMissing()
    {
        var payload = CreatePayload(4096);
        await using var server = new TestHttpServer(request => Task.FromResult<TestResponse?>(
            request.Path.Contains("fallback", StringComparison.Ordinal)
                ? TestResponse.FromBody(200, payload)
                : new TestResponse(404, [])));

        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var result = await new HttpDownloader(http).DownloadAsync(new DownloadRequest
        {
            Uri = new Uri(server.BaseUrl + "/primary.zip"),
            FallbackUri = new Uri(server.BaseUrl + "/fallback.zip"),
            DestinationPath = temp.File("godot.zip"),
            ExpectedSize = payload.Length,
        });

        Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath));
        Assert.Contains("fallback", result.UsedUri.AbsoluteUri);
    }

    [Fact]
    public async Task Download_RetriesTransientServerErrors()
    {
        var payload = CreatePayload(4096);
        await using var server = new TestHttpServer((_, index) => Task.FromResult<TestResponse?>(
            index == 0 ? new TestResponse(500, []) : TestResponse.FromBody(200, payload)));

        using var temp = new TempDirectory();
        using var http = new HttpClient();
        var result = await new HttpDownloader(http).DownloadAsync(new DownloadRequest
        {
            Uri = new Uri(server.BaseUrl + "/godot.zip"),
            DestinationPath = temp.File("godot.zip"),
            ExpectedSize = payload.Length,
            MaxAttempts = 2,
        });

        Assert.Equal(payload, await File.ReadAllBytesAsync(result.FilePath));
        Assert.Equal(2, server.RequestCount);
    }

    [Fact]
    public async Task Download_RejectsChecksumMismatchAndDeletesPartialFile()
    {
        var payload = CreatePayload(2048);
        await using var server = new TestHttpServer(_ => Task.FromResult<TestResponse?>(TestResponse.FromBody(200, payload)));

        using var temp = new TempDirectory();
        var destination = temp.File("godot.zip");
        using var http = new HttpClient();

        var exception = await Assert.ThrowsAsync<DownloadException>(() => new HttpDownloader(http).DownloadAsync(
            new DownloadRequest
            {
                Uri = new Uri(server.BaseUrl + "/godot.zip"),
                DestinationPath = destination,
                ExpectedSize = payload.Length,
                ExpectedSha256 = new string('a', 64),
            }));

        Assert.True(exception.IsFatal);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task Download_ThrowsWhenEverySourceFails()
    {
        await using var server = new TestHttpServer(_ => Task.FromResult<TestResponse?>(new TestResponse(404, [])));
        using var temp = new TempDirectory();
        using var http = new HttpClient();

        await Assert.ThrowsAsync<DownloadException>(() => new HttpDownloader(http).DownloadAsync(new DownloadRequest
        {
            Uri = new Uri(server.BaseUrl + "/missing.zip"),
            DestinationPath = temp.File("godot.zip"),
        }));
    }
}
