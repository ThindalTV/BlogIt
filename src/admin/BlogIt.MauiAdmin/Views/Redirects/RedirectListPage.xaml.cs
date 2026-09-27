using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Redirects;

namespace BlogIt.MauiAdmin.Views.Redirects;

public partial class RedirectListPage : ContentPage
{
    private readonly RedirectListViewModel _viewModel;

    public RedirectListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<RedirectListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
