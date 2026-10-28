using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Mvvm;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Blog;
using GodotLauncher.Core.Storage;

namespace GodotLauncher.App.ViewModels;

/// <summary>One button in the pagination row.</summary>
public sealed class BlogPageLink
{
    public BlogPageLink(string label, bool isCurrent, ICommand? command, bool isEllipsis = false)
    {
        Label = label;
        IsCurrent = isCurrent;
        Command = command;
        IsEllipsis = isEllipsis;
    }

    public string Label { get; }

    public bool IsCurrent { get; }

    public bool IsEllipsis { get; }

    public ICommand? Command { get; }
}

/// <summary>
/// Reads the official Godot blog page by page. Pages are cached for the session and the last page
/// number is discovered in the background so the footer can offer a full page selector.
/// </summary>
public sealed partial class BlogViewModel : ViewModelBase
{
    private readonly Dictionary<int, BlogPage> _pages = [];
    private bool _discoveringLastPage;
    private int _lastPage;
    private bool _hasNext;

    public BlogViewModel(AppServices services)
        : base(services)
    {
    }

    public ObservableCollection<BlogPostViewModel> Posts { get; } = [];

    public ObservableCollection<BlogPageLink> PageLinks { get; } = [];

    [ObservableProperty]
    private int _pageIndex = 1;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _pageInfo = string.Empty;

    public bool HasPosts => Posts.Count > 0;

    /// <summary>The empty hint is only useful when the archive answered but had nothing to show.</summary>
    public bool ShowEmptyState => IsEmpty && !HasError;

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

    public bool CanGoPrevious => PageIndex > 1;

    public bool CanGoNext => _hasNext;

    /// <summary>Loads page one the first time the blog page becomes visible.</summary>
    public Task EnsureLoadedAsync()
    {
        if (Posts.Count > 0 || IsLoading)
        {
            return Task.CompletedTask;
        }

        return LoadPageAsync(1);
    }

    [RelayCommand]
    private Task ReloadAsync()
    {
        _pages.Clear();
        _lastPage = 0;
        BuildPageLinks();
        return LoadPageAsync(PageIndex);
    }

    [RelayCommand]
    private void Previous()
    {
        if (CanGoPrevious)
        {
            _ = LoadPageAsync(PageIndex - 1);
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CanGoNext)
        {
            _ = LoadPageAsync(PageIndex + 1);
        }
    }

    [RelayCommand]
    private void Retry() => _ = LoadPageAsync(PageIndex);

    internal async Task LoadPageAsync(int page)
    {
        if (IsLoading)
        {
            return;
        }

        if (_lastPage > 0)
        {
            page = Math.Clamp(page, 1, _lastPage);
        }

        if (page < 1)
        {
            page = 1;
        }

        IsLoading = true;
        HasError = false;
        ErrorText = string.Empty;
        try
        {
            if (!_pages.TryGetValue(page, out var result))
            {
                result = await Services.Blog.GetPageAsync(page).ConfigureAwait(true);
                _pages[page] = result;
            }

            PageIndex = page;
            _hasNext = result.HasNext;
            Posts.Clear();
            foreach (var post in result.Posts)
            {
                Posts.Add(new BlogPostViewModel(Services, post, OpenPost));
            }

            IsEmpty = Posts.Count == 0;
            OnPropertyChanged(nameof(HasPosts));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            BuildPageLinks();
            UpdatePageInfo();

            if (_lastPage == 0 && !_discoveringLastPage)
            {
                _ = DiscoverLastPageAsync();
            }
        }
        catch (Exception ex) when (ex is BlogException or HttpRequestException or TaskCanceledException)
        {
            Services.Log.Error("Could not load the blog page.", ex);
            HasError = true;
            ErrorText = L.Blog.Error;
            IsEmpty = Posts.Count == 0;
            BuildPageLinks();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Probes the archive in the background so the footer can show the real page count.</summary>
    private async Task DiscoverLastPageAsync()
    {
        _discoveringLastPage = true;
        try
        {
            var last = await Services.Blog.GetLastPageAsync().ConfigureAwait(true);
            if (last <= 0)
            {
                return;
            }

            _lastPage = last;
            BuildPageLinks();
            UpdatePageInfo();
        }
        catch (Exception ex) when (ex is BlogException or HttpRequestException or TaskCanceledException)
        {
            Services.Log.Warn($"Could not determine the last blog page: {ex.Message}");
        }
        finally
        {
            _discoveringLastPage = false;
        }
    }

    private void OpenPost(BlogPostViewModel post)
    {
        Services.OpenExternally(post.Url);
        Services.Log.Info($"Opened blog post '{post.Title}' ({post.Url}).");
    }

    private void BuildPageLinks()
    {
        PageLinks.Clear();
        if (_lastPage <= 0)
        {
            var first = Math.Max(1, PageIndex - 2);
            for (var page = first; page <= PageIndex + 2; page++)
            {
                AddPageLink(page);
            }

            return;
        }

        if (_lastPage <= 7)
        {
            for (var page = 1; page <= _lastPage; page++)
            {
                AddPageLink(page);
            }

            return;
        }

        AddPageLink(1);
        var start = Math.Clamp(PageIndex - 2, 2, _lastPage - 4);
        var end = Math.Min(_lastPage - 1, start + 3);
        if (start > 2)
        {
            PageLinks.Add(new BlogPageLink("…", false, null, isEllipsis: true));
        }

        for (var page = start; page <= end; page++)
        {
            AddPageLink(page);
        }

        if (end < _lastPage - 1)
        {
            PageLinks.Add(new BlogPageLink("…", false, null, isEllipsis: true));
        }

        AddPageLink(_lastPage);
    }

    private void AddPageLink(int page)
    {
        PageLinks.Add(new BlogPageLink(
            page.ToString(),
            page == PageIndex,
            new RelayCommand(() => _ = LoadPageAsync(page))));
    }

    private void UpdatePageInfo()
    {
        PageInfo = _lastPage > 0
            ? string.Format(L.Blog.PageInfo, PageIndex, _lastPage)
            : string.Format(L.Blog.PageInfoUnknown, PageIndex);
    }

    protected override void OnLanguageChanged()
    {
        UpdatePageInfo();
        if (HasError)
        {
            ErrorText = L.Blog.Error;
        }

        BuildPageLinks();
    }
}
