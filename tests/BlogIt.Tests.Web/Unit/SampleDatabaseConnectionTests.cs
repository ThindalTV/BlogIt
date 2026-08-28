using BlogIt.Sample;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The sample's connection-string guard. Extracted from <c>Program.cs</c> so the combinations can be
/// covered at all: the checks run before the host is built, and the sample's configuration sources are
/// cleared and rebuilt during startup, so a test cannot inject a connection string into a real run
/// without setting a process environment variable — which is itself one of the Aspire signals.
/// </summary>
public sealed class SampleDatabaseConnectionTests
{
    private const string LocalDb =
        @"Server=(localdb)\mssqllocaldb;Database=BlogItSample;Trusted_Connection=True;TrustServerCertificate=True";

    private const string Provisioned =
        "Server=tcp:sql,1433;Database=blogit;User ID=sa;Password=Str0ng!;TrustServerCertificate=True";

    [Fact]
    public void MissingConnectionStringIsRejectedWithSetupInstructions()
    {
        var require = () => SampleDatabaseConnection.Require(null, isAspireRun: false, isDevelopment: true);

        require.Should().Throw<InvalidOperationException>()
            .WithMessage("*BlogItDb*AppHost*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LocalDbIsRejectedOutsideDevelopmentWhetherOrNotAspireInjectedIt(bool isAspireRun)
    {
        // The gap this closes: the guard used to require an Aspire signal, so a plain `dotnet run`
        // in Production accepted the committed LocalDB default in silence.
        var require = () => SampleDatabaseConnection.Require(LocalDb, isAspireRun, isDevelopment: false);

        require.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid BlogItDb connection*");
    }

    [Fact]
    public void LocalDbIsRejectedOnAnAspireRunEvenInDevelopment()
    {
        // Aspire provisions its own SQL container and injects those credentials; reaching LocalDB
        // instead means the injection did not happen and the run is not using the database it thinks.
        var require = () => SampleDatabaseConnection.Require(LocalDb, isAspireRun: true, isDevelopment: true);

        require.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid BlogItDb connection*");
    }

    [Fact]
    public void LocalDbIsAllowedForAPlainDevelopmentRun()
    {
        // Deliberately still permitted: a developer pointing the sample at their own LocalDB is a
        // legitimate way to run it, and Windows-integrated credentials are only a problem once the
        // thing is deployed.
        SampleDatabaseConnection
            .Require(LocalDb, isAspireRun: false, isDevelopment: true)
            .Should().Be(LocalDb);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ProvisionedCredentialsAreAlwaysAccepted(bool isAspireRun, bool isDevelopment)
    {
        SampleDatabaseConnection
            .Require(Provisioned, isAspireRun, isDevelopment)
            .Should().Be(Provisioned);
    }

    [Fact]
    public void IntegratedSecurityIsRejectedOutsideDevelopmentToo()
    {
        var require = () => SampleDatabaseConnection.Require(
            "Server=tcp:sql,1433;Database=blogit;Integrated Security=True",
            isAspireRun: false,
            isDevelopment: false);

        require.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid BlogItDb connection*");
    }
}
