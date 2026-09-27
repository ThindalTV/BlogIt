using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// A JWT is only as revocable as the checks made when it is presented. Nothing used to invalidate
/// one before its natural expiry — a jti was minted and never stored or consulted — so a changed
/// password left old sessions live, and a deleted account kept full access for up to the token
/// lifetime, long enough to re-create itself through POST /api/users.
/// </summary>
public class TokenRevocationTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task ChangingPassword_InvalidatesTokensIssuedBeforeTheChange()
    {
        var username = $"revoke_pw_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username);

        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password1!", "NewPassword1!"));
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        // Same bearer token, now stale: the account's stamp moved when the password did.
        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangingPassword_IssuesAWorkingTokenOnNextLogin()
    {
        var username = $"revoke_relogin_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username);

        await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password1!", "NewPassword1!"));

        var login = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "NewPassword1!"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        var fresh = factory.CreateClient();
        fresh.DefaultRequestHeaders.Authorization = new("Bearer", token);
        (await fresh.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeletingAUser_InvalidatesTheirExistingToken()
    {
        var adminName = $"revoke_admin_{Guid.NewGuid():N}";
        var victimName = $"revoke_victim_{Guid.NewGuid():N}";
        var adminId = await factory.SeedUserAsync(adminName);
        var victimId = await factory.SeedUserAsync(victimName);

        var victimClient = factory.CreateClient().WithAuth(victimId, victimName);
        (await victimClient.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);

        var adminClient = factory.CreateClient().WithAuth(adminId, adminName);
        var delete = await adminClient.DeleteAsync($"/api/users/{victimId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Without a lookup of the sub claim, this token stayed good until expiry — up to 24 hours
        // for a deleted account, ample time to POST /api/users itself back into existence.
        (await victimClient.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenForAUserThatNeverExisted_IsRejected()
    {
        await factory.SeedUserAsync($"revoke_ghost_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(Guid.NewGuid(), "ghost");

        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithNoSecurityStampClaim_IsRejected()
    {
        // The shape every token minted before this change had.
        var username = $"revoke_nostamp_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username, securityStamp: null);

        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithAStaleSecurityStamp_IsRejected()
    {
        var username = $"revoke_stale_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username, securityStamp: "a-previous-stamp");

        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LoginToken_CarriesTheStoredSecurityStamp()
    {
        var username = $"revoke_claim_{Guid.NewGuid():N}";
        await factory.SeedUserAsync(username);

        var login = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username, "Password1!"));
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        var stamp = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(token)
            .Claims
            .FirstOrDefault(claim => claim.Type == BlogItClaimTypes.SecurityStamp)?.Value;

        stamp.Should().Be(BlogItSampleFactory.DefaultTestSecurityStamp);
    }

    [Fact]
    public async Task ChangingPassword_MovesTheStoredSecurityStamp()
    {
        var username = $"revoke_stamp_moves_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username);

        await client.PostAsJsonAsync(
            "/api/auth/change-password",
            new ChangePasswordRequest("Password1!", "NewPassword1!"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var stamp = await db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();

        stamp.Should().NotBe(BlogItSampleFactory.DefaultTestSecurityStamp);
    }
}
