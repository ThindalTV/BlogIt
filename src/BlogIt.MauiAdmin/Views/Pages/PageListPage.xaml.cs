using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Pages;

namespace BlogIt.MauiAdmin.Views.Pages;

public partial class PageListPage : ContentPage
{
    private readonly PageListViewModel _viewModel;

    public PageListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<PageListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
