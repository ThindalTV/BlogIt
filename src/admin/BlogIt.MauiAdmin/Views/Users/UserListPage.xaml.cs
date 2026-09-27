using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Users;

namespace BlogIt.MauiAdmin.Views.Users;

public partial class UserListPage : ContentPage
{
    private readonly UserListViewModel _viewModel;

    public UserListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<UserListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
