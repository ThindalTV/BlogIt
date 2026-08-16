// TEMPORARY manual-testing scaffolding — not a shipped feature. Swapped in for IAnalyticsService
// so the admin Dashboard's Analytics panel can be exercised end-to-end with deterministic,
// known data instead of a real Google Analytics connection. Delete this file and its
// registration in Program.cs once manual browser testing is complete.
//
// Doubles as the worked example of the host-substitution path: IAnalyticsService is a public
// abstraction in the core BlogIt package, so a host can implement it without installing
// BlogIt.GoogleAnalytics or referencing the Google SDK at all. This project does not.
using BlogIt.Shared.DTOs;

namespace BlogIt.Sample;

public class ManualTestAnalyticsService : BlogIt.Services.IAnalyticsService
{
    public Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate)
    {
        var summary = new AnalyticsSummaryDto(
            Sessions: 1234,
            Users: 987,
            PageViews: 4321,
            TopPages:
            [
                new TopPageDto("/2026/my-first-test-post", 512),
                new TopPageDto("/archive", 210),
                new TopPageDto("/", 178),
            ]);
        return Task.FromResult<AnalyticsSummaryDto?>(summary);
    }
}
