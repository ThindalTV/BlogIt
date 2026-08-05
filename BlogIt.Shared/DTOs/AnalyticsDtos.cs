namespace BlogIt.Shared.DTOs;

public record AnalyticsSummaryDto(
    long Sessions,
    long Users,
    long PageViews,
    IReadOnlyList<TopPageDto> TopPages
);

public record TopPageDto(string Path, long Views);
