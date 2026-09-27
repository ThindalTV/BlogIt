using BlogIt.Shared.DTOs;

namespace BlogIt.Services;

/// <summary>
/// The <see cref="IAnalyticsService"/> the engine registers when no analytics provider was
/// configured. Reports no summary.
/// </summary>
/// <remarks>
/// Returning <see langword="null"/> rather than throwing, because "no analytics data available" is
/// already a first-class outcome of this interface: the Google Analytics provider returns
/// <see langword="null"/> when its property ID or service-account JSON has not been entered, and
/// <c>AnalyticsApi</c> turns that into <c>404 "Analytics is not configured."</c>. So a host with no
/// analytics package installed gets byte-identical behaviour to a host with one installed and not
/// set up, and the admin dashboard's analytics panel - which already handles that 404 - needs no
/// change. This is the deliberate difference from <see cref="NotConfiguredAiService"/>: analytics
/// is a read the caller can do without, whereas an AI send that silently succeeded with no result
/// would be indistinguishable from a working provider returning nothing.
/// </remarks>
internal sealed class NotConfiguredAnalyticsService : IAnalyticsService
{
    public Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate) =>
        Task.FromResult<AnalyticsSummaryDto?>(null);
}
