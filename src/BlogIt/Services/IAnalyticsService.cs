using BlogIt.Shared.DTOs;

namespace BlogIt.Services;

public interface IAnalyticsService
{
    Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate);
}
