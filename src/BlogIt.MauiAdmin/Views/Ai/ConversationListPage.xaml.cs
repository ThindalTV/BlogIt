using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Ai;

namespace BlogIt.MauiAdmin.Views.Ai;

public partial class ConversationListPage : ContentPage
{
    private readonly ConversationListViewModel _viewModel;

    public ConversationListPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<ConversationListViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }
}
