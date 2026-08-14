using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Posts;

namespace BlogIt.MauiAdmin.Views.Posts;

public partial class PostListPage : ContentPage
{
    private readonly PostListViewModel _viewModel;

    public PostListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<PostListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
