using BlogIt.Shared.DTOs;

namespace BlogIt.Web.Services;

public interface IAnalyticsService
{
    Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate);
}
