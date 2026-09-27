using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Guards the relational shape of the model, which the entity classes alone do not describe. The
/// audit found three separate defects here that no functional test could see: a queried column
/// mapped to <c>nvarchar(max)</c> and therefore unindexable, a unique index declared over a key
/// wider than SQL Server permits, and no index at all behind the query every public page runs.
/// These assertions are stated as rules rather than as a list of expected indexes, so a future
/// column added without a length is caught by the same test.
/// </summary>
public class SchemaMappingTests
{
    /// <summary>SQL Server's maximum key size for a nonclustered index.</summary>
    private const int MaxNonclusteredKeyBytes = 1700;

    [Fact]
    public void EveryIndexedColumn_IsBounded()
    {
        // An unbounded string maps to nvarchar(max), which SQL Server cannot index at all. This is
        // the rule that BackendUrl broke: MediaProxyApi resolves every media request through it.
        var unbounded = IndexedProperties()
            .Where(property => property.ClrType == typeof(string) && property.GetMaxLength() is null)
            .Select(Describe)
            .ToList();

        unbounded.Should().BeEmpty("an indexed string column must have a max length or it maps to nvarchar(max)");
    }

    [Fact]
    public void EveryUniqueIndexKey_FitsWithinSqlServersKeyLimit()
    {
        // UrlRedirects.SourcePath broke this: nvarchar(1000) is 2000 bytes, so the index was
        // created with only a warning and then failed at insert time for any row that used the
        // full length the validator allowed.
        var oversized = Model().GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .Where(index => index.IsUnique)
            .Select(index => new
            {
                Index = string.Join(", ", index.Properties.Select(Describe)),
                Bytes = index.Properties.Sum(KeyBytes)
            })
            .Where(candidate => candidate.Bytes > MaxNonclusteredKeyBytes)
            .ToList();

        oversized.Should().BeEmpty(
            $"a unique index key must fit in {MaxNonclusteredKeyBytes} bytes or inserts fail once a row actually uses the width");
    }

    [Fact]
    public void MediaFileBackendUrl_IsUniquelyIndexed()
    {
        // Every media request resolves on this column, so without an index each image fetch on the
        // public site is a full scan of MediaFiles. Unique because the storage key already is.
        var mediaFile = Model().FindEntityType(typeof(MediaFile))!;

        mediaFile.GetIndexes()
            .Should().Contain(index =>
                index.IsUnique
                && index.Properties.Count == 1
                && index.Properties[0].Name == nameof(MediaFile.BackendUrl));
    }

    [Fact]
    public void BlogPosts_AreIndexedForThePublicListingQuery()
    {
        // Every public listing filters IsPublished plus a non-null PublishedAt and then orders by
        // PublishedAt descending. The two scheduling indexes that existed served a background job
        // running twice a minute; this one serves every visitor.
        var blogPost = Model().FindEntityType(typeof(BlogPost))!;

        var index = blogPost.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(BlogPost.IsPublished), nameof(BlogPost.PublishedAt)]));

        index.Should().NotBeNull("the query behind every public page needs a supporting index");
        index!.IsDescending.Should().Equal([false, true], "the listing orders by PublishedAt descending");
    }

    [Theory]
    [InlineData(typeof(AppUser), nameof(AppUser.PasswordHash))]
    [InlineData(typeof(BlogPost), nameof(BlogPost.SeoTitle))]
    [InlineData(typeof(BlogPost), nameof(BlogPost.SeoDescription))]
    [InlineData(typeof(BlogPost), nameof(BlogPost.SeoKeywords))]
    [InlineData(typeof(BlogPost), nameof(BlogPost.OgImageUrl))]
    [InlineData(typeof(Page), nameof(Page.SeoTitle))]
    [InlineData(typeof(Page), nameof(Page.SeoDescription))]
    [InlineData(typeof(Page), nameof(Page.SeoKeywords))]
    [InlineData(typeof(Page), nameof(Page.OgImageUrl))]
    public void ColumnsOfKnownWidth_AreNotStoredOffRow(Type entityType, string propertyName)
    {
        // nvarchar(max) for a value whose length is known pushes it off-row and costs page loads
        // on every query that materialises the whole entity — which the admin listings do.
        var property = Model().FindEntityType(entityType)!.FindProperty(propertyName)!;

        property.GetMaxLength().Should().NotBeNull($"{entityType.Name}.{propertyName} has a known maximum width");
        property.GetMaxLength().Should().BeLessThanOrEqualTo(4000, "beyond 4000 SQL Server stores nvarchar off-row");
    }

    [Fact]
    public void ContentColumns_StayUnbounded()
    {
        // The counterpart to the rule above: post and page bodies, AI messages and the settings
        // value are genuinely unbounded, and capping them would be the wrong fix.
        var model = Model();

        model.FindEntityType(typeof(BlogPost))!.FindProperty(nameof(BlogPost.Content))!
            .GetMaxLength().Should().BeNull();
        model.FindEntityType(typeof(Page))!.FindProperty(nameof(Page.Content))!
            .GetMaxLength().Should().BeNull();
        model.FindEntityType(typeof(AiMessage))!.FindProperty(nameof(AiMessage.Content))!
            .GetMaxLength().Should().BeNull();
        model.FindEntityType(typeof(SiteSetting))!.FindProperty(nameof(SiteSetting.Value))!
            .GetMaxLength().Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(BlogPost))]
    [InlineData(typeof(Page))]
    public void EditableContent_CarriesAConcurrencyToken(Type entityType)
    {
        // Two admins on the same post — or the Blazor and MAUI clients, both shipped — was a
        // silent last-write-wins clobber with no conflict surfaced anywhere.
        var entity = Model().FindEntityType(entityType)!;

        // Application-generated rather than a store-generated rowversion, so it behaves the same on
        // the in-memory provider as on SQL Server — see IConcurrencyStamped for why.
        entity.GetProperties().Should().Contain(
            property => property.IsConcurrencyToken
                && property.Name == nameof(IConcurrencyStamped.ConcurrencyStamp),
            $"{entityType.Name} is edited from more than one client");
    }

    private static IEnumerable<IProperty> IndexedProperties() =>
        Model().GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .SelectMany(index => index.Properties)
            .Concat(Model().GetEntityTypes()
                .SelectMany(entity => entity.GetKeys())
                .SelectMany(key => key.Properties))
            .Distinct();

    private static int KeyBytes(IProperty property)
    {
        if (property.ClrType == typeof(string))
        {
            var maxLength = property.GetMaxLength();
            if (maxLength is null)
                return int.MaxValue / 2; // nvarchar(max): unindexable, force a failure
            return maxLength.Value * (property.IsUnicode() == false ? 1 : 2);
        }

        return property.ClrType switch
        {
            var type when type == typeof(Guid) => 16,
            var type when type == typeof(bool) => 1,
            var type when type == typeof(int) => 4,
            var type when type == typeof(long) => 8,
            var type when type == typeof(DateTime) || type == typeof(DateTime?) => 8,
            var type when type == typeof(byte[]) => property.GetMaxLength() ?? 8,
            _ => 8
        };
    }

    private static string Describe(IProperty property) =>
        $"{property.DeclaringType.ShortName()}.{property.Name}";

    private static IModel Model()
    {
        // A real relational provider, so the assertions see the same facets SQL Server would.
        // No connection is opened — building the model does not need one.
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseSqlServer("Server=localhost;Database=BlogItSchemaTests;User Id=x;Password=y;TrustServerCertificate=True")
            .Options;
        using var db = new BlogItDbContext(options);
        // The design-time model rather than db.Model: index sort order is not kept in the
        // read-optimized runtime model and throws when asked for.
        return db.GetService<IDesignTimeModel>().Model;
    }
}
