using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Media;
using BlogIt.Shared.DTOs;

namespace BlogIt.MauiAdmin.Views.Media;

/// <summary>Modal picker pushed via Navigation.PushModalAsync (not Shell routing,
/// since this page's result needs to flow back to the caller — Post/Page editors
/// await <see cref="WaitForSelectionAsync"/> after pushing it).</summary>
public partial class MediaPickerModalPage : ContentPage
{
    private readonly MediaPickerViewModel _viewModel;
    private readonly TaskCompletionSource<MediaFileDto?> _tcs = new();
    private bool _resultSet;

    public MediaPickerModalPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<MediaPickerViewModel>();
        BindingContext = _viewModel;
        _viewModel.ItemSelected += OnItemSelected;
    }

    public Task<MediaFileDto?> WaitForSelectionAsync() => _tcs.Task;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.LoadAsync();
    }

    private async void OnItemSelected(MediaItemRow row)
    {
        _resultSet = true;
        _tcs.TrySetResult(row.Dto);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _resultSet = true;
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_resultSet)
            _tcs.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
}
