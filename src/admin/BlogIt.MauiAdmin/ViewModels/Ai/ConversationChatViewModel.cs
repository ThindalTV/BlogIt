using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Ai;

public partial class ConversationChatViewModel(MauiApiClient apiClient, IDialogService dialogService)
    : ObservableObject, IQueryAttributable
{
    private Guid _id;

    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string newMessageText = string.Empty;
    [ObservableProperty] private bool isSending;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private Guid? linkedDraftId;
    [ObservableProperty] private bool showExportPanel;
    [ObservableProperty] private string? exportInstructions;
    [ObservableProperty] private string? exportMessage;

    public ObservableCollection<AiMessageDto> Messages { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string idStr && Guid.TryParse(idStr, out var id))
        {
            _id = id;
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var result = await apiClient.GetConversationAsync(_id);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Title = result.Value!.Title;
            LinkedDraftId = result.Value.LinkedDraftId;
            Messages.Clear();
            foreach (var m in result.Value.Messages)
                Messages.Add(m);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = NewMessageText.Trim();
        if (string.IsNullOrEmpty(text) || IsSending) return;

        NewMessageText = string.Empty;
        IsSending = true;
        ErrorMessage = null;
        try
        {
            // No streaming server-side — this is a single blocking call that
            // returns the whole updated conversation (the new user message and the
            // generated assistant reply together).
            var result = await apiClient.SendMessageAsync(_id, text);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.StatusCode == System.Net.HttpStatusCode.InternalServerError
                    ? "AI is unavailable for this site right now."
                    : result.Error!.Message;
                NewMessageText = text;
                return;
            }

            Messages.Clear();
            foreach (var m in result.Value!.Messages)
                Messages.Add(m);
            LinkedDraftId = result.Value.LinkedDraftId;
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void ToggleExportPanel() => ShowExportPanel = !ShowExportPanel;

    [RelayCommand]
    private async Task ExportDraftAsync()
    {
        IsBusy = true;
        ExportMessage = null;
        try
        {
            var result = await apiClient.ExportDraftAsync(_id, string.IsNullOrWhiteSpace(ExportInstructions) ? null : ExportInstructions);
            if (!result.Success)
            {
                await dialogService.AlertAsync("Export failed", result.Error!.StatusCode == System.Net.HttpStatusCode.InternalServerError
                    ? "AI is unavailable for this site right now."
                    : result.Error!.Message);
                return;
            }

            LinkedDraftId = result.Value!.PostId;
            ShowExportPanel = false;
            ExportMessage = "Exported to a new draft post.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenDraftAsync()
    {
        if (LinkedDraftId is { } id)
            await Shell.Current.GoToAsync($"posts/edit?id={id}");
    }
}
