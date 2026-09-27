namespace BlogIt.MauiAdmin.Services;

/// <summary>Thin wrapper around the current page's DisplayAlert, giving every screen
/// in the app the same confirm pattern for delete/unpublish actions — including
/// Redirects, which lacked any confirmation at all in the reference Blazor admin.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel");
    Task AlertAsync(string title, string message, string cancel = "OK");
}

public class DialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "Cancel")
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false);
    }

    public Task AlertAsync(string title, string message, string cancel = "OK")
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page?.DisplayAlertAsync(title, message, cancel) ?? Task.CompletedTask;
    }
}
