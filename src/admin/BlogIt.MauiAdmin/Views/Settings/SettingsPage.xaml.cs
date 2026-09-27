using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Settings;

namespace BlogIt.MauiAdmin.Views.Settings;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<SettingsViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
