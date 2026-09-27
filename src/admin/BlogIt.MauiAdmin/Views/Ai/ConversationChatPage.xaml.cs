using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Ai;

namespace BlogIt.MauiAdmin.Views.Ai;

public partial class ConversationChatPage : ContentPage
{
    public ConversationChatPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<ConversationChatViewModel>();
    }
}
