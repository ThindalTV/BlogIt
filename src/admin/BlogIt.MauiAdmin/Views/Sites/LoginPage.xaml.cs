using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Sites;

namespace BlogIt.MauiAdmin.Views.Sites;

public partial class LoginPage : ContentPage
{
    public LoginPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<LoginViewModel>();
    }
}
