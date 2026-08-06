using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlogIt.Tests.Integration;

public sealed class AdminAssetIntegrationTests(BlogItSampleFactory factory)
    : IClassFixture<BlogItSampleFactory>
{
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task DefaultAdminPath_ServesRedirectShellAssetsConfigAndDeepLinks()
    {
        var redirect = await _client.GetAsync("/blogit");
        redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        redirect.Headers.Location.Should().Be("/blogit/");

        var shell = await _client.GetStringAsync("/blogit/");
        shell.Should().Contain("<title>BlogIt Admin</title>");
        shell.Should().Contain("<base href=\"/blogit/\" />");

        var directIndex = await _client.GetStringAsync("/blogit/index.html");
        directIndex.Should().Contain("<base href=\"/blogit/\" />");

        var config = await _client.GetFromJsonAsync<BlogItAdminBootstrapConfig>(
            $"/blogit/{BlogItAdminBootstrapConfig.RelativePath}");
        config.Should().Be(new BlogItAdminBootstrapConfig("/api"));

        var framework = await _client.GetAsync(
            "/blogit/_framework/blazor.webassembly.js");
        framework.StatusCode.Should().Be(HttpStatusCode.OK);
        framework.Content.Headers.ContentType?.MediaType
            .Should().Be("text/javascript");
        framework.Headers.CacheControl?.NoCache
            .Should().BeTrue();

        var deepLink = await _client.GetStringAsync("/blogit/posts/edit/123");
        deepLink.Should().Contain("<title>BlogIt Admin</title>");

        var internalPath = await _client.GetAsync(
            "/BlogItAdminAssets/index.html");
        internalPath.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
