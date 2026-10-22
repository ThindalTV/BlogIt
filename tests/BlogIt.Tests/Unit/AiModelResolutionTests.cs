using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers <c>OpenAiService.ResolveModels</c>: an export model left blank means "the model the
/// conversation was brainstormed with", not a second default of its own.
/// </summary>
public class AiModelResolutionTests
{
    [Fact]
    public void ResolveModels_NoExportModel_ReusesTheConfiguredChatModel() =>
        OpenAiService.ResolveModels("llama3", null, "default-chat")
            .Should().Be(("llama3", "llama3"));

    [Fact]
    public void ResolveModels_NothingConfigured_ExportUsesTheDefaultChatModel() =>
        OpenAiService.ResolveModels(null, null, "default-chat")
            .Should().Be(("default-chat", "default-chat"));

    [Fact]
    public void ResolveModels_ExplicitExportModel_Wins() =>
        OpenAiService.ResolveModels("small", "large", "default-chat")
            .Should().Be(("small", "large"));
}
