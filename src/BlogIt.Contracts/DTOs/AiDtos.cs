namespace BlogIt.Shared.DTOs;

public record AiConversationSummaryDto(
    Guid Id,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount,
    Guid? LinkedDraftId
);

public record AiConversationDetailDto(
    Guid Id,
    string Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? LinkedDraftId,
    IReadOnlyList<AiMessageDto> Messages
);

public record AiMessageDto(Guid Id, string Role, string Content, DateTime CreatedAt);

public record CreateAiConversationRequest(string Title);

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
