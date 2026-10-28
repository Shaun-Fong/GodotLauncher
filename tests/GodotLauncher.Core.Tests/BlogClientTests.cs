using GodotLauncher.Core.Blog;

namespace GodotLauncher.Core.Tests;

public sealed class BlogClientTests
{
    [Fact]
    public void ParsePosts_ReadsEveryCardFromTheArchivePage()
    {
        var posts = BlogClient.ParsePosts(ReadFixture());

        Assert.Equal(24, posts.Count);
        var first = posts[0];
        Assert.Equal("Godot XR Community Game Jam VI", first.Title);
        Assert.Equal("Bastiaan Olij", first.Author);
        Assert.Equal("25 October 2026", first.DateText);
        Assert.StartsWith("Results of the sixth Godot XR Community Game Jam", first.Summary);
        Assert.StartsWith("https://godotengine.org/article/", first.Url);
        Assert.StartsWith("https://godotengine.org/storage/blog/covers/", first.ImageUrl);
        Assert.All(posts, post =>
        {
            Assert.False(string.IsNullOrWhiteSpace(post.Title));
            Assert.StartsWith("https://godotengine.org/article/", post.Url);
            Assert.DoesNotContain("&nbsp;", post.DateText);
            Assert.DoesNotContain("<", post.Title);
        });
        Assert.DoesNotContain(posts, post => post.DateText.StartsWith('-'));
    }

    [Fact]
    public void ParsePosts_ResolvesLinksAgainstTheRequestedSite()
    {
        var site = new Uri("http://127.0.0.1:9/blog/");

        var posts = BlogClient.ParsePosts(ReadFixture(), site);

        Assert.All(posts, post => Assert.StartsWith("http://127.0.0.1:9/article/", post.Url));
        Assert.All(posts, post => Assert.StartsWith("http://127.0.0.1:9/storage/", post.ImageUrl));
    }

    [Fact]
    public async Task GetPageAsync_LoadsTheRequestedArchivePage()
    {
        await using var server = new TestHttpServer(request => Task.FromResult<TestResponse?>(
            request.Path switch
            {
                "/blog/" => TestResponse.Text(200, BuildPage(1, hasNext: true)),
                "/blog/2/" => TestResponse.Text(200, BuildPage(2, hasNext: false)),
                _ => TestResponse.Text(404, "missing")
            }));
        using var http = new HttpClient();
        var client = new BlogClient(http, $"{server.BaseUrl}/blog/");

        var page = await client.GetPageAsync(2);

        Assert.Equal(2, page.Number);
        Assert.Single(page.Posts);
        Assert.Equal("Post 2", page.Posts[0].Title);
        Assert.StartsWith($"{server.BaseUrl}/article/post-2/", page.Posts[0].Url);
        Assert.False(page.HasNext);
    }

    [Fact]
    public async Task GetPageAsync_ReportsAMissingPage()
    {
        await using var server = new TestHttpServer(_ => Task.FromResult<TestResponse?>(TestResponse.Text(404, "nope")));
        using var http = new HttpClient();
        var client = new BlogClient(http, $"{server.BaseUrl}/blog/");

        var error = await Assert.ThrowsAsync<BlogException>(() => client.GetPageAsync(1));

        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public async Task GetPageAsync_RejectsPagesWithoutCards()
    {
        await using var server = new TestHttpServer(_ =>
            Task.FromResult<TestResponse?>(TestResponse.Text(200, "<html><body>maintenance</body></html>")));
        using var http = new HttpClient();
        var client = new BlogClient(http, $"{server.BaseUrl}/blog/");

        await Assert.ThrowsAsync<BlogException>(() => client.GetPageAsync(1));
    }

    [Fact]
    public async Task GetLastPageAsync_BinarySearchesTheArchive()
    {
        await using var server = new TestHttpServer(request =>
        {
            var page = request.Path switch
            {
                "/blog/" => 1,
                _ when request.Path.StartsWith("/blog/") && int.TryParse(request.Path[6..^1], out var number) => number,
                _ => 0
            };
            return Task.FromResult<TestResponse?>(page is >= 1 and <= 3
                ? TestResponse.Text(200, BuildPage(page, hasNext: page < 3))
                : TestResponse.Text(404, "missing"));
        });
        using var http = new HttpClient();
        var client = new BlogClient(http, $"{server.BaseUrl}/blog/");

        var last = await client.GetLastPageAsync();

        Assert.Equal(3, last);
        Assert.InRange(server.RequestCount, 3, 20);
    }

    [Fact]
    public void ParsePosts_IgnoresCardsWithoutATitle()
    {
        const string html = """
            <a href="/article/broken/"><article class="article-card"><h3></h3></article></a>
            <a href="/article/ok/"><article class="article-card"><h3>Keep me</h3></article></a>
            """;

        var posts = BlogClient.ParsePosts(html);

        var post = Assert.Single(posts);
        Assert.Equal("Keep me", post.Title);
    }

    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "blog.html"));

    private static string BuildPage(int number, bool hasNext)
    {
        var navigation = hasNext ? "<a class=\"pagination-next\" href=\"/blog/2/\">next</a>" : string.Empty;
        return $"""
            <html><body>
            <a href="/article/post-{number}/"><article class="article-card">
              <div class="thumbnail" style="background-image:url(/storage/blog/covers/post-{number}.jpg)"></div>
              <span class="by">Author {number}</span><span class="date">&nbsp;-&nbsp;1 October 2026</span>
              <h3>Post {number}</h3><p class="excerpt">Summary {number}</p>
            </article></a>
            {navigation}
            </body></html>
            """;
    }
}
