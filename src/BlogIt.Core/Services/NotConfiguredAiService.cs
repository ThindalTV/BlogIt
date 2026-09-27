using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;

namespace BlogIt.Services;

/// <summary>
/// The <see cref="IAiService"/> the engine registers when no AI provider was configured. Every
/// call throws <see cref="InvalidOperationException"/> carrying installation instructions.
/// </summary>
/// <remarks>
/// <para>
/// The alternative - leaving <see cref="IAiService"/> unregistered - was tried and rejected. The
/// AI endpoints take <see cref="IAiService"/> as a parameter, so an unregistered service makes the
/// DI container throw while activating the endpoint handler: an unhandled 500 with a container
/// stack trace, produced before any of <c>AiApi</c>'s own error handling runs. Failing here
/// instead keeps the failure inside the path <c>AiApi.HandleAiFailure</c> already guards.
/// </para>
/// <para>
/// <see cref="InvalidOperationException"/> specifically, because that is the exception
/// <c>AiApi.HandleAiFailure</c> maps to <c>400 Bad Request</c> with the message surfaced to the
/// caller - the identical response the admin already gets when a provider is installed but its API
/// key has not been entered. So "no package installed" and "package installed, not set up" look
/// the same to the admin UI, which needs no change to display either. A 501 would have been more
/// literally correct but would have needed new handling in the admin's AI screen to render at all.
/// </para>
/// <para>
/// Only the two provider-calling endpoints are affected. Listing, reading, creating and deleting
/// conversations touch nothing but the database and keep working, so drafts brainstormed before an
/// AI package was removed stay readable and deletable.
/// </para>
/// </remarks>
internal sealed class NotConfiguredAiService : IAiService
{
    internal const string NotConfiguredMessage =
        "BlogIt AI is not configured. Install the BlogIt.OpenAi package and call " +
        "options.UseOpenAi() inside AddBlogIt, or register your own IAiService.";

    public Task<AiConversationDetailDto> SendMessageAsync(
        Guid conversationId,
        string userContent,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(NotConfiguredMessage);

    public Task<BlogPost> ExportToDraftAsync(
        Guid conversationId,
        Guid authorId,
        string? additionalInstructions,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(NotConfiguredMessage);
}
