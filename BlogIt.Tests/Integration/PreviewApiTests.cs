using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

public class PreviewApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task CreatePostPreview_RequiresAuthentication()
    {
        var response = await factory.CreateClient()
            .PostAsync($"/api/previews/posts/{Guid.NewGuid()}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostPreview_RefreshesInRedeemingBrowserButRejectsReplay()
    {
        var userId = await factory.SeedUserAsync($"previewer_{Guid.NewGuid():N}");
        var post = await factory.SeedPostAsync(userId, published: false);
        var client = factory.CreateClient().WithAuth(userId);

        var createResponse = await client.PostAsync($"/api/previews/posts/{post.Id}", null);
        createResponse.EnsureSuccessStatusCode();
        var preview = await createResponse.Content.ReadFromJsonAsync<PreviewLinkResponse>();

        preview.Should().NotBeNull();
        preview!.Url.Should().StartWith($"/{post.CreatedAt.Year}/{post.Slug}?preview=");
        (await client.GetStringAsync(preview.Url)).Should().Contain("DRAFT PREVIEW");
        (await client.GetStringAsync(preview.Url)).Should().Contain("DRAFT PREVIEW");

        var replayClient = factory.CreateClient();
        (await replayClient.GetStringAsync(preview.Url)).Should().Contain("Post Not Found");
    }

    [Fact]
    public async Task PagePreview_RendersUnpublishedPage()
    {
        var userId = await factory.SeedUserAsync($"page_previewer_{Guid.NewGuid():N}");
        var page = new Page
        {
            Title = "Private page",
            Slug = $"private-{Guid.NewGuid():N}",
            Content = "# Private preview",
            IsPublished = false
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
            db.Pages.Add(page);
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient().WithAuth(userId);
        var createResponse = await client.PostAsync($"/api/previews/pages/{page.Id}", null);
        createResponse.EnsureSuccessStatusCode();
        var preview = await createResponse.Content.ReadFromJsonAsync<PreviewLinkResponse>();

        preview.Should().NotBeNull();
        (await client.GetStringAsync(preview!.Url)).Should().Contain("DRAFT PREVIEW");
    }

    [Fact]
    public async Task IncorrectPostYear_RedirectsToPublishedYear()
    {
        var userId = await factory.SeedUserAsync($"legacy_previewer_{Guid.NewGuid():N}");
        var post = await factory.SeedPostAsync(userId, published: true);
        var expectedPath = $"/{post.PublishedAt!.Value.Year}/{post.Slug}";

        var response = await factory.CreateClient()
            .GetAsync($"/{post.PublishedAt.Value.Year - 1}/{post.Slug}");

        response.EnsureSuccessStatusCode();
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().Be(expectedPath);
    }
}
