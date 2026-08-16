using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;

namespace BlogIt;

/// <summary>
/// The <see cref="IAnalyticsService"/> implementation backed by the Google Analytics Data API.
/// </summary>
/// <remarks>
/// Internal, like <c>AzureBlobMediaStorage</c> in <c>BlogIt.AzureStorage</c>: hosts resolve
/// <see cref="IAnalyticsService"/> from DI and never name this type.
/// </remarks>
internal sealed class GoogleAnalyticsService(ISettingsService settings) : IAnalyticsService
{
    /// <summary>
    /// Reads the traffic summary for the given GA4 date range, or returns <see langword="null"/>
    /// when the property ID or service-account JSON has not been configured or cannot be parsed.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> rather than an exception because <c>AnalyticsApi</c> turns it into
    /// <c>404 "Analytics is not configured."</c>, which is exactly the right answer for a site that
    /// simply has not filled the settings in - and is the same answer the engine's
    /// not-configured fallback gives when this package is not installed at all.
    /// </remarks>
    public async Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate)
    {
        var credentialsJson = await settings.GetAsync(SettingKeys.GoogleAnalyticsCredentialsJson);
        var propertyId = await settings.GetAsync(SettingKeys.GoogleAnalyticsPropertyId);

        if (string.IsNullOrEmpty(credentialsJson) || string.IsNullOrEmpty(propertyId))
            return null;

        GoogleCredential credential;
        try
        {
            credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(credentialsJson)
                .ToGoogleCredential()
                .CreateScoped("https://www.googleapis.com/auth/analytics.readonly");
        }
        catch
        {
            return null;
        }

        var clientBuilder = new BetaAnalyticsDataClientBuilder
        {
            Credential = credential
        };
        var client = await clientBuilder.BuildAsync();

        var request = new RunReportRequest
        {
            Property = $"properties/{propertyId}",
            DateRanges = { new DateRange { StartDate = startDate, EndDate = endDate } },
            Metrics =
            {
                new Metric { Name = "sessions" },
                new Metric { Name = "totalUsers" },
                new Metric { Name = "screenPageViews" }
            }
        };

        var topPagesRequest = new RunReportRequest
        {
            Property = $"properties/{propertyId}",
            DateRanges = { new DateRange { StartDate = startDate, EndDate = endDate } },
            Dimensions = { new Dimension { Name = "pagePath" } },
            Metrics = { new Metric { Name = "screenPageViews" } },
            Limit = 10,
            OrderBys =
            {
                new OrderBy
                {
                    Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" },
                    Desc = true
                }
            }
        };

        var summaryTask = client.RunReportAsync(request);
        var topPagesTask = client.RunReportAsync(topPagesRequest);

        await Task.WhenAll(summaryTask, topPagesTask);

        var summary = await summaryTask;
        var topPages = await topPagesTask;

        long GetMetric(RunReportResponse r, int index)
            => r.Rows.Count > 0 && long.TryParse(r.Rows[0].MetricValues[index].Value, out var v) ? v : 0;

        var topPageList = topPages.Rows
            .Select(row => new TopPageDto(
                row.DimensionValues[0].Value,
                long.TryParse(row.MetricValues[0].Value, out var v) ? v : 0))
            .ToList();

        return new AnalyticsSummaryDto(
            GetMetric(summary, 0),
            GetMetric(summary, 1),
            GetMetric(summary, 2),
            topPageList);
    }
}
