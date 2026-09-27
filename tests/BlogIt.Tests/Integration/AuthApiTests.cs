using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class AuthApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var userId = await factory.SeedUserAsync("alice", "P@ssw0rd!");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("alice", "P@ssw0rd!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.Username.Should().Be("alice");
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        await factory.SeedUserAsync("bob", "correct-password");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("bob", "wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest("nobody", "password"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithValidCurrentPassword_Succeeds()
    {
        var userId = await factory.SeedUserAsync("charlie", "OldPass1!");
        var client = factory.CreateClient().WithAuth(userId, "charlie");

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("OldPass1!", "NewPass2@"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest()
    {
        var userId = await factory.SeedUserAsync("dave", "RealPass1!");
        var client = factory.CreateClient().WithAuth(userId, "dave");

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("WrongPass!", "NewPass2@"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsBadRequest()
    {
        var userId = await factory.SeedUserAsync("erin", "RealPass1!");
        var client = factory.CreateClient().WithAuth(userId, "erin");

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("RealPass1!", "weak"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WithoutAuth_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("any", "any"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
