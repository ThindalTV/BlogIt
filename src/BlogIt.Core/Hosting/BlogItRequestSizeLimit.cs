using Microsoft.AspNetCore.Http.Metadata;

namespace BlogIt;

/// <summary>
/// Sets the maximum request body size for a single endpoint.
/// </summary>
/// <remarks>
/// Routing applies this to <c>IHttpMaxRequestBodySizeFeature</c> once the endpoint has been matched
/// and before the body is read, which is what lets BlogIt's media upload carry its own ceiling
/// instead of inheriting whichever default the host's server was configured with.
/// </remarks>
/// <param name="MaxRequestBodySize">The limit in bytes.</param>
internal sealed record BlogItRequestSizeLimit(long? MaxRequestBodySize) : IRequestSizeLimitMetadata;
