using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using Google.Analytics.Data.V1Beta;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace BlogIt;

/// <summary>
/// The <see cref="IAnalyticsService"/> implementation backed by the Google Analytics Data API.
/// </summary>
/// <remarks>
/// Internal, like <c>AzureBlobMediaStorage</c> in <c>BlogIt.AzureStorage</c>: hosts resolve
/// <see cref="IAnalyticsService"/> from DI and never name this type.
/// </remarks>
internal sealed class GoogleAnalyticsService(
    ISettingsService settings,
    ILogger<GoogleAnalyticsService> logger) : IAnalyticsService
{
    private const string ReadonlyScope = "https://www.googleapis.com/auth/analytics.readonly";

    /// <summary>
    /// Reads the traffic summary for the given GA4 date range, or returns <see langword="null"/>
    /// when the property ID or service-account JSON has not been configured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three outcomes, deliberately kept apart, because the operator's next move differs for each
    /// and the admin's analytics panel used to render all three as the same empty box:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Nothing configured yet - <see langword="null"/>, which <c>AnalyticsApi</c> turns into
    /// <c>404 "Analytics is not configured."</c>, the same answer the engine's not-configured
    /// fallback gives when this package is not installed at all.
    /// </item>
    /// <item>
    /// Settings filled in but unusable - <see cref="InvalidOperationException"/> carrying text
    /// written to be shown to an admin, which <c>AnalyticsApi</c> echoes as a 400. Returning
    /// <see langword="null"/> here, as this used to, told an operator with a truncated or
    /// wrong-kind service-account JSON that they simply had not set analytics up yet.
    /// </item>
    /// <item>
    /// The report call itself failing - logged here with the property ID and rethrown, which
    /// <c>AnalyticsApi</c> turns into a 502 without echoing the provider's own message.
    /// </item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The configured service-account JSON cannot be read as a scoped credential.
    /// </exception>
    public async Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate)
    {
        var credentialsJson = await settings.GetAsync(SettingKeys.GoogleAnalyticsCredentialsJson);
        var propertyId = await settings.GetAsync(SettingKeys.GoogleAnalyticsPropertyId);

        if (string.IsNullOrEmpty(credentialsJson) || string.IsNullOrEmpty(propertyId))
        {
            // Debug, not Warning: an unconfigured site is a normal state, and the dashboard polls
            // this on every visit - at Warning it would be the noisiest line in the log.
            logger.LogDebug(
                "Google Analytics is not configured: {Missing} not set in site settings.",
                MissingSettings(credentialsJson, propertyId));
            return null;
        }

        GoogleCredential credential;
        try
        {
            credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(credentialsJson)
                .ToGoogleCredential()
                .CreateScoped(ReadonlyScope);
        }
        catch (Exception ex)
        {
            // The exception, never the JSON: that value is a private key.
            logger.LogError(ex, "The configured Google Analytics service-account JSON could not be read.");
            throw new InvalidOperationException(
                "The Google Analytics service-account JSON could not be read. Re-paste the full " +
                "JSON key file for the service account in Settings.",
                ex);
        }

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

        RunReportResponse summary;
        RunReportResponse topPages;
        try
        {
            // Client construction is inside the guard with the calls it makes: BuildAsync reaches
            // the token endpoint for a service account, so it fails for the same transport and
            // permission reasons the reports do, and belongs in the same 502.
            var client = await new BetaAnalyticsDataClientBuilder { Credential = credential }
                .BuildAsync();

            var summaryTask = client.RunReportAsync(request);
            var topPagesTask = client.RunReportAsync(topPagesRequest);

            await Task.WhenAll(summaryTask, topPagesTask);

            summary = await summaryTask;
            topPages = await topPagesTask;
        }
        catch (Exception ex)
        {
            // Rethrown rather than swallowed into null: a permission error on the property, a
            // revoked key or an unreachable API is not "analytics is not configured", and the
            // operator has to be able to tell those apart. AnalyticsApi maps this to a 502 with a
            // generic message - the log line here is the copy carrying the property ID and the
            // provider's own words, which are not safe to echo to the browser.
            logger.LogError(
                ex,
                "The Google Analytics report request for property {PropertyId} over {StartDate}..{EndDate} failed.",
                propertyId,
                startDate,
                endDate);
            throw;
        }

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

    /// <summary>
    /// Names which of the two required settings are blank, so the log line says what to fill in
    /// rather than only that something is missing.
    /// </summary>
    private static string MissingSettings(string? credentialsJson, string? propertyId) =>
        string.Join(
            " and ",
            new[]
            {
                string.IsNullOrEmpty(credentialsJson) ? "the service-account JSON" : null,
                string.IsNullOrEmpty(propertyId) ? "the GA4 property ID" : null
            }.Where(name => name is not null));
}
