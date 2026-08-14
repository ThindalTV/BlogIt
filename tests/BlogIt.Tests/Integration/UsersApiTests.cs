using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class UsersApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task GetUsers_RequiresAuth()
    {
        var response = await factory.CreateClient().GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_WithAuth_Returns201()
    {
        var userId = await factory.SeedUserAsync("admin_user");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreateUserRequest("newuser", "New User", "Password1!");
        var response = await client.PostAsJsonAsync("/api/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = await response.Content.ReadFromJsonAsync<AppUserDto>();
        user!.Username.Should().Be("newuser");
    }

    [Theory]
    [InlineData("short1A")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public async Task CreateUser_WithWeakPassword_ReturnsBadRequest(string weakPassword)
    {
        var userId = await factory.SeedUserAsync($"admin_weak_pw_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreateUserRequest("weakpassworduser", "Weak Password User", weakPassword);
        var response = await client.PostAsJsonAsync("/api/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteUser_CannotDeleteSelf()
    {
        var userId = await factory.SeedUserAsync("self_deleter");
        var client = factory.CreateClient().WithAuth(userId, "self_deleter");

        var response = await client.DeleteAsync($"/api/users/{userId}");
        // Should be forbidden — cannot delete own account
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
