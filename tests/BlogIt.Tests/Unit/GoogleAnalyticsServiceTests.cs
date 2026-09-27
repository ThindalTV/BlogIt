using BlogIt.Services;
using BlogIt.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Pins that the Google Analytics provider keeps "not configured" and "configured but unusable"
/// apart, and that both leave a trace in the log rather than only a blank dashboard panel.
/// </summary>
/// <remarks>
/// The successful path and the report-call failure need the Google Data API transport, which has no
/// offline seam here, so the endpoint's half of the story - including how a failed report call is
/// reported - is covered by <c>AnalyticsApiTests</c> instead.
/// </remarks>
public sealed class GoogleAnalyticsServiceTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "123456")]
    [InlineData("{}", null)]
    public async Task GetSummary_ReportsNothingAndSaysSoInTheLogWhenNotConfigured(
        string? credentialsJson,
        string? propertyId)
    {
        var logger = new CapturingLogger<GoogleAnalyticsService>();
        var service = new GoogleAnalyticsService(
            Settings(credentialsJson, propertyId),
            logger);

        var summary = await service.GetSummaryAsync("30daysAgo", "today");

        summary.Should().BeNull();
        var record = logger.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Debug);
        record.Message.Should().Contain("not configured");
        record.Exception.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_ThrowsAndLogsWhenTheCredentialsCannotBeRead()
    {
        var logger = new CapturingLogger<GoogleAnalyticsService>();
        var service = new GoogleAnalyticsService(
            Settings("{ not valid json", "123456"),
            logger);

        var read = async () => await service.GetSummaryAsync("30daysAgo", "today");

        // InvalidOperationException, not null: null is the "not configured" answer the endpoint
        // turns into a 404, and a site whose pasted service-account JSON is broken must not be
        // told it simply has not set analytics up yet.
        (await read.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*service-account JSON*");
        var record = logger.Records.Should().ContainSingle().Subject;
        record.Level.Should().Be(LogLevel.Error);
        record.Exception.Should().NotBeNull();
    }

    private static ISettingsService Settings(string? credentialsJson, string? propertyId)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.GetAsync(SettingKeys.GoogleAnalyticsCredentialsJson))
            .ReturnsAsync(credentialsJson);
        settings.Setup(service => service.GetAsync(SettingKeys.GoogleAnalyticsPropertyId))
            .ReturnsAsync(propertyId);
        return settings.Object;
    }

    private sealed record LogRecord(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogRecord> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Records.Add(new LogRecord(logLevel, formatter(state, exception), exception));
    }
}
