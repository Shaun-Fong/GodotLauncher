using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GodotLauncher.App.Services;
using GodotLauncher.Core.Models;

namespace GodotLauncher.App.ViewModels;

/// <summary>One blog card: title, meta line, teaser and a lazily loaded cover image.</summary>
public sealed partial class BlogPostViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action<BlogPostViewModel> _open;

    public BlogPostViewModel(AppServices services, BlogPost post, Action<BlogPostViewModel> open)
    {
        _services = services;
        _open = open;
        Post = post;
        Title = post.Title;
        Url = post.Url;
        Summary = post.Summary;
        HasSummary = post.Summary.Length > 0;
        MetaText = string.Join(
            " · ",
            new[] { post.Author, post.DateText }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(post.ImageUrl))
        {
            _ = LoadThumbnailAsync(post.ImageUrl);
        }
    }

    public BlogPost Post { get; }

    public string Title { get; }

    public string Url { get; }

    public string Summary { get; }

    public bool HasSummary { get; }

    /// <summary>Author and date, for example "Bastiaan Olij · 25 October 2026".</summary>
    public string MetaText { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    [RelayCommand]
    private void Open() => _open(this);

    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    private async Task LoadThumbnailAsync(string url)
    {
        var bitmap = await ThumbnailLoader.LoadAsync(_services.Http, url).ConfigureAwait(false);
        if (bitmap is null)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bitmap);
    }
}
