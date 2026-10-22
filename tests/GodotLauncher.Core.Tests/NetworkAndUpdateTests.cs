using System.Net;
using System.Text;
using GodotLauncher.Core.Net;
using GodotLauncher.Core.Settings;
using GodotLauncher.Core.Storage;
using GodotLauncher.Core.Updates;
using Xunit;

namespace GodotLauncher.Core.Tests;

public sealed class NetworkSettingsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void SettingsStore_RoundTripsProxyAndNeverInterval()
    {
        var path = System.IO.Path.Combine(_temp.Path, "config.toml");
        var store = new SettingsStore(path);
        var settings = LauncherSettings.Default with
        {
            Proxy = ProxyMode.Custom,
            ProxyUrl = "http://127.0.0.1:7890",
            RefreshIntervalHours = 0,
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(ProxyMode.Custom, loaded.Proxy);
        Assert.Equal("http://127.0.0.1:7890", loaded.ProxyUrl);
        Assert.Equal(0, loaded.RefreshIntervalHours);
        Assert.Contains("[network]", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseProxy_ReturnsNullForEmptyValues(string? value)
    {
        Assert.Null(ProxySupport.ParseProxy(value));
    }

    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [InlineData("http://proxy.local:8080", "http://proxy.local:8080/")]
    [InlineData("https://proxy.local:8443", "https://proxy.local:8443/")]
    public void ParseProxy_AcceptsHostPortAndUrls(string value, string expected)
    {
        var proxy = ProxySupport.ParseProxy(value);

        Assert.NotNull(proxy);
        var address = proxy!.GetProxy(new Uri("https://github.com"));
        Assert.Equal(expected, address?.ToString());
    }

    [Fact]
    public void ParseProxy_RejectsUnsupportedSchemes()
    {
        Assert.Null(ProxySupport.ParseProxy("socks5://127.0.0.1:1080"));
    }
}

public sealed class UpdateCheckerTests
{
    [Fact]
    public async Task CheckAsync_ReturnsNewerRelease()
    {
        using var handler = new StubHttpHandler(_ => Json(HttpStatusCode.OK, """
            {"tag_name":"v0.3.0","html_url":"https://example.test/releases/v0.3.0","body":"notes",
             "assets":[{"name":"GodotLauncher-win-x64.zip","browser_download_url":"https://example.test/win.zip","size":1234}]}
            """));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        var release = await checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 2, 0));

        Assert.NotNull(release);
        Assert.Equal("v0.3.0", release!.Tag);
        Assert.Equal(["GodotLauncher-win-x64.zip"], release.Assets.Select(asset => asset.Name));
        Assert.Equal(1234, release.Assets[0].Size);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenCurrentOrOlder()
    {
        using var handler = new StubHttpHandler(_ => Json(HttpStatusCode.OK, """{"tag_name":"v0.2.0","assets":[]}"""));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        Assert.Null(await checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 2, 0)));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenNoReleaseExists()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        Assert.Null(await checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 1, 0)));
    }

    [Fact]
    public async Task CheckAsync_ThrowsOnServerError()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 1, 0)));
    }

    [Fact]
    public async Task CheckAsync_FallsBackToTheReleasePageWhenRateLimited()
    {
        var tagUri = new Uri("https://github.com/owner/repo/releases/tag/v0.9.0");
        using var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>release</html>", Encoding.UTF8, "text/html"),
            };
            response.Headers.Location = tagUri;
            return response;
        });
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        var release = await checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 4, 0));

        Assert.NotNull(release);
        Assert.Equal("v0.9.0", release!.Tag);
        var asset = Assert.Single(release.Assets, a => a.Name == "GodotLauncher-win-x64.zip");
        Assert.Equal(
            "https://github.com/owner/repo/releases/download/v0.9.0/GodotLauncher-win-x64.zip",
            asset.DownloadUri.ToString());
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenTheFallbackPageHasNoRelease()
    {
        using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var checker = new UpdateChecker(http, "owner", "repo");

        Assert.Null(await checker.CheckAsync(new GodotLauncher.Core.Models.VersionNumber(0, 1, 0)));
    }

    [Fact]
    public void TryParseTag_StripsPrefix()
    {
        Assert.True(UpdateChecker.TryParseTag("v1.2.3", out var version));
        Assert.Equal("1.2.3", version.ToString());
        Assert.False(UpdateChecker.TryParseTag("nightly", out _));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
