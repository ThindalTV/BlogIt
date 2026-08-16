using BlogIt.Shared.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// A <see cref="BlogItSampleFactory"/> whose request-scoped <see cref="BlogItDbContext"/> can be
/// told to fail its next save the way a relational provider fails when an insert loses a race to a
/// unique index.
/// </summary>
/// <remarks>
/// Needed because the race cannot be reproduced by racing. EF Core's InMemory provider does not
/// enforce unique indexes at all — only the primary key, and then with a bare
/// <see cref="ArgumentException"/> rather than the <see cref="DbUpdateException"/> a real provider
/// raises, which is the divergence <c>SetupApi</c> documents. Two genuinely concurrent creates
/// against the test database therefore both succeed and prove nothing. Injecting the failure at the
/// exact call the database would have failed is what lets the endpoint's handling of it be tested,
/// and lets both exception shapes be covered.
/// <para>
/// Deliberately one-shot: seeding a user and issuing the request both save, so a context that failed
/// every time could not be set up at all.
/// </para>
/// </remarks>
public class SaveFailureFactory : BlogItSampleFactory
{
    /// <summary>The exception the next save will throw, consumed by that save.</summary>
    public SaveFailureSwitch Failures { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Failures);
            // Overrides the scoped registration AddDbContextFactory made, which is what the API
            // endpoints resolve. The factory itself is left alone: background services and the
            // startup migration use it, and neither should be able to trip the switch.
            services.AddScoped(sp => new FailableDbContext(
                sp.GetRequiredService<DbContextOptions<BlogItDbContext>>(),
                sp.GetRequiredService<SaveFailureSwitch>()));
            services.AddScoped<BlogItDbContext>(sp => sp.GetRequiredService<FailableDbContext>());
        });
    }
}

/// <summary>Shared switch between a test and the context its requests resolve.</summary>
public sealed class SaveFailureSwitch
{
    /// <summary>Set to make the next <c>SaveChangesAsync</c> throw this instead of saving.</summary>
    public Exception? NextFailure { get; set; }

    /// <summary>
    /// The exception a duplicate insert produces on SQL Server and Azure SQL. The message mirrors
    /// the shape EF wraps, though nothing under test reads it.
    /// </summary>
    public static Exception DuplicateKeyOnRelationalProvider() =>
        new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("Violation of UNIQUE KEY constraint."));

    /// <summary>The exception EF Core's InMemory provider produces for the same case.</summary>
    public static Exception DuplicateKeyOnInMemoryProvider() =>
        new ArgumentException("An item with the same key has already been added.");
}

internal sealed class FailableDbContext(
    DbContextOptions<BlogItDbContext> options,
    SaveFailureSwitch failures) : BlogItDbContext(options)
{
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (failures.NextFailure is { } failure)
        {
            failures.NextFailure = null;
            return Task.FromException<int>(failure);
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
