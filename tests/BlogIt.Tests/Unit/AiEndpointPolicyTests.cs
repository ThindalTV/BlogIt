using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the use-time half of the AI base URL guard: <c>OpenAiService.ResolveEndpoint</c>, the line
/// that decides where the stored API key is actually sent. Validation cannot be the only check,
/// because a value written before the check existed is still in the settings table.
/// </summary>
public class AiEndpointPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEndpoint_ReturnsNullForABlankSetting(string? baseUrl) =>
        // Blank means "use the client's own default endpoint", not "misconfigured".
        OpenAiService.ResolveEndpoint(baseUrl, allowPrivateAiEndpoints: false).Should().BeNull();

    [Fact]
    public void ResolveEndpoint_ReturnsAPublicEndpointUnchanged() =>
        OpenAiService.ResolveEndpoint(" https://api.example.com/v1 ", allowPrivateAiEndpoints: false)
            .Should().Be(new Uri("https://api.example.com/v1"));

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://192.168.1.20/v1")]
    public void ResolveEndpoint_RefusesAPrivateEndpointStoredBeforeTheGuardExisted(string baseUrl)
    {
        var resolve = () => OpenAiService.ResolveEndpoint(baseUrl, allowPrivateAiEndpoints: false);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowPrivateAiEndpoints*");
    }

    [Fact]
    public void ResolveEndpoint_AllowsAPrivateEndpointWhenTheHostOptedIn() =>
        OpenAiService.ResolveEndpoint("http://localhost:11434/v1", allowPrivateAiEndpoints: true)
            .Should().Be(new Uri("http://localhost:11434/v1"));

    [Fact]
    public void ResolveEndpoint_RefusesANonHttpEndpoint()
    {
        // Refused rather than ignored: falling back to the default endpoint would send the key to a
        // different party than the operator configured.
        var resolve = () => OpenAiService.ResolveEndpoint("file:///etc/passwd", allowPrivateAiEndpoints: true);

        resolve.Should().Throw<InvalidOperationException>();
    }
}
