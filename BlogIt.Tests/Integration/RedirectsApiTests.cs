using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlogIt.Tests.Integration;

public class RedirectsApiTests(BlogItWebFactory factory) : IClassFixture<BlogItWebFactory>
{
    [Fact]
    public async Task RedirectManagement_RequiresAuthentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/redirects");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ManualExternalRedirect_IsServedWithConfiguredStatus()
    {
        var userId = await factory.SeedUserAsync($"redirect_{Guid.NewGuid():N}");
        var source = $"/redir/{Guid.NewGuid():N}";
        var client = factory.CreateClient().WithAuth(userId);
        var createResponse = await client.PostAsJsonAsync(
            "/api/redirects",
            new SaveUrlRedirectRequest(source, "https://www.microsoft.com/", false));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var redirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await redirectClient.GetAsync($"{source}/");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location.Should().Be(new Uri("https://www.microsoft.com/"));
    }

    [Theory]
    [InlineData("/api/replacement")]
    [InlineData("/blogit/login")]
    [InlineData("/rss.xml")]
    [InlineData("//external.example/path")]
    public async Task ReservedSourcePath_IsRejected(string source)
    {
        var userId = await factory.SeedUserAsync($"reserved_{Guid.NewGuid():N}");
        var response = await factory.CreateClient().WithAuth(userId).PostAsJsonAsync(
            "/api/redirects",
            new SaveUrlRedirectRequest(source, "/destination", true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
