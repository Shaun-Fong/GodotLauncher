using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GodotLauncher.App.ViewModels;
using GodotLauncher.Core.Blog;
using GodotLauncher.Core.Models;

namespace GodotLauncher.App.Tests;

public sealed class BlogViewModelTests
{
    [AvaloniaFact]
    public async Task EnsureLoaded_LoadsTheFirstPageAndBuildsPagination()
    {
        using var environment = new TestEnvironment();
        var client = new FakeBlogClient(lastPage: 3);
        environment.Services.SetBlogClient(client);
        var model = new BlogViewModel(environment.Services);

        await model.EnsureLoadedAsync();
        Pump();

        Assert.Single(model.Posts);
        Assert.Equal(1, model.PageIndex);
        Assert.False(model.CanGoPrevious);
        Assert.True(model.CanGoNext);
        Assert.Equal("Page 1 of 3", model.PageInfo);
        Assert.Equal(["1", "2", "3"], model.PageLinks.Where(link => !link.IsEllipsis).Select(link => link.Label));
        Assert.True(model.PageLinks[0].IsCurrent);
    }

    [AvaloniaFact]
    public async Task LoadingTheLastPageDisablesNext()
    {
        using var environment = new TestEnvironment();
        environment.Services.SetBlogClient(new FakeBlogClient(lastPage: 3));
        var model = new BlogViewModel(environment.Services);
        await model.EnsureLoadedAsync();
        Pump();

        await model.LoadPageAsync(3);
        Pump();

        Assert.Equal(3, model.PageIndex);
        Assert.False(model.CanGoNext);
        Assert.True(model.CanGoPrevious);
        Assert.Equal("Post 3", model.Posts[0].Title);
        Assert.Equal("Page 3 of 3", model.PageInfo);
        Assert.True(model.PageLinks[^1].IsCurrent);
    }

    [AvaloniaFact]
    public async Task PagesAreCachedAndOutOfRangeNumbersAreClamped()
    {
        using var environment = new TestEnvironment();
        var client = new FakeBlogClient(lastPage: 3);
        environment.Services.SetBlogClient(client);
        var model = new BlogViewModel(environment.Services);
        await model.EnsureLoadedAsync();
        Pump();

        await model.LoadPageAsync(2);
        Pump();
        await model.LoadPageAsync(2);
        Pump();
        await model.LoadPageAsync(99);
        Pump();

        Assert.Equal(3, model.PageIndex);
        Assert.Equal(1, client.Requests.Count(page => page == 2));
    }

    [AvaloniaFact]
    public async Task ClickingAPostOpensItsAddress()
    {
        using var environment = new TestEnvironment();
        environment.Services.SetBlogClient(new FakeBlogClient(lastPage: 1));
        string? opened = null;
        environment.Services.OpenExternallyOverride = target => opened = target;
        var model = new BlogViewModel(environment.Services);
        await model.EnsureLoadedAsync();
        Pump();

        model.Posts[0].OpenCommand.Execute(null);

        Assert.Equal("https://example.test/article/1/", opened);
    }

    [AvaloniaFact]
    public async Task ADeadArchiveShowsTheLocalizedError()
    {
        using var environment = new TestEnvironment();
        environment.Services.SetBlogClient(new ThrowingBlogClient());
        var model = new BlogViewModel(environment.Services);

        await model.EnsureLoadedAsync();
        Pump();

        Assert.True(model.HasError);
        Assert.Equal("Could not load the blog.", model.ErrorText);
        Assert.Empty(model.Posts);
        Assert.True(model.IsEmpty);
    }

    [AvaloniaFact]
    public async Task SwitchingLanguageRerendersThePageInfo()
    {
        using var environment = new TestEnvironment();
        environment.Services.SetBlogClient(new FakeBlogClient(lastPage: 3));
        var model = new BlogViewModel(environment.Services);
        await model.EnsureLoadedAsync();
        Pump();

        environment.Services.UpdateSettings(settings => settings with { Language = "zh-CN" }, save: false);
        Pump();

        Assert.Equal("第 1 页，共 3 页", model.PageInfo);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class FakeBlogClient(int lastPage) : IBlogClient
    {
        public List<int> Requests { get; } = [];

        public Task<BlogPage> GetPageAsync(int page, CancellationToken cancellationToken = default)
        {
            Requests.Add(page);
            return Task.FromResult(new BlogPage
            {
                Number = page,
                HasNext = page < lastPage,
                Posts =
                [
                    new BlogPost
                    {
                        Title = $"Post {page}",
                        Url = $"https://example.test/article/{page}/",
                        Author = "Author",
                        DateText = "1 October 2026",
                        Summary = "Summary"
                    }
                ]
            });
        }

        public Task<int> GetLastPageAsync(CancellationToken cancellationToken = default) => Task.FromResult(lastPage);
    }

    private sealed class ThrowingBlogClient : IBlogClient
    {
        public Task<BlogPage> GetPageAsync(int page, CancellationToken cancellationToken = default) =>
            throw new BlogException("offline");

        public Task<int> GetLastPageAsync(CancellationToken cancellationToken = default) =>
            throw new BlogException("offline");
    }
}
