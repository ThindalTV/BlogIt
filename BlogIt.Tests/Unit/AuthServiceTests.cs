using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class AuthServiceTests
{
    private sealed class TestDbContextFactory(
        DbContextOptions<BlogItDbContext> options) : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);
    }

    private (BlogItDbContext Db, SettingsService Settings) CreateSubject()
    {
        var opts = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new BlogItDbContext(opts);
        db.SiteSettings.AddRange(
            new SiteSetting { Key = BlogIt.Shared.SettingKeys.JwtSecret, Value = "unit-test-secret-long-enough-for-hmac" },
            new SiteSetting { Key = BlogIt.Shared.SettingKeys.JwtExpiryMinutes, Value = "60" }
        );
        db.SaveChanges();
        return (db, new SettingsService(new TestDbContextFactory(opts)));
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsToken()
    {
        var (db, settings) = CreateSubject();
        db.Users.Add(new AppUser
        {
            Username = "alice",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret")
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("alice", "secret"));

        result.Should().NotBeNull();
        result!.Token.Should().NotBeEmpty();
        result.Username.Should().Be("alice");
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        var (db, settings) = CreateSubject();
        db.Users.Add(new AppUser
        {
            Username = "bob",
            DisplayName = "Bob",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct")
        });
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("bob", "wrong"));

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WithUnknownUser_ReturnsNull()
    {
        var (db, settings) = CreateSubject();
        var service = new AuthService(db, settings);
        var result = await service.LoginAsync(new BlogIt.Shared.DTOs.LoginRequest("nobody", "pass"));
        result.Should().BeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_UpdatesHash()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "charlie",
            DisplayName = "Charlie",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var success = await service.ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("old", "new"));

        success.Should().BeTrue();
        var updated = await db.Users.FindAsync(user.Id);
        BCrypt.Net.BCrypt.Verify("new", updated!.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrent_ReturnsFalse()
    {
        var (db, settings) = CreateSubject();
        var user = new AppUser
        {
            Username = "dave",
            DisplayName = "Dave",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("real")
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new AuthService(db, settings);
        var success = await service.ChangePasswordAsync(user.Id,
            new BlogIt.Shared.DTOs.ChangePasswordRequest("wrong", "new"));

        success.Should().BeFalse();
    }

    [Fact]
    public void GenerateToken_ProducesValidJwt()
    {
        var (db, settings) = CreateSubject();
        var service = new AuthService(db, settings);
        var token = service.GenerateToken(Guid.NewGuid(), "user", "User", "a-secret-that-is-long-enough-for-hs256!!", 60);
        token.Should().NotBeEmpty();
        token.Split('.').Should().HaveCount(3); // JWT = header.payload.signature
    }
}
