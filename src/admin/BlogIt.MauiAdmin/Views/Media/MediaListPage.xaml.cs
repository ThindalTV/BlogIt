using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Media;

namespace BlogIt.MauiAdmin.Views.Media;

public partial class MediaListPage : ContentPage
{
    private readonly MediaListViewModel _viewModel;

    public MediaListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<MediaListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
