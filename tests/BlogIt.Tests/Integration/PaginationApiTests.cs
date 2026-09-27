using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.Helpers;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// <c>page</c> and <c>pageSize</c> arrive straight off the query string on every admin list
/// endpoint and used to be passed to EF unexamined: <c>?page=0</c> became <c>Skip(-20)</c>, which
/// SQL Server refuses outright, so a mistyped URL was a 500; any <c>pageSize</c> at all was
/// honoured, and these listings materialize whole entities.
/// </summary>
/// <remarks>
/// These assert the clamped values the response echoes back rather than the absence of a 500,
/// because the test suite runs on EF's InMemory provider, where a negative <c>Skip</c> is
/// LINQ-to-objects and silently does nothing. The contract is what has to hold on both providers.
/// </remarks>
public class PaginationApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    // Every paged admin listing, so a fourth one added later without a clamp fails here.
    public static TheoryData<string> PagedListEndpoints() => new("posts", "pages", "media");

    [Theory]
    [MemberData(nameof(PagedListEndpoints))]
    public async Task List_WithPageBelowOne_ClampsToTheFirstPage(string resource)
    {
        var client = await AuthedClientAsync($"paging_zero_{resource}");

        var response = await client.GetAsync($"/api/{resource}?page=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPagingAsync(response)).Page.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(PagedListEndpoints))]
    public async Task List_WithNegativePage_ClampsToTheFirstPage(string resource)
    {
        var client = await AuthedClientAsync($"paging_negative_{resource}");

        var response = await client.GetAsync($"/api/{resource}?page=-7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPagingAsync(response)).Page.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(PagedListEndpoints))]
    public async Task List_WithAnEnormousPageSize_ClampsToTheMaximum(string resource)
    {
        var client = await AuthedClientAsync($"paging_huge_{resource}");

        var response = await client.GetAsync($"/api/{resource}?pageSize=1000000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPagingAsync(response)).PageSize.Should().Be(Pagination.MaxPageSize);
    }

    [Theory]
    [MemberData(nameof(PagedListEndpoints))]
    public async Task List_WithPageSizeBelowOne_ClampsToOne(string resource)
    {
        var client = await AuthedClientAsync($"paging_small_{resource}");

        var response = await client.GetAsync($"/api/{resource}?pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadPagingAsync(response)).PageSize.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(PagedListEndpoints))]
    public async Task List_WithParametersInRange_LeavesThemAlone(string resource)
    {
        var client = await AuthedClientAsync($"paging_ok_{resource}");

        var response = await client.GetAsync($"/api/{resource}?page=2&pageSize={Pagination.MaxPageSize}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paging = await ReadPagingAsync(response);
        paging.Page.Should().Be(2);
        paging.PageSize.Should().Be(Pagination.MaxPageSize);
    }

    /// <summary>Reads only the paging fields of a <c>PagedResult&lt;T&gt;</c>, so one helper covers
    /// all three item types.</summary>
    private static async Task<PagingEcho> ReadPagingAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<PagingEcho>())!;

    private sealed record PagingEcho(int TotalCount, int Page, int PageSize);

    private async Task<HttpClient> AuthedClientAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        return factory.CreateClient().WithAuth(userId, username);
    }
}
