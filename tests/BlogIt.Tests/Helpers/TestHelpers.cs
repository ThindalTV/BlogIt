using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BlogIt.Tests.Helpers;

public static class TestHelpers
{
    /// <summary>Creates a valid JWT for the given user, signed with the test secret.</summary>
    /// <param name="securityStamp">
    /// Defaults to the stamp <see cref="SeedUserAsync"/> writes, so a token minted here matches
    /// the seeded row. Pass something else — or null, to omit the claim entirely — to build the
    /// kind of token authentication must reject.
    /// </param>
    public static string CreateToken(
        Guid userId,
        string username = "testuser",
        string? securityStamp = BlogItSampleFactory.DefaultTestSecurityStamp)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(BlogItSampleFactory.TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim("displayName", "Test User"),
        ];
        if (securityStamp is not null)
            claims.Add(new Claim(BlogItClaimTypes.SecurityStamp, securityStamp));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Adds a bearer token to the HttpClient for all requests.</summary>
    public static HttpClient WithAuth(
        this HttpClient client,
        Guid userId,
        string username = "testuser",
        string? securityStamp = BlogItSampleFactory.DefaultTestSecurityStamp)
    {
        var token = CreateToken(userId, username, securityStamp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seeds a user + hashed password in the test DB and returns their ID.</summary>
    public static async Task<Guid> SeedUserAsync(this BlogItSampleFactory factory,
        string username = "testuser", string password = "Password1!")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var user = new AppUser
        {
            Username = username,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            SecurityStamp = BlogItSampleFactory.DefaultTestSecurityStamp
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(BlogIt.Shared.SettingKeys.JwtSecret, BlogItSampleFactory.TestJwtSecret);
        return user.Id;
    }

    /// <summary>Seeds a published blog post in the test DB.</summary>
    public static async Task<BlogPost> SeedPostAsync(this BlogItSampleFactory factory,
        Guid authorId, bool published = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var post = new BlogPost
        {
            Title = "Test Post",
            Slug = $"test-post-{Guid.NewGuid():N}",
            Summary = "A test summary",
            Content = "# Test\n\nContent here.",
            IsPublished = published,
            HasBeenPublished = published,
            PublishedAt = published ? DateTime.UtcNow.AddDays(-1) : null,
            AuthorId = authorId
        };
        db.BlogPosts.Add(post);
        await db.SaveChangesAsync();
        return post;
    }
}
