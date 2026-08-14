using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Account;

namespace BlogIt.MauiAdmin.Views.Account;

public partial class AccountPage : ContentPage
{
    private readonly AccountViewModel _viewModel;

    public AccountPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<AccountViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
