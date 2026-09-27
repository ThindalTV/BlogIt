using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Dashboard;

namespace BlogIt.MauiAdmin.Views.Dashboard;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<DashboardViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
