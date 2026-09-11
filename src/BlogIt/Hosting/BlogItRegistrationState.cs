using Microsoft.AspNetCore.Routing;

namespace BlogIt;

/// <summary>
/// Marks a route as one BlogIt mapped, so startup validation can tell BlogIt's endpoints apart from
/// the host's when looking for collisions.
/// </summary>
/// <param name="DisableHint">
/// The option a host can set to stop BlogIt claiming this route, when there is one — named in the
/// error message so the fix arrives with the problem. Null for routes that cannot be turned off.
/// </param>
internal sealed record BlogItEndpointMetadata(string? DisableHint);

/// <summary>
/// What the host actually called during startup, recorded so it can be checked once the pipeline is
/// built.
/// </summary>
/// <remarks>
/// A singleton written by <c>UseBlogIt</c> and <c>MapBlogIt</c> and read by
/// <see cref="BlogItStartupValidator"/>. It exists because BlogIt's setup is spread across three
/// calls the host makes in the right order, and until now nothing verified that they happened at
/// all — the rules lived only in prose and in a comment in the sample.
/// </remarks>
internal sealed class BlogItRegistrationState
{
    public bool UseCalled { get; set; }

    public bool MapCalled { get; set; }

    /// <summary>
    /// Whether the host called <c>UseAntiforgery</c> before <c>UseBlogIt</c>.
    /// </summary>
    public bool AntiforgeryBeforeUseBlogIt { get; set; }

    /// <summary>
    /// The live endpoint sources captured at <c>MapBlogIt</c>.
    /// </summary>
    /// <remarks>
    /// The collection itself is kept rather than a snapshot of its contents, so endpoints the host
    /// maps <em>after</em> <c>MapBlogIt</c> are still seen when validation runs.
    /// </remarks>
    public ICollection<EndpointDataSource>? DataSources { get; set; }
}
