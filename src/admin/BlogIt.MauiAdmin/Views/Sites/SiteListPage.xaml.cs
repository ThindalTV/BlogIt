using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Sites;

namespace BlogIt.MauiAdmin.Views.Sites;

public partial class SiteListPage : ContentPage
{
    private readonly SiteListViewModel _viewModel;

    public SiteListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<SiteListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
