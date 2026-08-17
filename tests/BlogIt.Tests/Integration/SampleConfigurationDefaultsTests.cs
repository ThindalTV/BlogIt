using BlogIt.Services;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// What the sample actually does when run as committed, rather than what its code reads like.
/// </summary>
public sealed class SampleConfigurationDefaultsTests
{
    [Fact]
    public void ARealRunWithNoConfiguredDatabaseFailsInsteadOfPickingADefault()
    {
        // Exercises the committed appsettings.json through the sample's own configuration pipeline.
        // It used to ship a LocalDB connection string, so this run started up and quietly used a
        // database nobody chose; the only thing that ever rejected it was a guard gated on Aspire.
        // The sample now ships no connection string at all, so there is nothing to fall back to.
        using var factory = new ProductionSampleFactory();

        var start = () => factory.Services;

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*connection string 'BlogItDb'*");
    }

    [Fact]
    public void TheSampleShipsNoAnalyticsImplementationOfItsOwn()
    {
        // The sample is what an integrator copies from, and it used to substitute a stub returning
        // hardcoded session and user counts for IAnalyticsService on every non-test run. Analytics
        // now reports its own not-configured state with real logging, so the stub bought nothing and
        // cost a plausible-looking set of invented numbers in the dashboard.
        typeof(Program).Assembly
            .GetTypes()
            .Where(type => typeof(IAnalyticsService).IsAssignableFrom(type))
            .Should().BeEmpty();
    }

    private sealed class ProductionSampleFactory : BlogItSampleFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }
}
