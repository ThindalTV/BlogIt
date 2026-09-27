using BlogIt.Contracts.DTOs;

namespace BlogIt.Services;

public interface IAiService
{
    Task<AiConversationDetailDto> SendMessageAsync(
        Guid conversationId,
        string userContent,
        CancellationToken cancellationToken = default);

    Task<BlogIt.Entities.BlogPost> ExportToDraftAsync(
        Guid conversationId,
        Guid authorId,
        string? additionalInstructions,
        CancellationToken cancellationToken = default);
}
