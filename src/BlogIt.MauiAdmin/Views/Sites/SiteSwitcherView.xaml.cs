using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Sites;

namespace BlogIt.MauiAdmin.Views.Sites;

public partial class SiteSwitcherView : ContentView
{
    private readonly SiteSwitcherViewModel _viewModel;

    public SiteSwitcherView()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<SiteSwitcherViewModel>();
        BindingContext = _viewModel;
    }

    private void OnSiteSelected(object? sender, EventArgs e)
    {
        if (SitePicker.SelectedItem is SiteProfile site)
            _viewModel.SwitchCommand.Execute(site);
    }
}
