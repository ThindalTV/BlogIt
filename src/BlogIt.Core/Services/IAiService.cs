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

    /// <summary>
    /// Makes one small real request with the saved settings, so a base URL, key or model name that
    /// is well-formed but wrong is reported when it is entered rather than at the next brainstorm.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The settings do not work; the message names what to fix and is safe to show the operator.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The default for a provider that has no check of its own — so a custom <see cref="IAiService"/>
    /// written before this member existed keeps compiling, and is reported as "not checked" rather
    /// than as working.
    /// </exception>
    Task VerifyConnectionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The configured AI provider does not support a connection check.");
}
