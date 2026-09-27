using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Pages;
using BlogIt.MauiAdmin.Views.Media;

namespace BlogIt.MauiAdmin.Views.Pages;

public partial class PageEditPage : ContentPage
{
    private readonly PageEditViewModel _viewModel;

    public PageEditPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<PageEditViewModel>();
        BindingContext = _viewModel;
    }

    private async void OnInsertMediaClicked(object? sender, EventArgs e)
    {
        var cursorPosition = ContentEditor.CursorPosition;
        var modal = new MediaPickerModalPage();
        await Navigation.PushModalAsync(modal);

        var selected = await modal.WaitForSelectionAsync();
        if (selected is not null)
            _viewModel.InsertMediaMarkdown(selected, cursorPosition);
    }
}
