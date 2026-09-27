using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Sites;

namespace BlogIt.MauiAdmin.Views.Sites;

public partial class AddSitePage : ContentPage
{
    public AddSitePage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<AddSiteViewModel>();
    }
}
