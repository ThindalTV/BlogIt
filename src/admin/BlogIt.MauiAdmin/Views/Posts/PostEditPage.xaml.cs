using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Posts;
using BlogIt.MauiAdmin.Views.Media;

namespace BlogIt.MauiAdmin.Views.Posts;

public partial class PostEditPage : ContentPage
{
    private readonly PostEditViewModel _viewModel;

    public PostEditPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<PostEditViewModel>();
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
