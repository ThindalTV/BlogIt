using BlogIt.Sample;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The sample's 404 middleware has to buffer the response body to be able to rewrite it, but only
/// for the responses it might actually rewrite. These pin that scope, because the middleware sits in
/// front of the whole pipeline and buffering a media download would read the entire file into memory
/// and defeat the range streaming <c>MediaProxyApi</c> enables.
/// </summary>
public sealed class NotFoundResponseBufferingTests
{
    [Fact]
    public void RazorComponentPageResponsesAreBuffered()
    {
        // Only a rendered page component can set the not-found flag, so this is the one case where
        // the status code still has to be changeable after next() returns.
        ShouldBuffer(new ComponentTypeMetadata(typeof(object))).Should().BeTrue();
    }

    [Fact]
    public void MediaAndApiEndpointResponsesAreNotBuffered()
    {
        // A minimal-API endpoint — MediaProxyApi's streaming GET is one — carries no component
        // metadata and can never raise the flag, so its body must go straight to the transport.
        ShouldBuffer().Should().BeFalse();
    }

    [Fact]
    public void UnroutedResponsesAreNotBuffered()
    {
        // Static files and genuinely unmatched requests. The framework's own 404 is correct for
        // these; there is no page render to wait for.
        var context = new DefaultHttpContext();

        NotFoundResponseMiddleware.ShouldBufferResponse(context).Should().BeFalse();
    }

    private static bool ShouldBuffer(params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test-endpoint"));
        return NotFoundResponseMiddleware.ShouldBufferResponse(context);
    }
}
