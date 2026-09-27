using BlogIt.Contracts.DTOs;

namespace BlogIt.Services;

public interface IAnalyticsService
{
    Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate);
}
