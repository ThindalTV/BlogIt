using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Ai;

public partial class ConversationListViewModel(MauiApiClient apiClient, IDialogService dialogService) : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool aiAvailable = true;

    public ObservableCollection<AiConversationSummaryDto> Conversations { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // AI can throw an unstructured 500 if no provider is configured for this
            // site — check availability first rather than surfacing a raw error.
            var providerInfo = await apiClient.GetAiProviderInfoAsync();
            AiAvailable = providerInfo.Success && !string.IsNullOrWhiteSpace(providerInfo.Value?.Provider);
            if (!AiAvailable) return;

            var result = await apiClient.GetConversationsAsync();
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Conversations.Clear();
            foreach (var c in result.Value!.OrderByDescending(c => c.UpdatedAt))
                Conversations.Add(c);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewConversationAsync()
    {
        var result = await apiClient.CreateConversationAsync("New Conversation");
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't start conversation", result.Error!.Message);
            return;
        }

        await Shell.Current.GoToAsync($"ai/chat?id={result.Value!.Id}");
    }

    [RelayCommand]
    private async Task OpenAsync(AiConversationSummaryDto conversation) =>
        await Shell.Current.GoToAsync($"ai/chat?id={conversation.Id}");

    [RelayCommand]
    private async Task DeleteAsync(AiConversationSummaryDto conversation)
    {
        var confirmed = await dialogService.ConfirmAsync("Delete conversation", $"Delete \"{conversation.Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeleteConversationAsync(conversation.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }
}
