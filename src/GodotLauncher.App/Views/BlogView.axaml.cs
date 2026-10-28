using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GodotLauncher.App.ViewModels;

namespace GodotLauncher.App.Views;

public partial class BlogView : UserControl
{
    private BlogViewModel? _viewModel;

    public BlogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as BlogViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BlogViewModel.PageIndex))
        {
            return;
        }

        // The new posts are swapped in after the change notification, so scroll once layout ran.
        Dispatcher.UIThread.Post(ScrollPostsToTop, DispatcherPriority.Background);
    }

    private void ScrollPostsToTop()
    {
        this.FindControl<ScrollViewer>("PostScroller")?.ScrollToHome();
    }
}
