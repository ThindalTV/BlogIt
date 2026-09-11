namespace BlogIt.Shared.DTOs;

public record AiConversationSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    Guid? LinkedDraftId
);

public record AiConversationDetailDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? LinkedDraftId,
    IReadOnlyList<AiMessageDto> Messages
);

public record AiMessageDto(Guid Id, string Role, string Content, DateTimeOffset CreatedAt);

public record CreateAiConversationRequest(string Title);

/// <summary>
/// Retitles an existing conversation. Separate from the create request so the endpoint cannot be
/// mistaken for one that also replaces the messages.
/// </summary>
public record RenameAiConversationRequest(string Title);

public record SendAiMessageRequest(string Content);

public record ExportAiConversationRequest(string? AdditionalInstructions);

public record ExportAiConversationResponse(Guid PostId, string Slug);

/// <summary>Describes the resolved AI provider config shown in the admin settings UI.</summary>
public record AiProviderInfoDto(
    string Provider,
    string? BaseUrl,
    string? Model,
    string? ExportModel
);
