using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Sites;

namespace BlogIt.MauiAdmin.Views.Sites;

public partial class SetupRequiredPage : ContentPage
{
    public SetupRequiredPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<SetupRequiredViewModel>();
    }
}
