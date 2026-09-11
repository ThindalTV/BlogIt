using BlogIt.Services;
using BlogIt.Shared.DTOs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Pins the AI and analytics provider split: what the engine registers when a satellite package is
/// installed, what it registers when none is, and that "none" degrades the way the READMEs promise
/// rather than by failing DI activation.
/// </summary>
/// <remarks>
/// Assertions are made against the <see cref="ServiceDescriptor"/> rather than by resolving the
/// service, because <c>OpenAiService</c> takes a <c>BlogItDbContext</c> that the fake database
/// provider below deliberately does not register — the question here is which implementation the
/// engine chose, not whether it can be constructed.
/// </remarks>
public sealed class AiAnalyticsProviderTests
{
    [Fact]
    public void AddBlogIt_WithNoProviders_FallsBackToTheNotConfiguredServices()
    {
        var services = CreateServices(_ => { });

        ImplementationTypeOf<IAiService>(services).Should().Be<NotConfiguredAiService>();
        ImplementationTypeOf<IAnalyticsService>(services).Should().Be<NotConfiguredAnalyticsService>();
    }

    [Fact]
    public void UseOpenAi_RegistersExactlyOneAiServiceFromTheSatellitePackage()
    {
        var services = CreateServices(options => options.UseOpenAi());

        services.Count(descriptor => descriptor.ServiceType == typeof(IAiService))
            .Should().Be(1);
        ImplementationTypeOf<IAiService>(services).Should().Be<OpenAiService>();
        // Installing the AI satellite must not quietly bring analytics with it.
        ImplementationTypeOf<IAnalyticsService>(services).Should().Be<NotConfiguredAnalyticsService>();
    }

    [Fact]
    public void UseGoogleAnalytics_RegistersExactlyOneAnalyticsServiceFromTheSatellitePackage()
    {
        var services = CreateServices(options => options.UseGoogleAnalytics());

        services.Count(descriptor => descriptor.ServiceType == typeof(IAnalyticsService))
            .Should().Be(1);
        ImplementationTypeOf<IAnalyticsService>(services).Should().Be<GoogleAnalyticsService>();
        ImplementationTypeOf<IAiService>(services).Should().Be<NotConfiguredAiService>();
    }

    [Fact]
    public void UseOpenAi_TwiceIsRejected()
    {
        var configure = () => CreateServices(options =>
        {
            options.UseOpenAi();
            options.UseOpenAi();
        });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*at most one AI provider*OpenAi*");
    }

    [Fact]
    public void UseGoogleAnalytics_TwiceIsRejected()
    {
        var configure = () => CreateServices(options =>
        {
            options.UseGoogleAnalytics();
            options.UseGoogleAnalytics();
        });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*at most one analytics provider*GoogleAnalytics*");
    }

    [Fact]
    public void HostRegisteredServices_WinOverBothTheSatelliteAndTheFallback()
    {
        // Both provider registrations and the engine's fallbacks use TryAdd, so a host that has
        // already supplied its own implementation keeps it. This is the path AiApiTests takes for AI.
        // The sample used to demonstrate it for analytics too, with a hardcoded-numbers stub; that was
        // removed (finding #36) because a sample is copied wholesale, so the substitution point is now
        // only documented there, not exercised.
        var services = new ServiceCollection();
        services.AddScoped<IAiService, HostAiService>();
        services.AddScoped<IAnalyticsService, HostAnalyticsService>();
        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseFileSystemStorage(storage => storage.RootPath = Path.GetTempPath());
            options.UseOpenAi();
            options.UseGoogleAnalytics();
        });

        ImplementationTypeOf<IAiService>(services).Should().Be<HostAiService>();
        ImplementationTypeOf<IAnalyticsService>(services).Should().Be<HostAnalyticsService>();
    }

    [Fact]
    public async Task NotConfiguredAiService_ThrowsAnInvalidOperationNamingThePackageToInstall()
    {
        var service = new NotConfiguredAiService();

        var send = async () => await service.SendMessageAsync(Guid.NewGuid(), "hello");
        var export = async () => await service.ExportToDraftAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        // InvalidOperationException specifically: that is what AiApi.HandleAiFailure maps to a 400
        // whose body carries this message, so the operator is told which package to install.
        (await send.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*BlogIt.OpenAi*UseOpenAi*");
        (await export.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*BlogIt.OpenAi*UseOpenAi*");
    }

    [Fact]
    public async Task NotConfiguredAnalyticsService_ReportsNoSummaryRatherThanThrowing()
    {
        var service = new NotConfiguredAnalyticsService();

        var summary = await service.GetSummaryAsync("30daysAgo", "today");

        // Null is already this interface's "nothing to show" answer, which AnalyticsApi turns into
        // 404 "Analytics is not configured." — identical to a configured provider with no
        // credentials entered, so the dashboard panel needs no change.
        summary.Should().BeNull();
    }

    private static IServiceCollection CreateServices(Action<BlogItOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseFileSystemStorage(storage => storage.RootPath = Path.GetTempPath());
            configure(options);
        });
        return services;
    }

    private static Type? ImplementationTypeOf<TService>(IServiceCollection services) =>
        services.Last(descriptor => descriptor.ServiceType == typeof(TService)).ImplementationType;

    private sealed class FakeDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "Fake";

        public void RegisterServices(IServiceCollection services)
        {
        }
    }

    private sealed class HostAiService : IAiService
    {
        public Task<AiConversationDetailDto> SendMessageAsync(
            Guid conversationId,
            string userContent,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Shared.Entities.BlogPost> ExportToDraftAsync(
            Guid conversationId,
            Guid authorId,
            string? additionalInstructions,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class HostAnalyticsService : IAnalyticsService
    {
        public Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate) =>
            Task.FromResult<AnalyticsSummaryDto?>(null);
    }
}
