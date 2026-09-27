using System.Net.Http.Json;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Pins the server contract the admin's Pages and Media screens now depend on: rows past the first
/// window are reachable via <c>page</c>, and <c>q</c> searches the whole table rather than the page
/// the client happens to be holding.
/// </summary>
public class AdminListPagingContractTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task GetPages_SecondPage_ReturnsRowsPastTheFirstWindow()
    {
        var userId = await factory.SeedUserAsync($"paging_pages_{Guid.NewGuid():N}");
        var marker = $"pagemarker{Guid.NewGuid():N}";
        await SeedPagesAsync(marker, count: 25);
        var client = factory.CreateClient().WithAuth(userId);

        var first = await client.GetFromJsonAsync<PagedResult<PageDto>>(
            $"/api/pages?page=1&pageSize=20&q={marker}");
        var second = await client.GetFromJsonAsync<PagedResult<PageDto>>(
            $"/api/pages?page=2&pageSize=20&q={marker}");

        first!.TotalCount.Should().Be(25);
        first.Items.Should().HaveCount(20);
        second!.Items.Should().HaveCount(5);
        second.Page.Should().Be(2);
        second.Items.Select(p => p.Id).Should().NotIntersectWith(first.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task GetMedia_ServerSideSearch_FindsAFileOutsideTheFirstPage()
    {
        // This is the reported symptom: the picker filtered its own 20-row window, so a file that
        // existed but sorted past row 20 came back as "No media files found."
        var userId = await factory.SeedUserAsync($"paging_media_{Guid.NewGuid():N}");
        var marker = $"mediamarker{Guid.NewGuid():N}";
        await SeedMediaAsync(userId, marker, count: 25);
        var client = factory.CreateClient().WithAuth(userId);

        // Oldest upload sorts last (the list is UploadedAt descending), so file 00 is row 25.
        var needle = $"{marker}-00";
        var unfiltered = await client.GetFromJsonAsync<PagedResult<MediaFileDto>>("/api/media?page=1&pageSize=20");
        var searched = await client.GetFromJsonAsync<PagedResult<MediaFileDto>>(
            $"/api/media?page=1&pageSize=20&q={needle}");

        unfiltered!.Items.Should().NotContain(m => m.Title == needle);
        searched!.TotalCount.Should().Be(1);
        searched.Items.Should().ContainSingle().Which.Title.Should().Be(needle);
    }

    private async Task SeedPagesAsync(string marker, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.Pages.Add(new Page
            {
                Title = $"{marker}-{i:D2}",
                Slug = $"{marker}-{i:D2}",
                Content = "Content",
                UpdatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedMediaAsync(Guid uploaderId, string marker, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.MediaFiles.Add(new MediaFile
            {
                Title = $"{marker}-{i:D2}",
                FileName = $"{marker}-{i:D2}.png",
                ContentType = "image/png",
                BackendUrl = $"{marker}-{i:D2}.png",
                PublicPath = $"/media/{marker}-{i:D2}.png",
                SizeBytes = 1024,
                UploadedAt = DateTime.UtcNow.AddMinutes(i),
                UploadedByUserId = uploaderId
            });
        }
        await db.SaveChangesAsync();
    }
}
