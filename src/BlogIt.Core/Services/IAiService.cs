using BlogIt.Shared.DTOs;

namespace BlogIt.Services;

public interface IAiService
{
    Task<AiConversationDetailDto> SendMessageAsync(
        Guid conversationId,
        string userContent,
        CancellationToken cancellationToken = default);

    Task<BlogIt.Shared.Entities.BlogPost> ExportToDraftAsync(
        Guid conversationId,
        Guid authorId,
        string? additionalInstructions,
        CancellationToken cancellationToken = default);
}
